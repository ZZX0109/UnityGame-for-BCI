using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Neuralife.VirtualHand;

namespace Neuralife.VirtualHand.PlayTests
{
    /// <summary>
    /// PlayMode smoke coverage on the SAVED Demo scene. These tests verify that
    /// serialized wiring survives a real scene load and that the production
    /// TaskController can complete carry and both handoff directions with a
    /// single owner at a time. They do not claim visual/art acceptance.
    /// </summary>
    public class PlayModeTaskTests
    {
        const string SceneName = "Demo";

        [UnitySetUp]
        public IEnumerator LoadSavedDemo()
        {
            var op = SceneManager.LoadSceneAsync(SceneName);
            Assert.IsNotNull(op, "Demo must be registered in Build Settings; regenerate the scene first.");
            while (!op.isDone) yield return null;
            yield return null;
        }

        static TaskController FindTasks()
        {
            var tasks = UnityEngine.Object.FindObjectOfType<TaskController>();
            Assert.IsNotNull(tasks, "saved scene must contain TaskController");
            return tasks;
        }

        static Rigidbody PauseReplayAndGetBox()
        {
            var source = UnityEngine.Object.FindObjectOfType<ReplaySource>();
            Assert.IsNotNull(source, "saved scene must contain ReplaySource");
            source.Pause(); // prevent Update() from overwriting deterministic test intents
            var box = GameObject.Find("CarryBox");
            Assert.IsNotNull(box, "saved scene must contain CarryBox");
            var rb = box.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, "CarryBox must have a Rigidbody");
            return rb;
        }

        static Vector3[] HelperPath()
        {
            var box = GameObject.Find("CarryBox");
            var zone = GameObject.Find("DropZone");
            Assert.IsNotNull(box, "CarryBox missing");
            Assert.IsNotNull(zone, "DropZone missing");
            return new[]
            {
                box.transform.position + new Vector3(0f, 0.19f, 0.3f),
                zone.transform.position + Vector3.up * 0.06f,
            };
        }

        static void AssertPhysicalHold(TaskController tasks, bool leftHand)
        {
            Assert.IsTrue(tasks.TryMeasureTaskHold(leftHand, out var fit), "weighted skin measurement unavailable");
            Assert.LessOrEqual(fit.meanDistalFaceDistanceMm, 15f,
                               "mean index/middle/ring/little distal distance from the box side face");
            Assert.LessOrEqual(fit.insideVertexFraction, 0.04f, "too many weighted skin vertices penetrate the box");
            Assert.LessOrEqual(fit.maxPenetrationMm, 8f, "skin penetration into the box is too deep");
        }

        [UnityTest]
        public IEnumerator SavedScene_WiringSurvivesPlayMode()
        {
            var tasks = FindTasks();
            var bindings = UnityEngine.Object.FindObjectOfType<ReplayUIBindings>();
            Assert.IsNotNull(bindings, "saved scene must contain ReplayUIBindings");
            Assert.IsNotNull(tasks.replay, "TaskController.replay must survive scene load");
            Assert.IsNotNull(tasks.leftAnchor, "leftAnchor must survive scene load");
            Assert.IsNotNull(tasks.rightAnchor, "rightAnchor must survive scene load");
            Assert.IsNotNull(tasks.dropZone, "dropZone must survive scene load");
            Assert.IsNotNull(bindings.ui, "DemoUI reference must survive scene load");
            Assert.IsNotNull(bindings.tasks, "TaskController reference must survive scene load");
            Assert.IsNotNull(bindings.carryButton, "serialized carry button must survive scene load");
            Assert.IsNotNull(bindings.handoffL2RButton, "serialized L2R button must survive scene load");
            Assert.IsNotNull(bindings.handoffR2LButton, "serialized R2L button must survive scene load");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SavedDemo_NativeSpreadMovesOnlyItsFingerInBothDirections()
        {
            var tasks = FindTasks();
            tasks.replay.source.Pause();
            var drivers = new[] { tasks.replay.leftHand, tasks.replay.rightHand };
            string[] endpoints = { "index_03.R_end", "middle_03.R_end", "ring_03.R_end", "pinky_03.R_end" };
            int[] channels = { 7, 11, 15, 19 };

            var leftSweeps = new Vector3[4];
            foreach (var driver in drivers)
            {
                Assert.IsNotNull(driver.nativeProfile);
                var bones = driver.GetComponentsInChildren<Transform>(true);
                var tips = new Transform[4];
                for (int f = 0; f < endpoints.Length; f++)
                    foreach (var bone in bones)
                        if (bone.name.StartsWith(endpoints[f] + "_", System.StringComparison.Ordinal)) { tips[f] = bone; break; }
                for (int f = 0; f < tips.Length; f++) Assert.IsNotNull(tips[f], "missing distal endpoint " + endpoints[f]);

                var neutral = (float[])driver.ProfileOpen.Clone();
                for (int f = 0; f < channels.Length; f++) neutral[channels[f]] = 0.5f;
                driver.SetControls(neutral);
                yield return null;

                for (int active = 0; active < channels.Length; active++)
                {
                    var center = new Vector3[4];
                    for (int f = 0; f < tips.Length; f++) center[f] = tips[f].position;
                    Vector3 negative = Vector3.zero, positive = Vector3.zero;
                    for (int polarity = 0; polarity < 2; polarity++)
                    {
                        var sample = (float[])neutral.Clone();
                        sample[channels[active]] = polarity == 0 ? 0f : 1f;
                        driver.SetControls(sample);
                        yield return null;
                        var movement = new Vector3[4];
                        for (int f = 0; f < tips.Length; f++) movement[f] = tips[f].position - center[f];
                        Assert.Greater(movement[active].magnitude, 0.001f,
                            driver.name + " spread channel " + channels[active] + " must move its MCP finger tip");
                        for (int f = 0; f < tips.Length; f++)
                            if (f != active)
                                Assert.Less(movement[f].magnitude, 0.0001f,
                                    driver.name + " spread channel moved an unrelated finger");
                        if (polarity == 0) negative = movement[active]; else positive = movement[active];
                        driver.SetControls(neutral);
                        yield return null;
                    }
                    Vector3 sweep = positive - negative;
                    if (driver == drivers[0]) leftSweeps[active] = sweep;
                    else
                    {
                        Vector3 mirroredLeft = Vector3.Scale(leftSweeps[active], new Vector3(-1f, 1f, 1f));
                        Assert.Less((mirroredLeft - sweep).magnitude, 0.0002f, "left/right tip travel must mirror");
                    }
                    Debug.Log($"[SpreadCheck] {driver.name} channel={channels[active]} deltaMm={(sweep * 1000f).ToString("F3")} travelMm={sweep.magnitude * 1000f:F3}");
                    Assert.Less(Vector3.Dot(negative.normalized, positive.normalized), -0.9f,
                                "negative and positive spread must move in opposite directions");
                    Assert.Less(Mathf.Abs(negative.magnitude - positive.magnitude),
                                0.25f * Mathf.Max(negative.magnitude, positive.magnitude),
                                "spread travel should be symmetric around neutral");
                }
            }
            tasks.replay.Restart();
        }

        [UnityTest]
        public IEnumerator SavedDemo_PlaysEachGloveClipOnNativeHands()
        {
            var replay = FindTasks().replay;
            Assert.IsNotNull(replay.leftHand.nativeProfile);
            Assert.AreSame(replay.leftHand.nativeProfile, replay.rightHand.nativeProfile);
            Assert.IsTrue(replay.leftHand.IsMirrored);
            Assert.IsFalse(replay.rightHand.IsMirrored);
            var menus = UnityEngine.Object.FindObjectsOfType<Db8ClipMenu>();
            Assert.AreEqual(6, menus.Length);
            var loaded = new System.Collections.Generic.HashSet<string>();
            foreach (var menu in menus)
            {
                menu.button.onClick.Invoke();
                var data = replay.source.Data;
                Assert.AreEqual(menu.clipPath, System.IO.Path.GetFileName(data.filePath));
                Assert.IsTrue(loaded.Add(menu.clipPath));
                Assert.IsNotEmpty(data.frames);
                replay.Play();
                replay.source.Tick(data.Duration * 0.5);
                yield return null;
                Assert.AreEqual("glove", replay.CurrentSource);
                Assert.IsTrue(replay.IsMirroredDisplay);
                Assert.AreEqual(data.frames[0].subject + "|" + data.frames[0].run, replay.SubjectRun);
                replay.Pause();
                replay.RestoreCurrentTargets();
                var frame = data.frames.FindLast(f => f.time <= replay.Position);
                var tip = System.Array.Find(replay.leftHand.GetComponentsInChildren<Transform>(),
                    bone => ElenaNativeRigProfile.MatchesBone(bone.name, "index_03.R_end"));
                Vector3 actual = tip.position;
                string pictures = System.Environment.GetEnvironmentVariable("VIRTUALHAND_HOLD_PICTURES");
                string clip = System.IO.Path.GetFileNameWithoutExtension(menu.clipPath);
                if (!string.IsNullOrEmpty(pictures))
                {
                    // Allow the normal player loop to update the skinned mesh for this paused pose.
                    yield return null;
                    yield return null;
                    SaveCameraView(Camera.main, pictures, clip);
                }

                // Keep every measured flex value and compare just the four spread targets.
                var centered = (float[])frame.leftJoints.Clone();
                for (int i = 7; i < centered.Length; i += 4) centered[i] = 0.5f;
                replay.leftHand.SetControls(centered);
                float delta = Vector3.Distance(tip.position, actual);
                Assert.Greater(delta, 0.0002f, "the real glove sample must affect the index tip through spread");
                Debug.Log($"[Db8Spread] {clip} t={frame.time:F3} spread={frame.leftJoints[7]:F4} indexContributionMm={delta*1000f:F2}");
                replay.RestoreCurrentTargets();
            }
        }

        [UnityTest]
        public IEnumerator GloveReplay_IdentifiesAssistedDisplayAndRestoresPreview()
        {
            var buttons = Object.FindObjectOfType<ReplayUIBindings>();
            var tasks = buttons.tasks;
            buttons.db8Button.onClick.Invoke();
            buttons.handoffL2RButton.onClick.Invoke();
            buttons.playPauseButton.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual("glove", tasks.replay.CurrentSource);
            Assert.IsTrue(tasks.replay.IsMirroredDisplay, "the source contains a single measured hand");
            Assert.IsTrue(tasks.ControlsTaskHands);
            StringAssert.Contains("显示辅助姿态", buttons.ui.statusText.text);
            buttons.restartButton.onClick.Invoke();
            buttons.playPauseButton.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual("glove", tasks.replay.CurrentSource);
            Assert.IsFalse(tasks.ControlsTaskHands);
            StringAssert.Contains("左手镜像", buttons.ui.statusText.text);
        }

        [UnityTest]
        public IEnumerator RealReplay_CarriesAndHandsOffThroughSavedButtons()
        {
            var buttons = Object.FindObjectOfType<ReplayUIBindings>();
            var tasks = buttons.tasks;
            var replay = tasks.replay;
            var speed = Time.timeScale;
            try
            {
                Time.timeScale = 8f;
                for (int scenario = 0; scenario < 3; scenario++)
                {
                    if (scenario == 0) buttons.eegButton.onClick.Invoke();
                    else
                    {
                        buttons.subjectS003Button.onClick.Invoke();
                        buttons.labelsButton.onClick.Invoke();
                    }
                    var startL = tasks.leftAnchor.position;
                    var startR = tasks.rightAnchor.position;
                    var action = scenario == 0 ? buttons.carryButton :
                        (scenario == 1 ? buttons.handoffL2RButton : buttons.handoffR2LButton);
                    action.onClick.Invoke();
                    Assert.IsTrue(tasks.TryMeasureTaskHold(true, out var openFit));
                    Assert.That(openFit.maxPenetrationMm, Is.LessThan(1f), "open fingers intrude while awaiting grip");
                    buttons.playPauseButton.onClick.Invoke();
                    bool checkedHold = false, checkedSender = false;
                    var box = tasks.boxVisual.GetComponent<BoxCollider>();
                    float tableY = box.transform.TransformPoint(box.center - Vector3.up * (box.size.y * 0.5f)).y;
                    float deadline = Time.realtimeSinceStartup + 45f;
                    while (tasks.CurrentStage != TaskController.Stage.Done && replay.source.IsPlaying &&
                           Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                        if (scenario != 0 && !checkedSender && tasks.CurrentStage == TaskController.Stage.Transfer)
                        {
                            CheckHandoffSupport(tasks, scenario == 1, tableY, sender: true);
                            checkedSender = true;
                        }
                        if (checkedHold || tasks.CurrentStage != TaskController.Stage.Place) continue;
                        checkedHold = true;
                        Assert.AreEqual(scenario == 0 ? "prediction" : "labels", replay.CurrentSource);
                        if (scenario != 1) AssertPhysicalHold(tasks, true);
                        if (scenario != 2) AssertPhysicalHold(tasks, false);
                        if (scenario != 0)
                            CheckHandoffSupport(tasks, scenario == 2, tableY, sender: false);
                        buttons.playPauseButton.onClick.Invoke();
                        var position = tasks.boxVisual.transform.position;
                        double clock = replay.Position;
                        yield return null;
                        yield return null;
                        Assert.AreEqual(clock, replay.Position);
                        Assert.AreEqual(position, tasks.boxVisual.transform.position);
                        StringAssert.Contains("任务辅助握持", buttons.ui.statusText.text);
                        CaptureHeldView(tasks, scenario);
                        buttons.playPauseButton.onClick.Invoke();
                    }
                    if (scenario != 0) Assert.IsTrue(checkedSender, "sender must support the box before transfer");
                    Assert.IsTrue(checkedHold, "real replay must reach target with a holder, scenario " + scenario);
                    Assert.AreEqual(TaskController.Stage.Done, tasks.CurrentStage, "real replay must release in zone");
                    Assert.IsFalse(tasks.ControlsTaskHands, "release must return the hands to replay immediately");
                    Assert.That(Vector3.Distance(startL, tasks.leftAnchor.position), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(startR, tasks.rightAnchor.position), Is.LessThan(0.0001f));
                    yield return new WaitForSeconds(1.5f);
                    Vector3 settled = tasks.boxVisual.transform.position - tasks.dropZone.position;
                    Assert.Less(new Vector2(settled.x, settled.z).magnitude, 0.18f,
                                "released cube must settle on the target disk");
                    buttons.restartButton.onClick.Invoke();
                    Assert.AreEqual(TaskController.TaskType.None, tasks.Type);
                    Assert.That(Vector3.Distance(startL, tasks.leftAnchor.position), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(startR, tasks.rightAnchor.position), Is.LessThan(0.0001f));
                }
            }
            finally { Time.timeScale = speed; }
        }

        static void CheckHandoffSupport(TaskController tasks, bool left, float tableY, bool sender)
        {
            Assert.IsTrue(tasks.HasCalibratedHandoffFit(left), "single-hand wrist fit missing in saved scene");
            Assert.IsTrue(tasks.TryMeasureThumbPadContact(left, out var thumb));
            Assert.GreaterOrEqual(thumb.topContactVertices, 8, "thumb needs a facing patch within 3 mm of the box top");
            Assert.IsTrue(tasks.TryMeasureTaskHold(left, out var fingers));
            Assert.LessOrEqual(fingers.maxPenetrationMm, 8f);
            Assert.GreaterOrEqual(fingers.lowestWorldY, tableY - 0.002f, "hand intersects the training table");
            Debug.Log($"[HandoffReplay] sender={sender} left={left} topPatch={thumb.topContactVertices} gap={thumb.closestTopGapMm:F3}mm depth={fingers.maxPenetrationMm:F2}mm tableClearance={(fingers.lowestWorldY-tableY)*1000f:F2}mm");

            string pictures = System.Environment.GetEnvironmentVariable("VIRTUALHAND_HOLD_PICTURES");
            if (string.IsNullOrEmpty(pictures)) return;
            var cam = Camera.main;
            Vector3 position = cam.transform.position;
            Quaternion rotation = cam.transform.rotation;
            Vector3 center = tasks.boxVisual.transform.position;
            try
            {
                cam.transform.position = center + new Vector3(left ? 0.17f : -0.17f, 0.21f, 0.26f);
                cam.transform.LookAt(center + Vector3.up * 0.018f);
                string name = (left ? "left" : "right") + (sender ? "-sender" : "-receiver");
                SaveCameraView(cam, pictures, name);
            }
            finally { cam.transform.SetPositionAndRotation(position, rotation); }
        }

        static void CaptureHeldView(TaskController tasks, int scenario)
        {
            // Optional evidence from the running game camera during this actual replay.
            string directory = System.Environment.GetEnvironmentVariable("VIRTUALHAND_HOLD_PICTURES");
            if (string.IsNullOrEmpty(directory)) return;
            string action = scenario == 0 ? "carry" : (scenario == 1 ? "L-to-R" : "R-to-L");
            SaveCameraView(Camera.main, directory, action);
            bool isLeft = scenario != 1;
            if (tasks.TryMeasureTaskHold(isLeft, out var fit))
                Debug.Log($"[HeldView] {action} {tasks.replay.CurrentSource} {tasks.replay.SubjectRun} t={tasks.replay.Position:F2} side={(isLeft ? "L" : "R")} index={fit.indexFaceDistanceMm:F2}mm middle={fit.middleFaceDistanceMm:F2}mm mean={fit.meanDistalFaceDistanceMm:F2}mm maxDepth={fit.maxPenetrationMm:F2}mm");
            if (tasks.TryMeasureThumbPadContact(isLeft, out var pad))
                Debug.Log($"[HeldViewPad] {action} side={(isLeft ? "L" : "R")} volarSamples={pad.thumbPadVertexCount}/{pad.indexPadVertexCount} adjacent={pad.adjacentFaceGapMm:F2}mm facing={pad.adjacentFaceFacing:F2} padGap={pad.meanNearestPadGapMm:F2}mm opposite={pad.opposingPadNormals:F2} face={pad.adjacentFace}");
        }

        static void SaveCameraView(Camera cam, string directory, string action)
        {
            var rt = RenderTexture.GetTemporary(1200, 900, 24);
            var previous = RenderTexture.active;
            var previousTarget = cam.targetTexture;
            var rect = cam.rect;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                cam.rect = new Rect(0, 0, 1, 1);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, action + ".png"), tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = previousTarget;
                cam.rect = rect;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(tex);
            }
        }

        [UnityTest]
        public IEnumerator ManualPinch_BothHandsHaveOpposingPadContact()
        {
            var tasks = FindTasks();
            var replay = tasks.replay;
            replay.source.Pause();
            var ui = Object.FindObjectOfType<DemoUI>();
            ui.PreviewPinch();
            yield return new WaitForSeconds(1.6f);
            Assert.AreEqual("manual", replay.CurrentSource);
            StringAssert.Contains("双手捏握", ui.statusText.text);
            Assert.IsEmpty(ui.warningText.text);
            foreach (bool left in new[] { true, false })
            {
                Assert.IsTrue(tasks.TryMeasureThumbPadContact(left, out var pad));
                Debug.Log($"[PresetPinch] left={left} samples={pad.thumbPadVertexCount}/{pad.indexPadVertexCount} nearest={pad.closestPadGapMm:F2}mm mean={pad.meanNearestPadGapMm:F2}mm normals={pad.opposingPadNormals:F2} contact={pad.thumbContactVertices}/{pad.indexContactVertices} maxLocalOverlap={pad.maxPadOverlapMm:F2}mm");
                Assert.That(pad.thumbPadVertexCount, Is.GreaterThanOrEqualTo(6));
                Assert.That(pad.indexPadVertexCount, Is.GreaterThanOrEqualTo(6));
                Assert.Less(pad.closestPadGapMm, 1.5f, "pads should visibly meet");
                Assert.GreaterOrEqual(pad.thumbContactVertices, 4, "thumb needs a facing contact patch within 3 mm");
                Assert.GreaterOrEqual(pad.indexContactVertices, 4, "index needs a facing contact patch within 3 mm");
                Assert.Less(pad.maxPadOverlapMm, 1f, "opposing local pad surfaces overlap");
            }
            string pictures = System.Environment.GetEnvironmentVariable("VIRTUALHAND_HOLD_PICTURES");
            if (!string.IsNullOrEmpty(pictures))
            {
                var cam = Camera.main;
                SaveCameraView(cam, pictures, "manual-pinch");
                var wrist = replay.rightHand.transform;
                var bones = wrist.GetComponentsInChildren<Transform>();
                var thumb = System.Array.Find(bones, bone => bone.name.StartsWith("thumb_03.R_end"));
                var index = System.Array.Find(bones, bone => bone.name.StartsWith("index_03.R_end"));
                Vector3 focus = (thumb.position + index.position) * 0.5f;
                Vector3 position = cam.transform.position;
                Quaternion rotation = cam.transform.rotation;
                try
                {
                    cam.transform.position = focus + new Vector3(0.11f, 0.075f, 0.13f);
                    cam.transform.LookAt(focus);
                    SaveCameraView(cam, pictures, "manual-pinch-close");
                }
                finally { cam.transform.SetPositionAndRotation(position, rotation); }
            }
            replay.Restart();
        }

        [UnityTest]
        public IEnumerator Carry_CompletesWithContinuousRestInsideDropZone()
        {
            var tasks = FindTasks();
            var rb = PauseReplayAndGetBox();
            Vector3 leftAnchorStart = tasks.leftAnchor.position;
            Vector3 rightAnchorStart = tasks.rightAnchor.position;
            Vector3 boxStart = rb.position;
            Vector3 leftHandLocalStart = tasks.leftHand.transform.localPosition;
            Vector3 rightHandLocalStart = tasks.rightHand.transform.localPosition;
            Quaternion leftHandRotationStart = tasks.leftHand.transform.localRotation;
            Quaternion rightHandRotationStart = tasks.rightHand.transform.localRotation;

            tasks.Begin(TaskController.TaskType.Carry, HelperPath(), rb.gameObject);
            tasks.OnIntent("both", 0.016f);
            Assert.AreEqual(TaskController.Stage.Lift, tasks.CurrentStage);
            for (int frame = 0; frame < 20; frame++) yield return null;
            AssertPhysicalHold(tasks, true);
            AssertPhysicalHold(tasks, false);

            tasks.OnIntent("both", 5f);
            tasks.OnIntent("both", 5f);
            Assert.AreEqual(TaskController.Stage.Place, tasks.CurrentStage,
                            "carry must reach Place before release is allowed");

            tasks.OnIntent("rest", 0.4f);
            Assert.AreEqual(TaskController.Stage.Place, tasks.CurrentStage,
                            "short rest must not release");
            tasks.OnIntent("rest", 0.5f);
            Assert.AreEqual(TaskController.Stage.Done, tasks.CurrentStage,
                            "sustained valid rest in zone must release");
            Assert.IsFalse(rb.isKinematic, "Done must restore Rigidbody physics");
            tasks.End();
            Assert.That(Vector3.Distance(tasks.leftAnchor.position, leftAnchorStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(tasks.rightAnchor.position, rightAnchorStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(rb.position, boxStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(tasks.leftHand.transform.localPosition, leftHandLocalStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(tasks.rightHand.transform.localPosition, rightHandLocalStart), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(tasks.leftHand.transform.localRotation, leftHandRotationStart), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(tasks.rightHand.transform.localRotation, rightHandRotationStart), Is.LessThan(0.01f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Handoff_LToR_TransfersOwnershipWithoutDrop()
        {
            yield return RunHandoff(leftToRight: true);
        }

        [UnityTest]
        public IEnumerator Handoff_RToL_TransfersOwnershipWithoutDrop()
        {
            yield return RunHandoff(leftToRight: false);
        }

        IEnumerator RunHandoff(bool leftToRight)
        {
            var tasks = FindTasks();
            var rb = PauseReplayAndGetBox();
            string sender = leftToRight ? "left" : "right";
            string receiver = leftToRight ? "right" : "left";
            Vector3 leftAnchorStart = tasks.leftAnchor.position;
            Vector3 rightAnchorStart = tasks.rightAnchor.position;
            Vector3 boxStart = rb.position;
            Vector3 leftHandLocalStart = tasks.leftHand.transform.localPosition;
            Vector3 rightHandLocalStart = tasks.rightHand.transform.localPosition;
            Quaternion leftHandRotationStart = tasks.leftHand.transform.localRotation;
            Quaternion rightHandRotationStart = tasks.rightHand.transform.localRotation;

            tasks.Begin(TaskController.TaskType.Handoff, HelperPath(), rb.gameObject,
                        leftIsSender: leftToRight);
            tasks.OnIntent(sender, 0.016f);
            Assert.AreEqual(TaskController.Stage.Lift, tasks.CurrentStage,
                            "sender-only grip must initiate handoff lift");

            tasks.OnIntent(sender, 5f);
            Assert.AreEqual(TaskController.Stage.Transfer, tasks.CurrentStage,
                            "handoff must pass through Transfer at the midpoint");
            for (int frame = 0; frame < 20; frame++) yield return null;
            AssertPhysicalHold(tasks, leftToRight);

            Transform receiverAnchor = leftToRight ? tasks.rightAnchor : tasks.leftAnchor;
            Vector3 receiverGrip = leftToRight ? tasks.rightGripOffset : tasks.leftGripOffset;
            receiverAnchor.position = rb.transform.position + receiverGrip;
            tasks.OnIntent(receiver, 0.1f);
            tasks.OnIntent(receiver, 0.1f);
            tasks.OnIntent(receiver, 0.2f);
            Assert.AreEqual(TaskController.Stage.Follow, tasks.CurrentStage,
                            "stable valid receiver grip must take ownership");
            Assert.IsTrue(rb.isKinematic, "object must never be dropped during handoff");
            for (int frame = 0; frame < 20; frame++) yield return null;
            AssertPhysicalHold(tasks, !leftToRight);

            tasks.OnIntent(receiver, 5f);
            Assert.AreEqual(TaskController.Stage.Place, tasks.CurrentStage);
            tasks.OnIntent("rest", 1f);
            Assert.AreEqual(TaskController.Stage.Done, tasks.CurrentStage);
            Assert.IsFalse(rb.isKinematic, "final release must restore Rigidbody physics");
            tasks.End();
            Assert.That(Vector3.Distance(tasks.leftAnchor.position, leftAnchorStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(tasks.rightAnchor.position, rightAnchorStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(rb.position, boxStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(tasks.leftHand.transform.localPosition, leftHandLocalStart), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(tasks.rightHand.transform.localPosition, rightHandLocalStart), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(tasks.leftHand.transform.localRotation, leftHandRotationStart), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(tasks.rightHand.transform.localRotation, rightHandRotationStart), Is.LessThan(0.01f));
            yield return null;
        }
    }
}

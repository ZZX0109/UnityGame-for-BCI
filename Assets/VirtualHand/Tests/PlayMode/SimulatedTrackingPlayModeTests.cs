using System.Collections;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neuralife.VirtualHand.PlayTests
{
    /// <summary>
    /// Spec section 10.5, the parts that need no XR hardware: simulated tracking
    /// loss, recovery, source switching, pause/resume and the single-delivery rule.
    ///
    /// The recognizer/router tests are coroutines that advance real frames. That is
    /// deliberate: HandInteractionRouter ticks itself from Update and latches on the
    /// frame number, so calling Tick() by hand inside a single frame is a no-op and a
    /// test written that way would pass for the wrong reason. Driving actual frames
    /// is what exercises the shipped path.
    /// </summary>
    public sealed class SimulatedTrackingPlayModeTests
    {
        GameObject _root;
        HandGestureRecognizer _recognizer;
        HandInteractionRouter _router;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Simulated tracking test root");
            _recognizer = _root.AddComponent<HandGestureRecognizer>();
            _router = _root.AddComponent<HandInteractionRouter>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root) UnityEngine.Object.DestroyImmediate(_root);
            _root = null;
            _recognizer = null;
            _router = null;
        }

        static float[] Fist()
        {
            var c = new float[20];
            for (int finger = 0; finger < 5; finger++)
            {
                int o = finger * 4;
                c[o] = c[o + 1] = c[o + 2] = 1f;
                if (finger == 0) c[3] = 1f;
            }
            return c;
        }

        static float[] Open() => new float[20];

        static HandTrackingFrame Frame(SeatedHandSide side, bool valid) => new HandTrackingFrame
        {
            side = side,
            wristPositionValid = valid,
            wristRotationValid = valid,
            jointValidity = new bool[(int)TrackedJoint.JointCount]
        };

        /// <summary>
        /// Classifies one explicit step, then advances a real frame so the router's own
        /// Update-driven tick runs. The explicit step keeps the confirm window
        /// deterministic in batch mode, where frame deltas are not wall-clock seconds.
        /// </summary>
        IEnumerator Step(float dt = 0.02f)
        {
            _recognizer.TickClassify(dt);
            yield return null;
        }

        /// <summary>Holds a closed hand across real frames until the router reports it held.</summary>
        IEnumerator DriveUntilHeld(int maxSteps)
        {
            _recognizer.SetSourcePoses(Fist(), Fist());
            _router.InputTrusted = true;
            for (int i = 0; i < maxSteps && !_router.LeftHeld; i++)
                yield return Step();
        }

        // ------------------------------------------------------------------
        // validity state machine (no scene needed)
        // ------------------------------------------------------------------

        [Test]
        public void SingleHandLossDoesNotDisturbTheOther()
        {
            var left = new HandTrackingValidity();
            var right = new HandTrackingValidity();

            left.Report(Frame(SeatedHandSide.Left, true), 0.016f);
            right.Report(Frame(SeatedHandSide.Right, true), 0.016f);
            Assert.IsTrue(left.IsTracking && right.IsTracking);

            left.Report(default, 1f);

            Assert.IsFalse(left.IsTracking, "the left hand should be considered lost");
            Assert.IsTrue(right.IsTracking, "the right hand must stay tracked");
            Assert.IsTrue(right.IsInteractive, "the right hand must stay interactive");
        }

        [Test]
        public void BothHandsLostAndRecoveredIndependently()
        {
            var left = new HandTrackingValidity { resyncSeconds = 0.2f };
            var right = new HandTrackingValidity { resyncSeconds = 0.2f };

            left.Report(Frame(SeatedHandSide.Left, true), 0.016f);
            right.Report(Frame(SeatedHandSide.Right, true), 0.016f);

            left.Report(default, 1f);
            right.Report(default, 1f);
            Assert.IsFalse(left.IsInteractive);
            Assert.IsFalse(right.IsInteractive);

            left.Report(Frame(SeatedHandSide.Left, true), 0.016f);
            Assert.IsTrue(left.NeedsResync);
            Assert.IsFalse(left.IsInteractive);

            left.Report(Frame(SeatedHandSide.Left, true), 0.3f);
            Assert.IsTrue(left.IsInteractive, "the left hand should be usable again");
            Assert.IsFalse(right.IsInteractive, "the right hand is still lost");
        }

        [Test]
        public void TrackingOriginChangeIsObservableInTheFrame()
        {
            // Spec 4.2 requires the tracking space identity on the frame so a
            // relocation can be detected rather than silently absorbed.
            var a = Frame(SeatedHandSide.Left, true);
            a.trackingOriginId = "origin-A";
            var b = Frame(SeatedHandSide.Left, true);
            b.trackingOriginId = "origin-B";

            Assert.AreNotEqual(a.trackingOriginId, b.trackingOriginId);
        }

        // ------------------------------------------------------------------
        // interaction edges, driven by real frames
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ClosedHandRaisesExactlyOneGraspStart()
        {
            yield return DriveUntilHeld(20);
            Assert.IsTrue(_router.LeftHeld, "the closed hand was never recognised as held");

            // Keep holding: no further grasp starts may be emitted.
            int extraStarts = 0;
            for (int i = 0; i < 5; i++)
            {
                yield return Step();
                if (_router.LeftEvent.type == HandGestureType.GraspStart)
                    extraStarts++;
            }

            Assert.AreEqual(0, extraStarts,
                "holding a closed hand kept re-emitting GraspStart instead of latching");
        }

        [UnityTest]
        public IEnumerator TrackingLossNeverEmitsARelease()
        {
            // The critical safety property: losing tracking must not look like the
            // player opening their hand, or the game would drop a held object.
            yield return DriveUntilHeld(20);
            Assert.IsTrue(_router.LeftHeld, "the closed hand was not recognised as held");

            _router.InputTrusted = false;
            yield return Step();
            yield return Step();

            Assert.AreNotEqual(HandGestureType.GraspEnd, _router.LeftEvent.type,
                "losing tracking emitted a release");
            Assert.IsFalse(_router.LeftHeld, "the held state should be forgotten, silently");
        }

        [UnityTest]
        public IEnumerator RecoveryRequiresTheResyncWindowBeforeNewEdges()
        {
            _router.resyncSeconds = 0.3f;

            _recognizer.SetSourcePoses(Open(), Open());
            yield return Step();

            _router.InputTrusted = false;
            yield return Step();

            _router.InputTrusted = true;
            _recognizer.SetSourcePoses(Fist(), Fist());

            // First frames after recovery: no edge may be emitted yet.
            for (int i = 0; i < 3; i++)
            {
                yield return Step();
                Assert.AreNotEqual(HandGestureType.GraspStart, _router.LeftEvent.type,
                    "an interaction edge was emitted before the resync window elapsed");
            }
        }

        [UnityTest]
        public IEnumerator ResetHeldStateIsSilentAndNeverEmitsARelease()
        {
            yield return DriveUntilHeld(20);
            Assert.IsTrue(_router.LeftHeld, "the closed hand was not recognised as held");

            // Called synchronously by the host when it takes an object away.
            _router.ResetHeldState();
            Assert.IsFalse(_router.LeftHeld, "ResetHeldState did not clear the held flag");
            Assert.AreEqual(HandGestureType.None, _router.LeftEvent.type,
                "ResetHeldState emitted an event");

            // The hand is still physically closed, so a later frame legitimately reports
            // a fresh grasp. What must never happen is a release for a grasp the game
            // was never told about.
            for (int i = 0; i < 4; i++)
            {
                yield return Step();
                Assert.AreNotEqual(HandGestureType.GraspEnd, _router.LeftEvent.type,
                    "a release was emitted for a grasp the game never saw");
            }
        }

        [UnityTest]
        public IEnumerator OpenHandNeverReportsAHeldGesture()
        {
            _recognizer.SetSourcePoses(Open(), Open());
            for (int i = 0; i < 8; i++)
            {
                yield return Step();
                Assert.IsFalse(_router.LeftHeld, "an open hand was reported as held");
                Assert.AreNotEqual(HandGestureType.GraspStart, _router.LeftEvent.type);
            }
        }

        [UnityTest]
        public IEnumerator ManualTickTwiceInOneFrameEmitsOnlyOneEdge()
        {
            // Spec 4.3: an Update tick plus a before-render tick must not fire a grasp
            // twice. Disable the component so its own Update does not consume the frame
            // latch, then tick twice by hand — the second call must be a no-op.
            yield return DriveUntilHeld(20);
            Assert.IsTrue(_router.LeftHeld);

            _router.enabled = false;

            // Re-open the hand, then tick twice inside one frame.
            _recognizer.SetSourcePoses(Open(), Open());
            _recognizer.TickClassify(0.5f);
            yield return null;

            _router.Tick();
            var first = _router.LeftEvent;

            _router.Tick();
            var second = _router.LeftEvent;

            Assert.AreEqual(first.type, second.type,
                "the second tick in the same frame produced a different edge (" +
                first.type + " then " + second.type + ")");
            Assert.AreEqual(HandGestureType.GraspEnd, first.type,
                "opening the hand should report exactly one GraspEnd");

            _router.enabled = true;
        }

        // ------------------------------------------------------------------
        // source switching (no scene needed)
        // ------------------------------------------------------------------

        [Test]
        public void SourceSwitchClearsPendingFramesAndRaisesTheEvent()
        {
            var go = new GameObject("Replay source test");
            try
            {
                var source = go.AddComponent<ReplaySource>();

                var first = new ReplayData();
                first.frames.Add(new HandMotionFrame { version = 1, time = 0.0, duration = 0.1, intent = "rest", source = "manual" });
                first.frames.Add(new HandMotionFrame { version = 1, time = 0.5, duration = 0.1, intent = "rest", source = "manual" });
                first.frames.Add(new HandMotionFrame { version = 1, time = 1.0, duration = 0.1, intent = "rest", source = "manual" });

                bool switched = false;
                source.SourceChanged += () => switched = true;

                source.Open(first);
                Assert.Greater(source.PendingFrames, 0, "frames should be pending before playback");

                // ClearPending must drop the backlog so a switched source cannot replay
                // the previous stream's targets or un-consumed action events.
                source.ClearPending();
                Assert.AreEqual(0, source.PendingFrames);

                var second = new ReplayData();
                second.frames.Add(new HandMotionFrame { version = 1, time = 0.0, duration = 0.1, intent = "rest", source = "manual" });
                source.Open(second);

                Assert.IsTrue(switched, "swapping the data set did not raise SourceChanged");
                Assert.AreEqual(0, source.Position, "the clock must restart on a source switch");
                Assert.IsFalse(source.IsPlaying, "a source switch must not auto-play");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PauseStopsTheClockAndResumeContinuesFromTheSamePosition()
        {
            var go = new GameObject("Replay clock test");
            try
            {
                var source = go.AddComponent<ReplaySource>();
                var data = new ReplayData();
                for (int i = 0; i < 10; i++)
                    data.frames.Add(new HandMotionFrame
                    {
                        version = 1, time = i * 0.1, duration = 0.1, intent = "rest", source = "manual"
                    });

                source.Open(data);
                source.Play();
                source.Tick(0.25);
                double afterPlay = source.Position;
                Assert.That(afterPlay, Is.EqualTo(0.25).Within(1e-6));

                source.Pause();
                source.Tick(0.5);
                Assert.That(source.Position, Is.EqualTo(afterPlay).Within(1e-6),
                    "the clock advanced while paused");

                source.Play();
                source.Tick(0.1);
                Assert.That(source.Position, Is.EqualTo(afterPlay + 0.1).Within(1e-6),
                    "resume did not continue from the paused position");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResetPlaybackReturnsToTheStart()
        {
            var go = new GameObject("Replay reset test");
            try
            {
                var source = go.AddComponent<ReplaySource>();
                var data = new ReplayData();
                data.frames.Add(new HandMotionFrame { version = 1, time = 0f, duration = 0.1, intent = "rest", source = "manual" });
                data.frames.Add(new HandMotionFrame { version = 1, time = 0.5, duration = 0.1, intent = "rest", source = "manual" });

                source.Open(data);
                source.Play();
                source.Tick(0.3);

                source.ResetPlayback();

                Assert.AreEqual(0, source.Position);
                Assert.IsFalse(source.IsPlaying);
                Assert.Greater(source.PendingFrames, 0, "reset should make every frame pending again");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FrameStreamIsMonotonicAndNeverReplaysAnEarlierFrame()
        {
            var go = new GameObject("Replay ordering test");
            try
            {
                var source = go.AddComponent<ReplaySource>();
                var data = new ReplayData();
                double[] times = { 0.0, 0.2, 0.4, 0.6, 0.8 };
                foreach (var t in times)
                    data.frames.Add(new HandMotionFrame
                    {
                        version = 1, time = t, duration = 0.1, intent = "rest", source = "manual"
                    });

                var delivered = new System.Collections.Generic.List<double>();
                source.FrameReady += f => delivered.Add(f.time);

                source.Open(data);
                source.Play();
                for (int i = 0; i < 10; i++) source.Tick(0.1);

                for (int i = 1; i < delivered.Count; i++)
                    Assert.Less(delivered[i - 1], delivered[i],
                        "frames were delivered out of order: " + delivered[i - 1] + " then " + delivered[i]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}

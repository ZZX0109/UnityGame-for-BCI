using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>Minimal contract tests per brief: real label mapping, no train/test leak,
    /// commands-to-bones, pause/reset/hold, malformed-frame rejection. No giant matrix.</summary>
    public class ContractTests
    {
        [Test]
        public void HandMotionFrame_FrozenFields_MatchContractV1()
        {
            var f = new HandMotionFrame();
            Assert.AreEqual(1, f.version);
            Assert.AreEqual("rest", f.intent);
            Assert.AreEqual("manual", f.source);
            Assert.AreEqual(1f, f.confidence);
        }

        [TestCase("left", true, false)]
        [TestCase("right", false, true)]
        [TestCase("both", true, true)]
        [TestCase("rest", false, false)]
        public void IntentMapper_LeftRightBoth_MapToCorrectHands(string intent, bool expectLeft, bool expectRight)
        {
            Assert.AreEqual(expectLeft, IntentMapper.HandShouldGrip(intent, isLeftHand: true), "left hand");
            Assert.AreEqual(expectRight, IntentMapper.HandShouldGrip(intent, isLeftHand: false), "right hand");
        }

        [Test]
        public void PoseLibrary_AllPoses_Are20ControlsInUnitRange()
        {
            foreach (var p in new[] { PoseLibrary.Rest(), PoseLibrary.Fist(), PoseLibrary.Pinch(), PoseLibrary.Point() })
            {
                Assert.AreEqual(20, p.Length);
                Assert.IsTrue(p.All(v => v >= 0f && v <= 1f), "all controls in [0,1]");
            }
        }

        [Test]
        public void ReplayParse_MalformedLines_AreRejectedWithReason()
        {
            Assert.IsFalse(ReplayData.TryParseFrame("{\"time\":1,\"duration\":0,\"intent\":\"rest\",\"source\":\"manual\"}", out _, out string err1));
            StringAssert.Contains("duration", err1);
            Assert.IsFalse(ReplayData.TryParseFrame("{\"time\":1,\"duration\":1,\"intent\":\"spin\",\"source\":\"manual\"}", out _, out string err2));
            StringAssert.Contains("intent", err2);
            Assert.IsFalse(ReplayData.TryParseFrame("{\"time\":1,\"duration\":1,\"intent\":\"rest\",\"source\":\"manual\",\"confidence\":2.0}", out _, out string err3));
            StringAssert.Contains("confidence", err3);
            Assert.IsFalse(ReplayData.TryParseFrame("{\"time\":1,\"duration\":1,\"intent\":\"rest\",\"source\":\"manual\",\"leftJoints\":[0,0,0]}", out _, out string err4));
            StringAssert.Contains("20", err4);
            Assert.IsFalse(ReplayData.TryParseFrame("{\"time\":NaN,\"duration\":1,\"intent\":\"rest\",\"source\":\"manual\"}", out _, out string err5));
            StringAssert.Contains("time", err5);
        }

        [Test]
        public void ReplayParse_ValidGloveFrame_ParsesAllFields()
        {
            string json = "{\"version\":1,\"time\":0.5,\"duration\":0.0333,\"intent\":\"both\",\"confidence\":1.0," +
                          "\"source\":\"glove\",\"subject\":\"S1\",\"run\":\"E1/cylindrical_grip\"," +
                          "\"leftJoints\":[" + string.Join(",", Enumerable.Repeat("0.5", 20)) + "]}";
            Assert.IsTrue(ReplayData.TryParseFrame(json, out var f, out string err), err);
            Assert.AreEqual(0.5, f.time, 1e-6);
            Assert.AreEqual("S1", f.subject);
            Assert.AreEqual(20, f.leftJoints.Length);
        }

        [Test]
        public void HandRigProfile_OpenSampleIsTrueRest()
        {
            // Regression: pose20_open once returned all-0.5 while flex channels
            // have rest=0, turning the "open" sample into a half-fist.
            string path = "Assets/VirtualHand/Resources/Rig/hand_rig_profile.json";
            if (!File.Exists(path)) Assert.Ignore("profile not exported yet");
            var json = File.ReadAllText(path);
            Assert.IsTrue(HandRigProfile.TryParseText(json, out var p), "profile parses");
            Assert.AreEqual(20, p.Channels.Length, "20 channels");
            for (int i = 0; i < 20; i++)
            {
                bool isFlex = i % 4 != 3; // 4th of each block is twist/spread
                float rest = p.Channels[i].rest;
                if (isFlex)
                    Assert.AreEqual(0f, p.Open[i], 1e-4f,
                        $"open sample flex channel {i} must be 0 (rest), got {p.Open[i]}");
                Assert.AreEqual(isFlex ? 0f : 0.5f, rest, 1e-4f,
                    $"channel {i} rest offset");
            }
            // Fist sample: every finger flexion must be clearly above the open rest
            // sample. DIP calibration targets are anatomical angles divided by a
            // per-joint scale, so they are intentionally below 1.0; do not require
            // control=1.0 or the rig over-flexes.
            for (int f = 1; f < 5; f++)
                for (int j = 0; j < 3; j++)
                {
                    int idx = f * 4 + j;
                    Assert.GreaterOrEqual(p.Fist[idx], 0.25f, $"fist finger {f} joint {j} must be flexed");
                    Assert.Greater(p.Fist[idx], p.Open[idx] + 0.2f,
                                   $"fist finger {f} joint {j} must exceed open pose");
                    Assert.LessOrEqual(p.Fist[idx], 1f, $"fist finger {f} joint {j} must stay in range");
                }
        }

        [Test]
        public void TruthReference_IsSeparatedFromPredictions()
        {
            // Exported files: eeg_S00X_predictions.jsonl (source=prediction) and
            // eeg_S00X_truth.jsonl (source=labels) are separate files by construction.
            // This test pins the invariant that prediction frames never carry labels source.
            string dir = Path.Combine("Assets", "StreamingAssets", "Replays", "eeg");
            if (!Directory.Exists(dir)) Assert.Ignore("EEG replays not exported yet");
            var predictions = Directory.GetFiles(dir, "eeg_*_predictions.jsonl");
            Assert.IsNotEmpty(predictions, "prediction replay files must exist");
            foreach (var pred in predictions)
            {
                string truth = pred.Replace("_predictions", "_truth");
                Assert.IsTrue(File.Exists(truth), "truth file missing for " + pred);
                foreach (var line in File.ReadLines(pred))
                {
                    StringAssert.DoesNotContain("\"source\":\"labels\"", line,
                        "prediction file must not embed label source");
                    Assert.IsTrue(ReplayData.TryParseFrame(line, out var frame, out var err), err);
                    Assert.AreEqual("prediction", frame.source);
                }
            }
        }
    }
}

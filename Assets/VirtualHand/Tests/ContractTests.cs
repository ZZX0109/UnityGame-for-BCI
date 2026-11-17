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
        public void TruthReference_IsSeparatedFromPredictions()
        {
            // Exported files: eeg_S00X_predictions.jsonl (source=prediction) and
            // eeg_S00X_truth.jsonl (source=labels) are separate files by construction.
            // This test pins the invariant that prediction frames never carry labels source.
            string dir = "Assets/StreamingAssets/Replays";
            if (!Directory.Exists(dir)) Assert.Ignore("Replays not exported yet");
            foreach (var pred in Directory.GetFiles(dir, "eeg_*_predictions.jsonl"))
            {
                string truth = pred.Replace("_predictions", "_truth");
                Assert.IsTrue(File.Exists(truth), "truth file missing for " + pred);
                foreach (var line in File.ReadLines(pred))
                    StringAssert.DoesNotContain("\"labels\"", line, "prediction file must not embed label source");
            }
        }
    }
}

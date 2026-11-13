using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawCanvasAudioTests
    {
        [TestCase(1280, 720)]
        [TestCase(1366, 768)]
        [TestCase(1920, 1080)]
        public void FullScreenSafeArea_UsesEntireNormalizedCanvas(int width, int height)
        {
            Assert.AreEqual(new Rect(0, 0, 1, 1), ClawGameUI.NormalizeSafeArea(new Rect(0, 0, width, height), new Vector2(width, height)));
        }

        [Test]
        public void SafeArea_RespectsInsetsAndRejectsInvalidDisplayData()
        {
            Rect area = ClawGameUI.NormalizeSafeArea(new Rect(100, 40, 800, 520), new Vector2(1000, 600));
            Assert.AreEqual(0.1f, area.xMin, 0.000001f);
            Assert.AreEqual(0.9f, area.xMax, 0.000001f);
            Assert.AreEqual(40f / 600, area.yMin, 0.000001f);
            Assert.AreEqual(560f / 600, area.yMax, 0.000001f);
            Assert.AreEqual(new Rect(0, 0, 1, 1), ClawGameUI.NormalizeSafeArea(new Rect(0, 0, 100, 100), Vector2.zero));
            Assert.AreEqual(new Rect(0, 0, 1, 1), ClawGameUI.NormalizeSafeArea(new Rect(float.NaN, 0, 100, 100), Vector2.one * 100));
        }

        [Test]
        public void StageCues_AreDistinctDeterministicAndWithinAudioHeadroom()
        {
            var signatures = new HashSet<ulong>();
            foreach (ClawAudioCue cue in Enum.GetValues(typeof(ClawAudioCue)))
            {
                float[] samples = ClawAudioController.BuildCueSamples(cue, 22050);
                CollectionAssert.AreEqual(samples, ClawAudioController.BuildCueSamples(cue, 22050));
                ulong fingerprint = 14695981039346656037UL;
                float peak = 0;
                foreach (float sample in samples)
                {
                    Assert.IsFalse(float.IsNaN(sample) || float.IsInfinity(sample));
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                    unchecked { fingerprint = (fingerprint ^ (ulong)(Mathf.RoundToInt(sample * 1000000) + 1000000)) * 1099511628211UL; }
                }
                Assert.Greater(peak, 0.05f, cue + " must be audible before the volume control.");
                Assert.LessOrEqual(peak, 0.8f, cue + " must preserve headroom.");
                Assert.IsTrue(signatures.Add(fingerprint), cue + " must not reuse another phase's waveform.");
                Assert.AreEqual(0, samples[0], 0.000001f);
                Assert.AreEqual(0, samples[samples.Length - 1], 0.000001f);
            }
            Assert.AreEqual(10, signatures.Count);
        }

        [Test]
        public void AudioSynthesis_RejectsInvalidSampleRates()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ClawAudioController.BuildCueSamples(ClawAudioCue.Move, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ClawAudioController.BuildCueSamples(ClawAudioCue.Move, 192000));
        }
    }
}

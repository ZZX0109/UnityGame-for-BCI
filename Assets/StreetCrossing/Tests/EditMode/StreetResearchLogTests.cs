using System.IO;
using NUnit.Framework;
using StreetCrossing.Save;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// Contracts of the research metadata channel: camera orientation is measured evidence, eye
    /// gaze exists only with registered hardware, and BCI numbers can never appear unmeasured.
    /// </summary>
    public sealed class StreetResearchLogTests
    {
        private string directory;
        private StreetResearchLog log;

        [SetUp]
        public void CreateLog()
        {
            directory = Path.Combine(Application.temporaryCachePath, "research-tests-" + Path.GetRandomFileName());
            log = new StreetResearchLog("S01", directory);
            Assume.That(log.Healthy, Is.True, "Temporary research log could not be opened on this machine.");
        }

        [TearDown]
        public void DisposeLog()
        {
            log?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private string ReadAll()
        {
            using (var stream = new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        [Test]
        public void CameraOrientationRowsAreMarkedMeasured()
        {
            log.RecordCameraOrientation(3.5f, 42f, -7f);
            string content = ReadAll();
            Assert.That(content, Does.Contain("\"signal\":\"camera-orientation\""));
            Assert.That(content, Does.Contain("\"measured\":true"));
            Assert.That(content, Does.Contain("\"yawDeg\":42"));
            Assert.That(content, Does.Contain("NOT eye gaze"), "The provenance note must warn against head-as-gaze conflation.");
        }

        [Test]
        public void DesktopWithoutHardwareRecordsExplicitGazeAndBciAbsence()
        {
            log.RecordEyeGazeAvailability(0f);
            log.MarkBciUnavailable(0f, "no hardware");
            string content = ReadAll();
            Assert.That(content, Does.Contain("\"signal\":\"eye-gaze\""));
            Assert.That(content, Does.Contain("\"measured\":false"));
            Assert.That(content, Does.Contain("\"signal\":\"bci\""));
            Assert.That(content, Does.Contain("\"source\":\"none\""));
        }

        [Test]
        public void UnregisteredGazeSamplesAreRefused()
        {
            Assert.That(log.EyeGazeRegistered, Is.False);
            Assert.That(log.RecordEyeGaze(1f, 10f, 5f), Is.False, "Without a registered eye tracker no gaze sample may be written.");
            Assert.That(ReadAll(), Is.Empty);
        }

        [Test]
        public void RegisteredGazeSourceRecordsMeasuredSamples()
        {
            log.RegisterEyeGazeSource("PicoNeo3Eye-01");
            Assert.That(log.EyeGazeRegistered, Is.True);
            Assert.That(log.RecordEyeGaze(1f, 10f, 5f), Is.True);
            string content = ReadAll();
            Assert.That(content, Does.Contain("\"source\":\"PicoNeo3Eye-01\""));
            Assert.That(content, Does.Contain("\"measured\":true"));
        }

        [Test]
        public void BciValuesExistOnlyFromRegisteredProviders()
        {
            Assert.That(log.RecordBci(1f), Is.EqualTo(0), "No provider, no BCI rows.");
            Assert.That(log.HasBciMetrics, Is.False);
            log.RegisterBciMetric("workload-index", () => 0.75, "z");
            Assert.That(log.HasBciMetrics, Is.True);
            Assert.That(log.RecordBci(2f), Is.EqualTo(1));
            string content = ReadAll();
            Assert.That(content, Does.Contain("\"signal\":\"bci\""));
            Assert.That(content, Does.Contain("\"measured\":true"));
            Assert.That(content, Does.Contain("\"value\":0.75"));
        }

        [Test]
        public void NonFiniteBciStreamsNeverFabricateValues()
        {
            log.RegisterBciMetric("broken", () => double.NaN, "z");
            Assert.That(log.RecordBci(1f), Is.EqualTo(0), "A dead stream must not write a numeric row.");
        }

        [Test]
        public void UnmeasuredRowsZeroTheirNumericFields()
        {
            log.MarkBciUnavailable(1f, "absent hardware");
            string content = ReadAll();
            Assert.That(content, Does.Contain("\"measured\":false"));
            Assert.That(content, Does.Contain("\"value\":0"));
            Assert.That(content, Does.Contain("\"yawDeg\":0"));
        }

        [Test]
        public void SessionIdIsStableAcrossRows()
        {
            log.RecordCameraOrientation(0f, 1f, 1f);
            log.RecordCameraOrientation(1f, 2f, 2f);
            string content = ReadAll();
            string[] lines = content.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines.Length, Is.EqualTo(2));
            string id = JsonUtility.FromJson<Probe>(lines[0]).sessionId;
            Assert.That(JsonUtility.FromJson<Probe>(lines[1]).sessionId, Is.EqualTo(id));
            Assert.That(id.Length, Is.GreaterThanOrEqualTo(8));
        }

        [System.Serializable]
        private sealed class Probe
        {
            public string sessionId;
            public string signal;
        }
    }
}

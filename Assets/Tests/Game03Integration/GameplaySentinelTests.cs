using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace StreetCrossing.Integration.Tests
{
    /// <summary>
    /// Sentinel for the untouched host seam this integration relies on: if either host
    /// script (or its meta GUID) disappears, every wiring assumption in the binding breaks.
    /// </summary>
    [TestFixture]
    public class GameplaySentinelTests
    {
        static string PlayerPath(params string[] parts)
        {
            return Path.Combine(Application.dataPath, Path.Combine(parts));
        }

        [Test]
        public void PedestrianPlayerController_Exists()
        {
            string script = PlayerPath("StreetCrossing", "Scripts", "Player", "PedestrianPlayerController.cs");
            string meta = script + ".meta";
            Assert.IsTrue(File.Exists(script), $"Host seam missing: {script}");
            Assert.IsTrue(File.Exists(meta), $"Host meta missing (GUID import must be preserved): {meta}");
        }

        [Test]
        public void ObservationTracker_Exists()
        {
            string script = PlayerPath("StreetCrossing", "Scripts", "Player", "ObservationTracker.cs");
            string meta = script + ".meta";
            Assert.IsTrue(File.Exists(script), $"Host seam missing: {script}");
            Assert.IsTrue(File.Exists(meta), $"Host meta missing (GUID import must be preserved): {meta}");
        }
    }
}

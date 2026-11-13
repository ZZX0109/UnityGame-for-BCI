using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetVisualProfileTests
    {
        [TestCase("S01", StreetVisualIdentity.Residential, 3101, 2)]
        [TestCase("S02", StreetVisualIdentity.Commercial, 3202, 10)]
        [TestCase("S03", StreetVisualIdentity.Corner, 3303, 8)]
        public void ExplicitVisualIdentityKeepsExistingRoadAndTrafficBaseline(string id, StreetVisualIdentity identity, int seed, int cars)
        {
            var scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>("Assets/StreetCrossing/Scenarios/" + id + ".asset");
            Assert.That(scenario, Is.Not.Null); Assert.That(scenario.visualProfile, Is.Not.Null);
            Assert.DoesNotThrow(() => scenario.visualProfile.Validate(id)); Assert.That(scenario.visualProfile.identity, Is.EqualTo(identity));
            Assert.That(scenario.seed, Is.EqualTo(seed)); Assert.That(scenario.maximumVehicles, Is.EqualTo(cars));
            Assert.That(scenario.roadHalfWidth, Is.EqualTo(3.3f)); Assert.That(scenario.laneOffset, Is.EqualTo(1.65f));
            Assert.That(scenario.crosswalkHalfWidth, Is.EqualTo(1.8f)); Assert.That(scenario.stopLineOffset, Is.EqualTo(4.6f));
        }

        [Test]
        public void RoundedSignalHousingHasOutwardFacingTrianglesAndMetreScaleBounds()
        {
            Mesh mesh = StreetDetailMesh.RoundedBox(new Vector3(.8f, .95f, .2f), .12f);
            try
            {
                Assert.That(Vector3.Distance(mesh.bounds.size, new Vector3(.8f, .95f, .2f)), Is.LessThan(.0001f));
                Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles;
                Assert.That(indices.Length, Is.GreaterThan(36), "Rounded silhouette needs real geometry.");
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    Assert.That(normal.sqrMagnitude, Is.GreaterThan(.0000000001f));
                    Assert.That(Vector3.Dot(normal, (a + b + c) / 3), Is.GreaterThan(0), "Back-facing housing surface would disappear in the Standard shader.");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}

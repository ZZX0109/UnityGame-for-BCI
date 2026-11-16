using NUnit.Framework;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetBackgroundRouteTests
    {
        [TestCase(3101, 6)]
        [TestCase(3202, 10)]
        [TestCase(3303, 8)]
        public void EntireBackgroundRouteRemainsOutsideTrainingCorridorRoadAndSideJunction(int seed, int count)
        {
            StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(seed, count);
            Assert.That(routes.Length, Is.EqualTo(count));
            foreach (StreetBackgroundRoute route in routes)
            for (int sample = 0; sample <= 3000; sample++)
            {
                Vector3 point = route.Position(sample * .1f);
                Assert.That(point.x, Is.GreaterThan(4.3f), "Keep the required approach/destination/observation corridor empty, including body width.");
                Assert.That(Mathf.Abs(point.z), Is.GreaterThan(3.3f + .45f), "An actor must never enter the traffic roadway.");
                Assert.That(Mathf.Abs(point.x + 9), Is.GreaterThan(3.3f + .45f), "Keep the entire S03 side approach empty.");
                Assert.That(point.x, Is.LessThan(36), "Keep actors inside the original visible sidewalk segment.");
            }
        }

        [Test]
        public void IndependentPopulationSeedDoesNotConsumeUnityRandomAndReplaysIdentically()
        {
            Random.State before = Random.state;
            StreetBackgroundRoute[] first = StreetBackgroundPopulation.Routes(3303, 8);
            Assert.That(Random.state, Is.EqualTo(before));
            StreetBackgroundRoute[] second = StreetBackgroundPopulation.Routes(3303, 8);
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(first[i].Position(0), Is.EqualTo(second[i].Position(0)));
                Assert.That(first[i].Position(17.35f), Is.EqualTo(second[i].Position(17.35f)));
                Assert.That(first[i].speed, Is.EqualTo(second[i].speed));
                if (first[i].waiting) Assert.That(first[i].Position(0), Is.EqualTo(first[i].Position(240)));
            }
        }

        [TestCase(.7f)]
        [TestCase(1.15f)]
        public void CornerFootprintsAreActuallyChamferedWithOutwardFaces(float cut)
        {
            Mesh mesh = StreetDetailMesh.ChamferedCorner(new Vector3(6.6f, 9.8f, 4), cut);
            try
            {
                Assert.That(mesh.bounds.size, Is.EqualTo(new Vector3(6.6f, 9.8f, 4)));
                Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles;
                foreach (Vector3 vertex in vertices)
                    Assert.That(vertex.x > -3.3f + cut - .0001f || vertex.z > -2 + cut - .0001f, Is.True, "The missing corner must not still contain box vertices.");
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3), Is.GreaterThan(0));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}

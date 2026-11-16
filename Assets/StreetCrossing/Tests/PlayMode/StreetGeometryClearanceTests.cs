using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class StreetGeometryClearanceTests
    {
        [UnityTest]
        public IEnumerator S03TurnEnvelopeDoesNotPenetrateCurbsFurnitureOrBuildings()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>(); game.SelectScenario(2);
            yield return null; yield return null; Physics.SyncTransforms();
            for (int index = game.scenario.StraightLaneCount; index < game.scenario.StraightLaneCount + 2; index++)
            {
                VehicleLanePath path = game.Traffic.Routes[index];
                for (float distance = path.StopDistance - 3; distance <= path.JunctionExit + 3; distance += .12f)
                {
                    Vector3 centre = path.Position(distance) + Vector3.up * 1.08f;
                    Collider[] overlaps = Physics.OverlapBox(centre, new Vector3(1.08f, .94f, 2.4f), path.RotationAt(distance), ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider hit in overlaps)
                        if (hit.GetComponentInParent<VehicleController>() == null)
                            Assert.Fail("Unchanged turning vehicle body penetrates " + hit.name + " at " + path.Position(distance));
                    // Wheel-level footprint detects the old 10cm curb penetration even though the chassis sits higher.
                    overlaps = Physics.OverlapBox(path.Position(distance) + Vector3.up * .075f,
                        new Vector3(1.08f, .015f, 2.4f), path.RotationAt(distance), ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider hit in overlaps) Assert.That(hit.name.StartsWith("Kerb"), Is.False, "Wheel envelope penetrates " + hit.name);
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RaisedTreeGratesAndNorthCornerRemainOutsideExistingSolidGeometry()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>(); game.SelectScenario(2);
            yield return null; yield return null; Physics.SyncTransforms();
            Transform environment = game.transform.Find("S03 Street Environment");
            int grateCount = 0;
            foreach (Transform group in environment.GetComponentsInChildren<Transform>())
                if (group.name == "Raised tree grating")
                {
                    grateCount++;
                    Collider pit = null;
                    foreach (Collider candidate in environment.GetComponentsInChildren<Collider>())
                        if (candidate.name == "Tree pit" && Vector2.Distance(new Vector2(candidate.bounds.center.x, candidate.bounds.center.z), new Vector2(group.position.x, group.position.z)) < .01f) pit = candidate;
                    Assert.That(pit, Is.Not.Null);
                    foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>())
                        Assert.That(renderer.bounds.min.y, Is.GreaterThan(pit.bounds.max.y + .0001f), "A grate must stand above, not inside, its original 14cm planter.");
                }
            Assert.That(grateCount, Is.EqualTo(6));
            Transform northCorner = null; BoxCollider westNeighbour = null;
            foreach (Transform group in environment.GetComponentsInChildren<Transform>())
            {
                if (group.name == "Corner building Pharmacy") northCorner = group;
                BoxCollider box = group.GetComponent<BoxCollider>();
                if (box != null && Mathf.Abs(group.position.x + 26) < .01f && Mathf.Abs(group.position.z - StreetLayout.SidewalkZ(11.3f)) < .01f) westNeighbour = box;
            }
            Assert.That(northCorner, Is.Not.Null); Assert.That(westNeighbour, Is.Not.Null);
            Bounds interior = westNeighbour.bounds; interior.Expand(-.01f);
            foreach (MeshFilter filter in northCorner.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    Assert.That(interior.Contains(filter.transform.TransformPoint(vertex)), Is.False, "The corner side frontage penetrates its west neighbour.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

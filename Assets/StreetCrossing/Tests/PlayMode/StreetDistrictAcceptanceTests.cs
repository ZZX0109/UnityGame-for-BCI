using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class StreetDistrictAcceptanceTests
    {
        private StreetGameManager game;
        private sealed class QuietInput : IStreetInput
        {
            public Vector2 Movement => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }
        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            game = Object.FindObjectOfType<StreetGameManager>(); game.AutoPauseOnFocusLoss = false; game.SetInputSource(new QuietInput());
        }
        [UnityTearDown]
        public IEnumerator Cleanup() { if (game != null) { game.Pause(); game.AutoPauseOnFocusLoss = true; } yield return null; }

        [UnityTest, Category("DistrictAcceptance")]
        public IEnumerator ThreeDistrictsHaveFullStreetDepthSafePopulationAndUnchangedCoreGeometry()
        {
            int[] counts = { 6, 10, 8 };
            for (int i = 0; i < 3; i++)
            {
                game.SelectScenario(i); yield return null;
                Transform environment = game.transform.Find(game.scenario.scenarioId + " Street Environment");
                Transform details = environment.Find("Visual details - " + game.scenario.visualProfile.identity);
                Assert.That(details.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(details.Find("Continuous 160 metre streetscape"), Is.Not.Null);
                Assert.That(details.Find("Continuous sidewalk furnishings"), Is.Not.Null);
                StreetBackgroundPopulation population = details.GetComponentInChildren<StreetBackgroundPopulation>();
                Assert.That(population.Count, Is.EqualTo(counts[i]));
                Assert.That(population.GetComponentsInChildren<LODGroup>().Length, Is.EqualTo(counts[i]));
                Physics.SyncTransforms();
                foreach (StreetBackgroundRoute route in StreetBackgroundPopulation.Routes(game.scenario.seed, counts[i]))
                for (int seconds = 0; seconds <= 300; seconds += 2)
                {
                    Vector3 samplePosition = route.Position(seconds);
                    Assert.That(Physics.CheckCapsule(samplePosition + Vector3.up * .27f, samplePosition + Vector3.up * 1.48f, .23f, ~0, QueryTriggerInteraction.Ignore),
                        Is.False, "A background actor's complete safe route intersects world geometry at " + samplePosition);
                }
                Bounds corridor = new Bounds(new Vector3(0, 1, -.25f), new Vector3(2.8f, 2, 16));
                foreach (Collider collider in environment.Find("Additional world geometry").GetComponentsInChildren<Collider>())
                    Assert.That(collider.bounds.Intersects(corridor), Is.False, collider.name + " blocks the original practice route.");
                Assert.That(game.scenario.roadHalfWidth, Is.EqualTo(3.3f)); Assert.That(game.scenario.crosswalkHalfWidth, Is.EqualTo(1.8f));
                Assert.That(game.scenario.playerSpawn, Is.EqualTo(new Vector3(0, .02f, -8)));
                Assert.That(game.scenario.destination, Is.EqualTo(new Vector3(0, 0, 7.5f)));
                // Each theme contains complete low-cost facade variants and two dedicated corner blocks.
                int cornerCount = 0; bool[] facadeKinds = new bool[6];
                foreach (Transform part in environment.GetComponentsInChildren<Transform>())
                {
                    if (part.name.StartsWith("Corner building ") && part.GetComponent<LODGroup>() != null) cornerCount++;
                    for (int kind = 0; kind < 6; kind++) if (part.name == "Facade " + (StreetFacadeKind)kind) facadeKinds[kind] = true;
                }
                Assert.That(cornerCount, Is.EqualTo(2));
                foreach (bool present in facadeKinds) Assert.That(present, Is.True);
                game.StartPractice(); yield return new WaitForSecondsRealtime(.25f); game.Pause(); yield return null;
                Transform walker = population.transform.GetChild(1); Vector3 position = walker.position; Quaternion heading = walker.rotation;
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(walker.position, Is.EqualTo(position)); Assert.That(walker.rotation, Is.EqualTo(heading));
                LogAssert.NoUnexpectedReceived();
            }
        }

        [UnityTest]
        public IEnumerator VehicleModelsReuseCompleteVariantsAndPresentationUsesSimulationDistanceAndClock()
        {
            Transform holder = new GameObject("Vehicle presentation test").transform;
            StreetWorld world = new StreetWorld(holder, game.scenario.surfaceMaterial, game.uiFont);
            GameObject car = Object.Instantiate(game.scenario.vehiclePrefab, holder);
            for (int repeat = 0; repeat < 4; repeat++)
                for (int variant = 0; variant < 3; variant++) { world.DetailVehicle(car, variant); world.ColorVehicle(car, variant); yield return null; }
            StreetVehicleVisualCache cache = car.GetComponent<StreetVehicleVisualCache>();
            Assert.That(cache.CreatedModels, Is.EqualTo(3));
            Assert.That(car.GetComponentsInChildren<StreetVehiclePresentation>(true).Length, Is.EqualTo(3));
            Assert.That(car.GetComponentsInChildren<StreetVehiclePresentation>().Length, Is.EqualTo(1));
            StreetVehiclePresentation presentation = car.GetComponentInChildren<StreetVehiclePresentation>();
            presentation.ResetVisuals(); presentation.TickVisuals(.34f * Mathf.PI, 3, true, VehicleRouteKind.LeftTurn, 20, 40, 0);
            Assert.That(presentation.WheelDegrees, Is.EqualTo(180).Within(.001f));
            Assert.That(presentation.BrakeLightsOn, Is.True); Assert.That(presentation.LeftIndicatorOn, Is.True); Assert.That(presentation.RightIndicatorOn, Is.False);
            presentation.TickVisuals(0, 0, false, VehicleRouteKind.LeftTurn, 45, 40, .6f);
            Assert.That(presentation.WheelDegrees, Is.EqualTo(180).Within(.001f));
            Assert.That(presentation.BrakeLightsOn, Is.True); Assert.That(presentation.LeftIndicatorOn || presentation.RightIndicatorOn, Is.False);
            world.Dispose(); Object.Destroy(holder.gameObject); yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}

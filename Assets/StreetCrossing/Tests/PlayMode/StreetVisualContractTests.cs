using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class StreetVisualContractTests
    {
        [UnityTest]
        public IEnumerator ThreeDistrictsKeepDecorationOutOfPhysicsAndShowPhaseDrivenIcons()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>(); Assert.That(game, Is.Not.Null);
            game.AutoPauseOnFocusLoss = false;
            string[] landmarks = { "Garden walk pergola", "Commercial street canopy row", "Corner square wayfinding" };
            for (int index = 0; index < 3; index++)
            {
                game.SelectScenario(index); yield return null;
                Transform environment = game.transform.Find(game.scenario.scenarioId + " Street Environment");
                Transform details = environment.Find("Visual details - " + game.scenario.visualProfile.identity);
                Assert.That(details, Is.Not.Null); Assert.That(details.Find(landmarks[index]), Is.Not.Null);
                Assert.That(details.GetComponentsInChildren<Collider>(true), Is.Empty, "Decoration must not silently change route or observation occlusion.");
                Assert.That(details.GetComponentsInChildren<MeshRenderer>(true).Length, Is.GreaterThan(100));
                Transform signals = environment.Find("Intersection Signals");
                Assert.That(signals.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(8), "Keep only the original four poles and four housings as blockers.");
                foreach (Transform child in signals)
                {
                    if (!child.name.StartsWith("Pedestrian signal")) continue;
                    Assert.That(child.Find("Standing pedestrian icon").gameObject.activeSelf, Is.True);
                    Assert.That(child.Find("Walking pedestrian icon").gameObject.activeSelf, Is.False);
                }
                float toWalk = 0;
                for (int phase = 0; phase < 3; phase++) toWalk += game.scenario.phaseTable.phases[phase].duration;
                game.Intersection.Tick(toWalk + .01f, false, false);
                Assert.That(game.Intersection.Phase, Is.EqualTo(TrafficPhase.Walk));
                foreach (Transform child in signals)
                {
                    if (!child.name.StartsWith("Pedestrian signal")) continue;
                    Assert.That(child.Find("Walking pedestrian icon").gameObject.activeSelf, Is.True);
                    Assert.That(child.Find("Standing pedestrian icon").gameObject.activeSelf, Is.False);
                }
                if (index == 2)
                {
                    Transform blocker = environment.Find("Opaque bus information shelter - changes visibility");
                    Assert.That(blocker, Is.Not.Null); Assert.That(blocker.localPosition, Is.EqualTo(new Vector3(-2.7f, 1.1f, -4.1f)));
                    Assert.That(blocker.localScale, Is.EqualTo(new Vector3(.9f, 2.2f, 1.4f)));
                    Assert.That(blocker.GetComponent<Collider>().enabled, Is.True);
                }
            }
            game.AutoPauseOnFocusLoss = true; LogAssert.NoUnexpectedReceived();
        }
    }
}

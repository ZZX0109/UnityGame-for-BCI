using NUnit.Framework;
using StreetCrossing.Presentation;
using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetBackgroundAvoidanceTests
    {
        private static readonly StreetBackgroundAvoidance.Personal Variation =
            new StreetBackgroundAvoidance.Personal(2f, .5f, .2f, 1f);

        [Test]
        public void SameInputsReproduceIdenticalDisplacement()
        {
            Vector3 analytic = new Vector3(8f, 0f, 7.55f);
            Vector3 player = new Vector3(9f, 0f, 6.5f);
            Vector3 first = StreetBackgroundAvoidance.Displace(analytic, Vector3.right, player, Variation, 1f);
            Vector3 second = StreetBackgroundAvoidance.Displace(analytic, Vector3.right, player, Variation, 1f);
            Assert.That(first, Is.EqualTo(second), "Avoidance must be a stateless pure function to stay reproducible.");
        }

        [Test]
        public void PlayerOutsideInfluenceRadiusLeavesTheAnalyticPose()
        {
            Vector3 analytic = new Vector3(8f, 0f, 7.55f);
            Vector3 player = new Vector3(8f, 0f, 7.55f - 4f); // 4 m away, radius 2 m
            Vector3 displaced = StreetBackgroundAvoidance.Displace(analytic, Vector3.right, player, Variation, 1f);
            Assert.That(displaced, Is.EqualTo(analytic));
        }

        [Test]
        public void ZeroScaleDisablesAvoidanceEvenAtContactRange()
        {
            Vector3 analytic = new Vector3(8f, 0f, 7.55f);
            Vector3 displaced = StreetBackgroundAvoidance.Displace(analytic, Vector3.right, analytic, Variation, 0f);
            Assert.That(displaced, Is.EqualTo(analytic));
        }

        [Test]
        public void DisplacementFallsBackToIdentityAtTheBoundary()
        {
            Vector3 analytic = new Vector3(8f, 0f, 7.55f);
            Vector3 player = new Vector3(8f, 0f, 7.55f - 2f); // exactly the influence radius
            Vector3 displaced = StreetBackgroundAvoidance.Displace(analytic, Vector3.right, player, Variation, 1f);
            Assert.That(displaced, Is.EqualTo(analytic).Within(1e-5f), "The falloff must reach zero at the radius or yields would snap.");
        }

        [Test]
        public void NearbyPlayerPushesTheWalkerAwayAndBackward()
        {
            Vector3 analytic = new Vector3(8f, 0f, 7.55f);
            Vector3 player = new Vector3(8.6f, 0f, 7.55f); // dead ahead on the route
            Vector3 displaced = StreetBackgroundAvoidance.Displace(analytic, Vector3.right, player, Variation, 1f);
            Assert.That(displaced.z, Is.GreaterThan(analytic.z), "The walker sidesteps away from the player across the route line.");
            Assert.That(displaced.x, Is.LessThan(analytic.x), "The walker also brakes against the walking direction.");
        }

        [Test]
        public void DisplacementNeverBreaksRouteCorridorBounds()
        {
            StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(3101, 8);
            foreach (StreetBackgroundRoute route in routes)
                for (float t = 0f; t <= 240f; t += 0.7f)
                {
                    Vector3 analytic = route.Position(t);
                    Vector3 tangent = route.Position(t + .04f) - analytic;
                    foreach (Vector3 player in new[] { analytic + Vector3.forward * .5f, analytic - Vector3.forward * .5f,
                        analytic + Vector3.right * .5f, analytic })
                    {
                        Vector3 displaced = StreetBackgroundAvoidance.Displace(analytic, tangent, player, Variation, 1.5f);
                        Assert.That(displaced.x, Is.InRange(StreetBackgroundAvoidance.MinCorridorX, StreetBackgroundAvoidance.MaxCorridorX));
                        Assert.That(Mathf.Abs(displaced.x + 9f), Is.GreaterThanOrEqualTo(StreetBackgroundAvoidance.SideRoadClearance - 1e-4f),
                            "The side-junction approach corridor must stay clear.");
                        Assert.That(Mathf.Abs(displaced.z), Is.GreaterThanOrEqualTo(StreetBackgroundAvoidance.MinRoadClearance - 1e-4f),
                            "Displacement may never put a background pedestrian in the roadway.");
                    }
                }
        }

        [Test]
        public void SeedVariationIsDeterministicAndDistinct()
        {
            StreetBackgroundAvoidance.Personal a = StreetBackgroundAvoidance.Personal.ForSeed(3101, 0);
            StreetBackgroundAvoidance.Personal b = StreetBackgroundAvoidance.Personal.ForSeed(3101, 1);
            StreetBackgroundAvoidance.Personal replay = StreetBackgroundAvoidance.Personal.ForSeed(3101, 0);
            Assert.That(a.influenceRadius, Is.EqualTo(replay.influenceRadius), "Same seed+index must replay the same personality.");
            Assert.That(a.influenceRadius, Is.Not.EqualTo(b.influenceRadius).Within(.01f), "Neighbouring actors should vary so a crowd does not move in lockstep.");
            Assert.That(a.influenceRadius, Is.InRange(1.6f, 2.4f));
            Assert.That(a.lateralYield, Is.InRange(.35f, .6f));
            Assert.That(a.brakeDistance, Is.InRange(.15f, .35f));
        }

        [Test]
        public void WaitingActorsYieldOnThePlayerBearingWithoutATangent()
        {
            Vector3 spot = new Vector3(6.6f, 0f, StreetLayout.SidewalkZ(4.65f));
            Vector3 player = spot + new Vector3(0f, 0f, .4f);
            Vector3 displaced = StreetBackgroundAvoidance.Displace(spot, Vector3.zero, player, Variation, 1f);
            Assert.That(displaced, Is.Not.EqualTo(spot), "Waiting actors must still yield when the player stands close.");
            Assert.That(Mathf.Abs(displaced.z), Is.GreaterThanOrEqualTo(StreetBackgroundAvoidance.MinRoadClearance - 1e-4f));
        }
    }
}

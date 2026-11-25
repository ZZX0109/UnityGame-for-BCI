using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// Behavioural contract of the stateful background-pedestrian traversal: continuous motion,
    /// gentle direction changes, braking into the far end, a real pause there, an in-place pivot
    /// instead of an instant 180° flip, and deterministic same-seed routes.
    /// </summary>
    public sealed class StreetPedestrianMotionTests
    {
        private static readonly (int seed, int count)[] Scenarios =
        {
            (3101, 6), (3202, 10), (3303, 8)
        };

        private static System.Collections.Generic.IEnumerable<StreetBackgroundRoute> Walkers()
        {
            foreach ((int seed, int count) in Scenarios)
            {
                StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(seed, count);
                foreach (StreetBackgroundRoute route in routes)
                    if (!route.waiting)
                        yield return route;
            }
        }

        [Test]
        public void TraversalNeverJumpsBetweenSamples()
        {
            foreach (StreetBackgroundRoute route in Walkers())
            {
                Vector3 previous = route.Position(0);
                for (float t = 0.15f; t <= 60f; t += 0.15f)
                {
                    Vector3 current = route.Position(t);
                    Assert.That(Vector3.Distance(previous, current),
                        Is.LessThanOrEqualTo(route.speed * 0.15f + 0.001f),
                        $"Displacement of {t - 0.15f:F2}s..{t:F2}s must stay within cruise speed reach.");
                    previous = current;
                }
            }
        }

        [Test]
        public void MotionDirectionChangesGradually()
        {
            float cosTwelveDegrees = 0.9781f;
            const float step = 0.05f;
            foreach (StreetBackgroundRoute route in Walkers())
            {
                Vector2? previousDirection = null;
                for (float t = 0f; t <= 60f; t += step)
                {
                    Vector3 displacement = route.Position(t + step) - route.Position(t);
                    if (displacement.sqrMagnitude < 0.001f * 0.001f)
                    {
                        previousDirection = null; // rest or pivot gap: never compare across it
                        continue;
                    }
                    Vector2 direction = new Vector2(displacement.x, displacement.z).normalized;
                    if (previousDirection.HasValue)
                        Assert.That(Vector2.Dot(previousDirection.Value, direction),
                            Is.GreaterThanOrEqualTo(cosTwelveDegrees),
                            "Walking direction must not flip between adjacent moving samples.");
                    previousDirection = direction;
                }
            }
        }

        [Test]
        public void PedestrianBrakesBeforeReachingTheEndpoint()
        {
            foreach (StreetBackgroundRoute route in Walkers())
            {
                float arrival = FirstArrival(route);
                Assert.That(arrival, Is.GreaterThan(0f), "Pedestrian should reach its far end within 60s.");

                // Sample the approach 0.15s..0.2s ahead of the arrival instant; with a comfortable
                // acceleration cap the speed there must already be well under 30% of cruise speed.
                Vector3 before = route.Position(arrival - 0.2f);
                Vector3 after = route.Position(arrival - 0.15f);
                float approachSpeed = Vector3.Distance(before, after) / 0.05f;
                Assert.That(approachSpeed, Is.LessThanOrEqualTo(route.speed * 0.3f),
                    "Pedestrian must arrive at the far end slowly, not at cruise speed.");
            }
        }

        [Test]
        public void PedestrianRestsAtTheEndpointBeforeReturning()
        {
            foreach (StreetBackgroundRoute route in Walkers())
            {
                float arrival = FirstArrival(route);
                Vector3 atArrival = route.Position(arrival);
                float stationary = 0f;
                for (float t = arrival; t <= arrival + 10f; t += 0.01f)
                {
                    if (Vector3.Distance(route.Position(t), atArrival) > 0.001f)
                        break;
                    stationary = t - arrival;
                }
                Assert.That(stationary, Is.GreaterThanOrEqualTo(1.4f),
                    "Pedestrian must pause at the far end (>=1.4s) instead of reversing instantly.");
            }
        }

        [Test]
        public void TurnPivotIsSlowAndHalfCircle()
        {
            const float step = 0.01f;
            // 120s contract horizon plus a generous closure margin: a pivot window still open at
            // the horizon is sampled just long enough to close (windows last TurnTime = 2s and no
            // window can open after the horizon), so AssertRun never judges a horizon-truncated
            // half circle as if the route had failed to sweep 180 degrees.
            const float closureCap = 182f;
            foreach (StreetBackgroundRoute route in Walkers())
            {
                float runStart = -1f;
                float previousYaw = 0f;
                float sweep = 0f;
                float maxRate = 0f;
                int runs = 0;
                // Exact integer-indexed grid (i * step) instead of a t += step accumulation, so the
                // sample phase cannot drift against the analytic pivot window as t grows.
                for (int i = 0; i * step <= closureCap; i++)
                {
                    float t = i * step;
                    if (t > 120f && runStart < 0f) break;
                    if (route.TryTurnYaw(t, out float yaw))
                    {
                        if (runStart < 0f)
                        {
                            runStart = t;
                            previousYaw = yaw;
                            sweep = 0f;
                            maxRate = 0f;
                        }
                        else
                        {
                            float delta = Mathf.Repeat(yaw - previousYaw + 180f, 360f) - 180f;
                            sweep += delta;
                            maxRate = Mathf.Max(maxRate, Mathf.Abs(delta) / step);
                            previousYaw = yaw;
                        }
                    }
                    else if (runStart >= 0f)
                    {
                        AssertRun(route, runStart, t, sweep, maxRate);
                        runs++;
                        runStart = -1f;
                    }
                }
                if (runStart >= 0f)
                {
                    AssertRun(route, runStart, closureCap, sweep, maxRate);
                    runs++;
                }
                Assert.That(runs, Is.GreaterThanOrEqualTo(1),
                    "Each walking route must perform in-place pivots within 120s.");
            }
        }

        private static void AssertRun(StreetBackgroundRoute route, float start, float end, float sweep, float maxRate)
        {
            Assert.That(end - start, Is.GreaterThanOrEqualTo(0.5f),
                "Pivot must last at least 0.5s so the reversal is visibly smooth.");
            Assert.That(maxRate, Is.LessThanOrEqualTo(91f),
                "Pivot angular velocity must stay around the 90°/s comfort cap.");
            Assert.That(Mathf.Abs(Mathf.Abs(sweep) - 180f), Is.LessThanOrEqualTo(3f),
                $"Route pivot should sweep a half circle, saw {sweep:F1}°.");
        }

        [Test]
        public void RoundTripVisitsBothEndpointsWithin120Seconds()
        {
            foreach (StreetBackgroundRoute route in Walkers())
            {
                float nearestStart = float.MaxValue;
                float nearestEnd = float.MaxValue;
                for (float t = 0f; t <= 120f; t += 0.05f)
                {
                    Vector3 point = route.Position(t);
                    nearestStart = Mathf.Min(nearestStart, Vector3.Distance(point, route.start));
                    nearestEnd = Mathf.Min(nearestEnd, Vector3.Distance(point, route.end));
                }
                Assert.That(nearestStart, Is.LessThanOrEqualTo(0.01f), "Round trip must return to the start.");
                Assert.That(nearestEnd, Is.LessThanOrEqualTo(0.01f), "Round trip must reach the far end.");
            }
        }

        [Test]
        public void WaitingPedestriansNeverMove()
        {
            foreach ((int seed, int count) in Scenarios)
            {
                StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(seed, count);
                foreach (StreetBackgroundRoute route in routes)
                {
                    if (!route.waiting) continue;
                    for (float t = 0f; t <= 60f; t += 1f)
                        Assert.That(Vector3.Distance(route.Position(t), route.start), Is.LessThanOrEqualTo(1e-5f),
                            "Waiting neighbours must stay on their standing position.");
                }
            }
        }

        [Test]
        public void SameSeedProducesIdenticalRoutesAndMotion()
        {
            StreetBackgroundRoute[] first = StreetBackgroundPopulation.Routes(4242, 8);
            StreetBackgroundRoute[] second = StreetBackgroundPopulation.Routes(4242, 8);
            Assert.That(first.Length, Is.EqualTo(second.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(Vector3.Distance(first[i].start, second[i].start), Is.LessThanOrEqualTo(1e-5f));
                Assert.That(Vector3.Distance(first[i].end, second[i].end), Is.LessThanOrEqualTo(1e-5f));
                Assert.That(first[i].speed, Is.EqualTo(second[i].speed).Within(1e-6));
                Assert.That(first[i].waiting, Is.EqualTo(second[i].waiting));
                float[] probes = { 0f, 17.35f, 90.7f };
                foreach (float t in probes)
                    Assert.That(Vector3.Distance(first[i].Position(t), second[i].Position(t)),
                        Is.LessThanOrEqualTo(1e-5f),
                        $"Same seed must reproduce identical motion at t={t}.");
            }
        }

        // Tight epsilon: detection must land close enough to the stop point that the remaining
        // braking tail (~0.25·τ²) stays under the rest test's 0.001 movement threshold.
        private static float FirstArrival(StreetBackgroundRoute route)
        {
            for (float t = 0f; t <= 60f; t += 0.01f)
            {
                if (Vector3.Distance(route.Position(t), route.end) <= 1e-4f)
                    return t;
            }
            return -1f;
        }

        // ------------------------------------------------------------------
        // §4.4 corridor-clearance regression: every resolved background route
        // keeps a body-width gap from registered street furniture (the shared
        // StreetSpatialAnchors registry is the single source of truth — no
        // coordinates are duplicated here). The kind lists deliberately mirror
        // the private WalkerObstacleKinds / WaitingObstacleKinds in
        // StreetBackgroundPopulation; if those change, these tests must too.
        // ------------------------------------------------------------------
        private static readonly AnchorKind[] WalkerObstacleKinds =
        {
            AnchorKind.BusStop, AnchorKind.Bench, AnchorKind.TreePit, AnchorKind.BuildingEntrance
        };

        private static readonly AnchorKind[] WaitingObstacleKinds =
        {
            AnchorKind.Bench, AnchorKind.TreePit, AnchorKind.BuildingEntrance
        };

        private const float MinGap = 0.40f;

        [Test]
        public void CorridorLinesKeepRegisteredClearance()
        {
            foreach ((int seed, int count) in Scenarios)
            {
                foreach (StreetBackgroundRoute route in StreetBackgroundPopulation.Routes(seed, count))
                {
                    if (route.waiting) continue;
                    Assert.That(route.start.z, Is.EqualTo(route.end.z).Within(1e-5f),
                        "Walking lines are straight shuttles: start and end share one z lane.");
                    // Rebuild the exact walking-line envelope the resolver probed
                    // (start.x = minX, end.x = maxX, both assigned verbatim in Routes).
                    var probe = new SpatialAnchor("test-walkline", AnchorKind.NpcWalkingBand,
                        route.start.z > 0f ? 1 : -1,
                        new Vector3((route.start.x + route.end.x) * .5f, 0f, route.start.z),
                        new Vector2(route.end.x - route.start.x, 0f));
                    foreach (AnchorKind kind in WalkerObstacleKinds)
                    {
                        foreach (SpatialAnchor obstacle in StreetSpatialAnchors.OfKind(kind))
                        {
                            if (obstacle.Side != probe.Side) continue;
                            Assert.That(StreetSpatialAnchors.ClearanceCheck(probe, obstacle, MinGap - 1e-4f),
                                Is.True, $"Walking line at z={route.start.z:0.000} must keep a " +
                                         $"{MinGap:0.00} m gap from {obstacle.Id}.");
                        }
                    }
                    Assert.That(StreetSpatialAnchors.IsOnSidewalk(route.start), Is.True,
                        "Walking lines must stay on the public sidewalk band.");
                    Assert.That(StreetSpatialAnchors.IsOnSidewalk(route.end), Is.True,
                        "Walking-line endpoints must stay on the public sidewalk band.");
                }
            }
        }

        [Test]
        public void Band0NorthLineNudgesPastBusShelter()
        {
            // i=6: band 0, north side, authored z 4.15 -> SidewalkZ 7.45. The street
            // shelter envelope (busStop.street, z 7.75..9.45) leaves only 0.30 there,
            // so the resolver nudges two 0.05 steps toward the kerb and accepts 7.35
            // (gap exactly 0.40). Seed must not matter: geometry is seed-independent.
            foreach ((int seed, int count) in Scenarios)
            {
                if (count <= 6) continue;
                StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(seed, count);
                Assert.That(routes[6].start.z, Is.EqualTo(7.35f).Within(1e-4f),
                    "Band-0 north line must resolve to the 7.35 kerb-side lane behind the shelter.");
                Assert.That(routes[6].start.x, Is.EqualTo(4.8f).Within(1e-4f),
                    "Band-0 geometry (x span 4.8..9.5) is part of the pinned fix.");
            }
        }

        [Test]
        public void WaitingSpotsStayOnSidewalkAndClearOfFurniture()
        {
            foreach ((int seed, int count) in Scenarios)
            {
                StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(seed, count);
                for (int i = 0; i < routes.Length; i += 4)
                {
                    StreetBackgroundRoute route = routes[i];
                    Assert.That(route.waiting, Is.True, $"Route {i} must be a waiting spot.");
                    Assert.That(StreetSpatialAnchors.IsOnSidewalk(route.start), Is.True,
                        "Waiting spots must stand on the public sidewalk band.");
                    // Zero-footprint point probe; BusStop is intentionally exempt
                    // (standing under the shelter, PlanarGap -0.20 kept by design).
                    var probe = new SpatialAnchor("test-waiting", AnchorKind.NpcWaitingPoint,
                        route.start.z > 0f ? 1 : -1,
                        new Vector3(route.start.x, 0f, route.start.z), Vector2.zero);
                    foreach (AnchorKind kind in WaitingObstacleKinds)
                    {
                        foreach (SpatialAnchor obstacle in StreetSpatialAnchors.OfKind(kind))
                        {
                            if (obstacle.Side != probe.Side) continue;
                            Assert.That(StreetSpatialAnchors.ClearanceCheck(probe, obstacle, MinGap - 1e-4f),
                                Is.True, $"Waiting spot {i} at ({route.start.x:0.00}, {route.start.z:0.00}) " +
                                         $"must keep a {MinGap:0.00} m gap from {obstacle.Id}.");
                        }
                    }
                }
            }
        }

        [Test]
        public void WaitingSpotsKeepAuthoredStandingRow()
        {
            // The authored standing row x = 6.0 + slot·0.66 at z = SidewalkZ(4.65) is
            // accepted at nudge 0 today; if the registry squeezes it, the resolver
            // nudges east in 0.05 steps — this pin makes such a registry change visible.
            float[] expectedX = { 6.0f, 6.66f, 7.32f };
            int[] indexes = { 0, 4, 8 };
            foreach ((int seed, int count) in Scenarios)
            {
                StreetBackgroundRoute[] routes = StreetBackgroundPopulation.Routes(seed, count);
                for (int slot = 0; slot < indexes.Length; slot++)
                {
                    if (indexes[slot] >= count) continue;
                    Assert.That(routes[indexes[slot]].start.x, Is.EqualTo(expectedX[slot]).Within(1e-4f),
                        $"Waiting spot {indexes[slot]} must keep its authored standing position.");
                    Assert.That(routes[indexes[slot]].start.z, Is.EqualTo(7.95f).Within(1e-4f),
                        "Waiting spots share the SidewalkZ(4.65) standing row.");
                }
            }
        }

        // ---- §4.4/§4.5 body-type silhouette contract (pure data, no prefab instantiation) ----

        [Test]
        public void SilhouetteProfilesAreDeterministicPerVariant()
        {
            Assert.That(StreetAssetLibrary.SilhouetteCount, Is.EqualTo(4),
                "The sidewalk crowd draws from four authored body types.");
            foreach (int variant in new[] { 0, 1, 2, 3, 7, 11, -3, -9 })
            {
                StreetAssetLibrary.Silhouette first = StreetAssetLibrary.SilhouetteFor(variant);
                StreetAssetLibrary.Silhouette second = StreetAssetLibrary.SilhouetteFor(variant);
                Assert.That(first.hipsX, Is.EqualTo(second.hipsX), $"hipsX must be stable for variant {variant}.");
                Assert.That(first.spineX, Is.EqualTo(second.spineX), $"spineX must be stable for variant {variant}.");
                Assert.That(first.spineZ, Is.EqualTo(second.spineZ), $"spineZ must be stable for variant {variant}.");
                Assert.That(first.spineY, Is.EqualTo(second.spineY), $"spineY must be stable for variant {variant}.");
                Assert.That(first.shoulderY, Is.EqualTo(second.shoulderY), $"shoulderY must be stable for variant {variant}.");
                Assert.That(first.headScale, Is.EqualTo(second.headScale), $"headScale must be stable for variant {variant}.");
            }
        }

        [Test]
        public void HeavyAndSlimBodyTypesReadApart()
        {
            // Authored classes 1 (heavy) and 2 (slim) sit 1.10 vs 0.90 on spine width; the
            // ±4 % seeded jitter cannot close that gap (worst case 1.056 vs 0.936).
            for (int baseVariant = 0; baseVariant < 12; baseVariant += StreetAssetLibrary.SilhouetteCount)
            {
                StreetAssetLibrary.Silhouette heavy = StreetAssetLibrary.SilhouetteFor(baseVariant + 1);
                StreetAssetLibrary.Silhouette slim = StreetAssetLibrary.SilhouetteFor(baseVariant + 2);
                Assert.That(heavy.hipsX, Is.GreaterThan(slim.hipsX),
                    $"Heavy variant {baseVariant + 1} must stay wider at the hips than slim variant {baseVariant + 2}.");
                Assert.That(heavy.spineX, Is.GreaterThan(slim.spineX),
                    $"Heavy variant {baseVariant + 1} must keep a wider torso than slim variant {baseVariant + 2}.");
                Assert.That(heavy.shoulderY, Is.GreaterThan(slim.shoulderY),
                    $"Heavy variant {baseVariant + 1} must keep broader shoulders than slim variant {baseVariant + 2}.");
            }
        }

        [Test]
        public void CrowdVariantsSpreadAcrossSilhouettes()
        {
            float minWidth = float.MaxValue, maxWidth = float.MinValue;
            for (int variant = 0; variant < 12; variant++)
            {
                float width = StreetAssetLibrary.SilhouetteFor(variant).spineX;
                minWidth = Mathf.Min(minWidth, width);
                maxWidth = Mathf.Max(maxWidth, width);
            }
            Assert.That(maxWidth - minWidth, Is.GreaterThan(0.10f),
                "Twelve consecutive crowd variants must not collapse onto one body width.");
            // Same authored class, different jitter seed: the per-variant draw must vary.
            Assert.That(StreetAssetLibrary.SilhouetteFor(0).spineX,
                Is.Not.EqualTo(StreetAssetLibrary.SilhouetteFor(4).spineX),
                "Variants sharing a body class must still differ through their seeded jitter.");
        }

        [Test]
        public void SilhouetteNeverScalesLengthAxes()
        {
            // The feet-plant maths reads bind-pose bounds after bone scaling; keeping every
            // length-dominant factor near 1.0 (spine/shoulder long axes, no leg bones at all)
            // is what stops height drift between body types.
            for (int variant = -8; variant <= 8; variant++)
            {
                StreetAssetLibrary.Silhouette s = StreetAssetLibrary.SilhouetteFor(variant);
                Assert.That(s.spineY, Is.EqualTo(1f).Within(0.04f),
                    $"Variant {variant} must keep the spine long axis at unit scale.");
                Assert.That(s.hipsX, Is.InRange(0.88f, 1.17f), $"Variant {variant} hips width out of range.");
                Assert.That(s.spineX, Is.InRange(0.86f, 1.15f), $"Variant {variant} torso width out of range.");
                Assert.That(s.spineZ, Is.InRange(0.90f, 1.13f), $"Variant {variant} torso depth out of range.");
                Assert.That(s.shoulderY, Is.InRange(0.90f, 1.10f), $"Variant {variant} shoulder span out of range.");
                Assert.That(s.headScale, Is.InRange(0.91f, 1.07f), $"Variant {variant} head scale out of range.");
            }
        }
    }
}

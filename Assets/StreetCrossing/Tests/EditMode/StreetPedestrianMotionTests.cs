using NUnit.Framework;
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
    }
}

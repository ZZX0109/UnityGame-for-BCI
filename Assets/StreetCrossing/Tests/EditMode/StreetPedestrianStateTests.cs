using NUnit.Framework;
using StreetCrossing.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// §5.1/§5.2 contract of the pure motion-state function: the same analytic route windows that
    /// drive position also classify StartWalk → Walk → SlowDown → Stop → Turn in strict cycle order,
    /// waiting routes stay Waiting at zero ground speed, the cadence source integrates to the travelled
    /// arc length (single source of truth - no foot sliding or teleporting), and the generated
    /// controller never lets a transition re-enter its own source state.
    /// </summary>
    public sealed class StreetPedestrianStateTests
    {
        private const string ControllerPath =
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/StreetPedestrian.controller";

        /// <summary>The shuttle's allowed order around one cycle; the Turn→StartWalk wrap is legal.</summary>
        private static readonly PedestrianMotionState[] CycleOrder =
        {
            PedestrianMotionState.StartWalk, PedestrianMotionState.Walk, PedestrianMotionState.SlowDown,
            PedestrianMotionState.Stop, PedestrianMotionState.Turn
        };

        private static int CycleIndexOf(PedestrianMotionState state)
        {
            for (int i = 0; i < CycleOrder.Length; i++)
                if (CycleOrder[i] == state) return i;
            return -1; // Waiting/Idle never participate in the shuttle cycle
        }

        /// <summary>
        /// 20 m line at 1 m/s: with the population's comfort constants (Accel .5, Rest 1.5 s, Turn 2 s)
        /// this yields a 2 s start-up, 18 s cruise, 2 s braking, 1.5 s rest and 2 s pivot - long,
        /// cleanly separated windows to sample (brakeEnd 22 s, turnStart 23.5 s, half-cycle 25.5 s).
        /// </summary>
        private static StreetBackgroundRoute Shuttle()
        {
            return new StreetBackgroundRoute
            {
                start = Vector3.zero, end = new Vector3(20f, 0f, 0f), waiting = false, speed = 1f, phase = 0f
            };
        }

        private static float FirstOccurrence(StreetBackgroundRoute route, PedestrianMotionState state,
            float from, float until = 60f)
        {
            for (float t = from; t <= until; t += 0.01f)
                if (route.MotionStateAt(t) == state) return t;
            Assert.Fail($"state {state} never occurred between t={from:0.00} and t={until:0.00}");
            return -1f;
        }

        [Test]
        public void ShuttleStatesFollowTheTraversalWindows()
        {
            StreetBackgroundRoute route = Shuttle();
            Assert.That(route.MotionStateAt(0f), Is.EqualTo(PedestrianMotionState.StartWalk),
                "t=0 is inside the ½at² start-up ramp");

            float cruise = FirstOccurrence(route, PedestrianMotionState.Walk, 0f);
            float brake = FirstOccurrence(route, PedestrianMotionState.SlowDown, cruise);
            float stop = FirstOccurrence(route, PedestrianMotionState.Stop, brake);
            float pivot = FirstOccurrence(route, PedestrianMotionState.Turn, stop);
            Assert.That(cruise, Is.GreaterThan(0f), "a walk window must exist after the ramp");
            Assert.That(brake, Is.GreaterThan(cruise), "braking must follow the cruise window");
            Assert.That(stop, Is.GreaterThan(brake), "the full pause must follow braking");
            Assert.That(pivot, Is.GreaterThan(stop), "the pivot must follow the pause");
            Assert.That(route.MotionStateAt(stop + 0.05f), Is.EqualTo(PedestrianMotionState.Stop),
                "just after reaching the endpoint the body is fully stopped");
            Assert.That(Vector3.Distance(route.Position(stop + 0.05f), route.end), Is.LessThan(1e-4f),
                "the Stop window begins once the endpoint is reached");
        }

        [Test]
        public void GroundSpeedRampsInSyncWithTheKinematics()
        {
            StreetBackgroundRoute route = Shuttle();
            Assert.That(route.SpeedAt(0f), Is.EqualTo(0f).Within(1e-5f), "the start-up begins from standstill");

            float cruise = FirstOccurrence(route, PedestrianMotionState.Walk, 0f);
            Assert.That(route.SpeedAt(cruise + 0.05f), Is.EqualTo(route.speed).Within(1e-4f),
                "cruise cadence matches the route speed exactly");

            float stop = FirstOccurrence(route, PedestrianMotionState.Stop, cruise);
            Assert.That(route.SpeedAt(stop + 0.05f), Is.EqualTo(0f).Within(1e-5f), "a stopped body has no cadence");

            float restart = FirstOccurrence(route, PedestrianMotionState.StartWalk, stop + 1f);
            Vector3 parked = route.Position(stop + 0.05f);
            Assert.That(Vector3.Distance(route.Position(restart - 0.05f), parked), Is.LessThan(1e-4f),
                "no ground motion while paused or pivoting");
            Assert.That(route.SpeedAt(restart - 0.05f), Is.EqualTo(0f).Within(1e-5f),
                "the pivot window keeps zero cadence so the standing take holds");
        }

        [Test]
        public void PositionIsTheTimeIntegralOfGroundSpeed()
        {
            // Single source of truth: the analytic position and the analytic cadence must integrate to
            // the same traversal over the whole outbound leg, otherwise feet slide or the body jumps.
            StreetBackgroundRoute route = Shuttle();
            float stop = FirstOccurrence(route, PedestrianMotionState.Stop, 0f);
            const float step = 0.01f;
            double integrated = 0.0;
            for (float t = 0f; t < stop; t += step)
                integrated += route.SpeedAt(t + step * 0.5f) * step;
            Assert.That(Vector3.Distance(route.start, route.Position(stop)),
                Is.EqualTo((float)integrated).Within(0.02f),
                "arc length travelled must equal the time integral of ground speed");
        }

        [Test]
        public void WaitingRoutesStayWaitingAtZeroSpeed()
        {
            var route = new StreetBackgroundRoute
            {
                start = new Vector3(7f, 0f, 4f), end = new Vector3(7f, 0f, 4f), waiting = true, speed = .8f, phase = .5f
            };
            for (float t = 0f; t <= 60f; t += 0.37f)
            {
                Assert.That(route.MotionStateAt(t), Is.EqualTo(PedestrianMotionState.Waiting),
                    $"waiting routes report Waiting for their whole duration (t={t:0.00})");
                Assert.That(route.SpeedAt(t), Is.EqualTo(0f).Within(1e-5f));
                Assert.That(Vector3.Distance(route.Position(t), route.start), Is.EqualTo(0f).Within(1e-6f));
            }
        }

        [TestCase(3101, 6)]
        [TestCase(3202, 10)]
        [TestCase(3303, 8)]
        public void ProductionWaitingSpotsStayWaiting(int seed, int count)
        {
            foreach (StreetBackgroundRoute route in StreetBackgroundPopulation.Routes(seed, count))
            {
                if (!route.waiting) continue;
                for (float t = 0f; t <= 90f; t += 0.9f)
                    Assert.That(route.MotionStateAt(t), Is.EqualTo(PedestrianMotionState.Waiting),
                        $"production waiting spot {route.start} reported {route.MotionStateAt(t)} at t={t:0.00}");
            }
        }

        [TestCase(3101, 6)]
        [TestCase(3202, 10)]
        [TestCase(3303, 8)]
        public void WalkerStatesAdvanceInCycleOrderWithoutSkipping(int seed, int count)
        {
            foreach (StreetBackgroundRoute route in StreetBackgroundPopulation.Routes(seed, count))
            {
                if (route.waiting) continue;
                int previous = CycleIndexOf(route.MotionStateAt(0f));
                Assert.That(previous, Is.EqualTo(0), "a shuttle sampled at t=0 begins its start-up ramp");
                // 110 s covers at least one full cycle of the slowest, longest seeded line,
                // including the legal Turn→StartWalk wrap into the homebound leg.
                for (float t = 0.05f; t <= 110f; t += 0.05f)
                {
                    int current = CycleIndexOf(route.MotionStateAt(t));
                    Assert.That((current - previous + CycleOrder.Length) % CycleOrder.Length,
                        Is.EqualTo(0).Or.EqualTo(1),
                        $"state cycle jumped from {CycleOrder[previous]} to {CycleOrder[current]} at t={t:0.00}");
                    previous = current;
                }
            }
        }

        [Test]
        public void ControllerTransitionsNeverRetargetThemselves()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null, $"the pedestrian controller must stay at {ControllerPath}");

            int anyStateTransitions = 0;
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                Assert.That(layer.stateMachine, Is.Not.Null, "every layer needs a state machine");
                foreach (AnimatorStateTransition transition in layer.stateMachine.anyStateTransitions)
                {
                    anyStateTransitions++;
                    Assert.That(transition.canTransitionToSelf, Is.False,
                        $"AnyState → {transition.destinationState?.name ?? "??"} must not re-enter itself");
                }
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                    foreach (AnimatorStateTransition transition in child.state.transitions)
                        Assert.That(transition.canTransitionToSelf, Is.False,
                            $"{child.state.name} → {transition.destinationState?.name ?? "??"} must not re-enter itself");
            }
            Assert.That(anyStateTransitions, Is.GreaterThanOrEqualTo(1),
                "the walker controller drives its clips through AnyState transitions");
        }
    }
}

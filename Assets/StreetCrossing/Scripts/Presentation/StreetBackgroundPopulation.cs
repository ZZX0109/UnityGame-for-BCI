using System.Collections.Generic;
using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    public struct StreetBackgroundRoute
    {
        public Vector3 start, end;
        public bool waiting;
        public float speed, phase;

        // Comfort constants for the stateful traversal (see Position): gentle, human-scale numbers so
        // start-ups, arrivals and pivots read as a person rather than a PingPong float reversing instantly.
        private const float Accel = .5f;     // m/s² - comfortable walking acceleration order; caps start-up and braking.
        private const float RestTime = 1.5f; // s    - full pause at each end, body facing held exactly as on arrival.
        private const float TurnTime = 2f;   // s    - 180° pivot at ≤90°/s so no single frame ever flips the body.

        /// <summary>
        /// Pure function of time: cruise, gentle start-up, braking into the far end, pause, in-place pivot,
        /// then the same in reverse. Closed analytic piecewise integration per half-cycle with no internal
        /// state, so sampling any number of times per frame (including the seconds+0.04 tangent look-ahead)
        /// stays reentrant and deterministic.
        /// </summary>
        public Vector3 Position(float seconds)
        {
            if (waiting) return start;
            Segment(seconds, out int leg, out float u, out float accelTime, out float cruiseTime, out float length);
            Vector3 from = leg == 0 ? start : end;
            Vector3 dir = (leg == 0 ? end - start : start - end) / length;
            float rampDistance = .5f * Accel * accelTime * accelTime; // ground used by the start-up ramp
            float s; // arc length travelled away from `from` within this half-cycle
            if (u < accelTime)
                s = .5f * Accel * u * u;                                    // start-up: s = ½at²
            else if (u < accelTime + cruiseTime)
                s = rampDistance + speed * (u - accelTime);                 // cruise at route speed
            else if (u < accelTime + cruiseTime + accelTime)
            {
                float brake = u - accelTime - cruiseTime;                   // remaining ground = ½a(t_brake-τ)²
                s = length - .5f * Accel * (accelTime - brake) * (accelTime - brake);
            }
            else
                s = length;                                                 // paused at the far end, then pivoting in place
            return from + dir * s;
        }

        /// <summary>
        /// True while the body performs the in-place pivot between the two travel directions; <paramref name="yaw"/>
        /// sweeps the arrival-to-departure arc at ≤90°/s so Apply can rotate smoothly instead of snapping 180°.
        /// A straight route cannot express a direction reversal as continuous on-line motion, so the pivot is
        /// an explicit stationary heading rather than a position tangent.
        /// </summary>
        public bool TryTurnYaw(float seconds, out float yaw)
        {
            yaw = 0f;
            if (waiting) return false;
            Segment(seconds, out int leg, out float u, out float accelTime, out float cruiseTime, out float length);
            float turn = 2f * accelTime + cruiseTime + RestTime; // pivot window inside the half-cycle
            if (u < turn) return false;
            float arrive = leg == 0 ? YawOf(end - start) : YawOf(start - end);
            float depart = leg == 0 ? YawOf(start - end) : YawOf(end - start);
            yaw = Mathf.LerpAngle(arrive, depart, (u - turn) / TurnTime);
            return true;
        }

        /// <summary>Decomposes a time into the half-cycle index (0 outbound, 1 homebound) and local time within it.</summary>
        private void Segment(float seconds, out int leg, out float u, out float accelTime, out float cruiseTime, out float length)
        {
            length = Vector3.Distance(start, end);
            accelTime = speed / Accel;                                    // gentle start-up/braking duration
            cruiseTime = Mathf.Max(0f, length / Mathf.Max(speed, .01f) - accelTime);
            float half = 2f * accelTime + cruiseTime + RestTime + TurnTime;
            float t = Mathf.Repeat(seconds, 2f * half);
            leg = t < half ? 0 : 1;
            u = t - leg * half;
        }

        private static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // Unity yaw of LookRotation(dir)
    }

    /// <summary>Independent seed, fixed safe sidewalk routes; no navmesh, colliders, crossing participation or shared traffic RNG.</summary>
    public sealed class StreetBackgroundPopulation : MonoBehaviour
    {
        private sealed class Actor
        {
            public StreetBackgroundRoute route;
            public Transform root;
            public Animator animator;
            public float restYaw;
        }
        private const float WalkClipSpeed = .8f;
        private readonly List<Actor> actors = new List<Actor>();
        private StreetGameManager game;
        private bool animatorsFrozen;
        public int Count => actors.Count;
        public static StreetBackgroundRoute[] Routes(int seed, int count)
        {
            var random = new System.Random(unchecked(seed ^ 0x3F1B293));
            var routes = new StreetBackgroundRoute[count];
            // Segments stay east of the S03 junction and outside |x|<4.3 crossing/required approach.
            for (int i = 0; i < count; i++)
            {
                bool waiting = i % 4 == 0;
                int side = (i & 1) == 0 ? 1 : -1;
                int band = i % 3;
                float minX = band == 0 ? 4.8f : band == 1 ? 10.4f : 28.2f;
                float maxX = band == 0 ? 9.5f : band == 1 ? 14.7f : 34.5f;
                float z = side * (band == 0 ? (side > 0 ? 4.15f : 5.1f) : band == 1 ? 7.55f : 6.0f);
                // Leave a real body-width gap between opposite walkers and keep the north lane in front of the bus seat.
                if (!(band == 0 && side > 0)) z += (i / 6) * .55f;
                // Four separated standing positions fit inside the bus-shelter waiting area without sharing walking paths.
                Vector3 start = waiting ? new Vector3(6.0f + (i / 4) * .66f, 0, StreetLayout.SidewalkZ(4.65f)) : new Vector3(minX, 0, StreetLayout.SidewalkZ(z));
                // The east-side walking and waiting routes stay outside all dropped kerbs.
                start.y = StreetFootwayMesh.HeightAtDepth(.10f, Mathf.Abs(start.z) - StreetLayout.RoadHalfWidth);
                routes[i] = new StreetBackgroundRoute { start = start, end = waiting ? start : new Vector3(maxX, start.y, start.z), waiting = waiting,
                    speed = .65f + (float)random.NextDouble() * .30f, phase = (float)random.NextDouble() };
            }
            return routes;
        }
        public void Initialize(StreetGameManager owner, StreetWorld world, StreetScenarioDefinition scenario)
        {
            game = owner;
            int count = scenario.visualProfile.backgroundPeople;
            StreetBackgroundRoute[] routes = Routes(scenario.seed, count);
            for (int i = 0; i < routes.Length; i++)
            {
                Transform person = new GameObject(routes[i].waiting ? "Waiting neighbour" : "Sidewalk pedestrian").transform;
                person.SetParent(transform, false);
                (Transform model, Animator animator) = world.CreatePedestrian(person, i);
                if (animator != null)
                {
                    // Controller Motion: 0 idle, 1 walk, 2 texting, 3 phone call.
                    animator.SetInteger("Motion", routes[i].waiting ? (i % 2 == 0 ? 2 : 3) : 1);
                    if (!routes[i].waiting)
                    {
                        // Cadence matched to travel speed (walk clip authored at ~0.8 m/s) so feet stay planted.
                        animator.speed = routes[i].speed / WalkClipSpeed;
                        // The Motion Int parameter offers no normalizedTime control, so start the walk
                        // state at the route's random phase to desynchronise pedestrian strides.
                        animator.Play("Walk", 0, routes[i].phase);
                    }
                }
                world.AddVisualLod(person, .018f);
                actors.Add(new Actor { route = routes[i], root = person, animator = animator,
                    restYaw = 165f + (i % 3) * 15f });
            }
            Apply(0);
        }
        private void LateUpdate()
        {
            if (game == null || !game.IsPlaying)
            {
                // Animators follow Time.timeScale, which may stay non-zero while the game is paused;
                // freeze them explicitly so pedestrians do not keep walking in the background.
                if (!animatorsFrozen)
                {
                    foreach (Actor actor in actors) if (actor.animator != null) actor.animator.speed = 0f;
                    animatorsFrozen = true;
                }
                return;
            }
            animatorsFrozen = false;
            // The simulation advances on a fixed 50 Hz step while this pose pass runs every rendered
            // frame; snapping to WorldTime read as one jump per step (sliding feet + VR judder).
            // Routes are analytic in time, so sample the trajectory at the fractionally blended
            // render time instead - equivalent to blending the previous and current fixed-step poses.
            float seconds = game.WorldTime - (1f - game.StepAlpha) * StreetGameManager.SimulationStep;
            Apply(seconds);
        }
        private void Apply(float seconds)
        {
            foreach (Actor actor in actors)
            {
                // Re-assert cadence every pass so pedestrians unfrozen after a pause resume at their
                // own walk rate (waiters animate at full speed).
                if (actor.animator != null)
                    actor.animator.speed = actor.route.waiting ? 1f : actor.route.speed / WalkClipSpeed;
                Vector3 p = actor.route.Position(seconds);
                Vector3 tangent = actor.route.Position(seconds + .04f) - p;
                actor.root.localPosition = p;
                // The end-of-route pivot is analytic (TryTurnYaw): the straight route cannot express the
                // reversal as motion, so without this branch the first reverse tangent would snap the
                // body 180° in a single frame.
                if (actor.route.TryTurnYaw(seconds, out float yaw))
                    actor.root.localRotation = Quaternion.Euler(0, yaw, 0);
                else if (tangent.sqrMagnitude > .000001f) actor.root.localRotation = Quaternion.LookRotation(tangent);
                else if (actor.route.waiting) actor.root.localRotation = Quaternion.Euler(0, actor.restYaw, 0);
                if (actor.animator != null)
                    actor.animator.SetInteger("Motion", actor.route.waiting
                        ? (actor.root.GetSiblingIndex() % 2 == 0 ? 2 : 3) : 1);
            }
        }
    }
}

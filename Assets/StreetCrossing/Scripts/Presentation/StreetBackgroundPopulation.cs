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
        public Vector3 Position(float seconds)
        {
            if (waiting) return start;
            float length = Vector3.Distance(start, end);
            float t = Mathf.PingPong(phase + seconds * speed / Mathf.Max(.01f, length), 1);
            return Vector3.Lerp(start, end, t);
        }
    }

    /// <summary>Independent seed, fixed safe sidewalk routes; no navmesh, colliders, crossing participation or shared traffic RNG.</summary>
    public sealed class StreetBackgroundPopulation : MonoBehaviour
    {
        private sealed class Actor
        {
            public StreetBackgroundRoute route;
            public Transform root, leftLeg, rightLeg, leftArm, rightArm;
        }
        private readonly List<Actor> actors = new List<Actor>();
        private StreetGameManager game;
        private float previousTime = -1;
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
                float minX = band == 0 ? 5.5f : band == 1 ? 10.4f : 28.2f;
                float maxX = band == 0 ? 7.7f : band == 1 ? 14.7f : 34.5f;
                float z = side * (band == 0 ? (side > 0 ? 4.15f : 5.1f) : band == 1 ? 7.55f : 6.0f);
                // Leave a real body-width gap between opposite walkers and keep the north lane in front of the bus seat.
                if (!(band == 0 && side > 0)) z += (i / 6) * .55f;
                // Four separated standing positions fit inside the bus-shelter waiting area without sharing walking paths.
                Vector3 start = waiting ? new Vector3(6.0f + (i / 4) * .66f, 0, StreetLayout.SidewalkZ(4.65f)) : new Vector3(minX, 0, StreetLayout.SidewalkZ(z));
                routes[i] = new StreetBackgroundRoute { start = start, end = waiting ? start : new Vector3(maxX, 0, start.z), waiting = waiting,
                    speed = .65f + (float)random.NextDouble() * .30f, phase = (float)random.NextDouble() };
            }
            return routes;
        }
        public void Initialize(StreetGameManager owner, StreetWorld world, StreetScenarioDefinition scenario)
        {
            game = owner;
            int count = scenario.visualProfile.backgroundPeople;
            StreetBackgroundRoute[] routes = Routes(scenario.seed, count);
            string[] clothes = { "5F7380", "9B7560", "70846C", "A28D65", "6A637B" };
            for (int i = 0; i < routes.Length; i++)
            {
                Transform person = new GameObject(routes[i].waiting ? "Waiting neighbour" : "Sidewalk pedestrian").transform; person.SetParent(transform, false);
                float scale = .94f + (i % 3) * .045f; person.localScale = Vector3.one * scale;
                Body(world, person, "Jacket", new Vector3(0, 1.10f, 0), new Vector3(.40f, .56f, .25f), clothes[i % clothes.Length], .10f);
                Body(world, person, "Head", new Vector3(0, 1.56f, 0), new Vector3(.24f, .29f, .24f), i % 2 == 0 ? "C69E80" : "AF8A6D", .115f);
                Body(world, person, "Hair", new Vector3(0, 1.70f, .025f), new Vector3(.25f, .10f, .24f), "55483F", .05f);
                Transform leftLeg = Limb(world, person, "Left leg", new Vector3(-.11f, .83f, 0), new Vector3(.15f, .74f, .16f), "4B5352");
                Transform rightLeg = Limb(world, person, "Right leg", new Vector3(.11f, .83f, 0), new Vector3(.15f, .74f, .16f), "4B5352");
                Body(world, leftLeg, "Left shoe", new Vector3(0, -.74f, .035f), new Vector3(.17f, .11f, .29f), "343E3B", .035f);
                Body(world, rightLeg, "Right shoe", new Vector3(0, -.74f, .035f), new Vector3(.17f, .11f, .29f), "343E3B", .035f);
                Transform leftArm = Limb(world, person, "Left arm", new Vector3(-.26f, 1.35f, 0), new Vector3(.13f, .57f, .14f), clothes[i % clothes.Length]);
                Transform rightArm = Limb(world, person, "Right arm", new Vector3(.26f, 1.35f, 0), new Vector3(.13f, .57f, .14f), clothes[i % clothes.Length]);
                world.AddVisualLod(person, .018f);
                actors.Add(new Actor { route = routes[i], root = person, leftLeg = leftLeg, rightLeg = rightLeg, leftArm = leftArm, rightArm = rightArm });
            }
            Apply(0);
        }
        private static void Body(StreetWorld world, Transform parent, string name, Vector3 p, Vector3 s, string colour, float radius)
        { world.VisualRounded(parent, name, p, s, colour, radius); }
        private static Transform Limb(StreetWorld world, Transform parent, string name, Vector3 anchor, Vector3 size, string colour)
        {
            Transform joint = new GameObject(name + " joint").transform; joint.SetParent(parent, false); joint.localPosition = anchor;
            Body(world, joint, name, Vector3.down * (size.y * .5f), size, colour, .06f); return joint;
        }
        private void LateUpdate()
        {
            if (game == null || !game.IsPlaying || Mathf.Approximately(previousTime, game.WorldTime)) return;
            Apply(game.WorldTime);
        }
        private void Apply(float seconds)
        {
            previousTime = seconds;
            foreach (Actor actor in actors)
            {
                Vector3 p = actor.route.Position(seconds);
                Vector3 tangent = actor.route.Position(seconds + .04f) - p;
                actor.root.localPosition = p;
                if (tangent.sqrMagnitude > .000001f) actor.root.localRotation = Quaternion.LookRotation(tangent);
                else if (actor.route.waiting) actor.root.localRotation = Quaternion.Euler(0, 180, 0);
                float swing = actor.route.waiting ? 0 : Mathf.Sin((seconds * actor.route.speed * 7 + actor.route.phase * 6.28f)) * 22;
                actor.leftLeg.localRotation = Quaternion.Euler(swing, 0, 0); actor.rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
                actor.leftArm.localRotation = Quaternion.Euler(-swing * .7f, 0, -5); actor.rightArm.localRotation = Quaternion.Euler(swing * .7f, 0, 5);
            }
        }
    }
}

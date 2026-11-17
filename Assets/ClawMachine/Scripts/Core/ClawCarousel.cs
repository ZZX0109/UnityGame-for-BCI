using System.Collections.Generic;
using UnityEngine;

namespace ClawMachine
{
    // Only carousel challenges opt in. During aiming each prize has a precise
    // kinematic pose. Braking hands the same poses back to normal PhysX grasping.
    public sealed class ClawCarousel : MonoBehaviour
    {
        [SerializeField] private Rigidbody platform;
        private readonly List<PrizePhysics> riders = new List<PrizePhysics>();
        private readonly List<float> heights = new List<float>();
        private readonly List<Quaternion> rotations = new List<Quaternion>();
        private ClawTaskSpace space;
        private ClawChallengeDefinition definition;
        private float phase;
        private bool staged;
        public Rigidbody Platform => platform;
        public bool IsConfigured => platform && platform.GetComponentInChildren<Collider>(true);
        public bool Active => definition && definition.carouselEnabled && platform && platform.gameObject.activeSelf;
        public bool IsStaged => staged;
        public float AngleDegrees => phase;

        public static float StartAngle(int seed, float offset)
        {
            if (!Finite(offset)) offset = 0;
            // Stable between runs and retries, with no shared Unity Random state.
            var random = new System.Random(seed);
            return Mathf.Repeat(offset + (float)random.NextDouble() * 360, 360);
        }
        public static Vector3 OrbitPosition(Vector3 center, float radius, float degrees, float height)
        {
            if (!ClawTaskSpace.Finite(center)) center = Vector3.zero;
            radius = Finite(radius) ? Mathf.Clamp(radius, 0.08f, 0.35f) : 0.21f;
            degrees = Finite(degrees) ? Mathf.Repeat(degrees, 360) : 0;
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(center.x + Mathf.Sin(radians) * radius, height,
                center.z + Mathf.Cos(radians) * radius);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Configure(ClawChallengeDefinition challenge, ClawTaskSpace taskSpace, IReadOnlyList<PrizePhysics> prizes)
        {
            Deactivate();
            if (!challenge || !challenge.carouselEnabled || !IsConfigured || !taskSpace || !taskSpace.IsConfigured) return;
            definition = challenge; space = taskSpace;
            phase = StartAngle(challenge.seed, challenge.carouselStartDegrees);
            platform.gameObject.SetActive(true);
            platform.isKinematic = true; platform.useGravity = false;
            platform.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            platform.interpolation = RigidbodyInterpolation.Interpolate;
            platform.position = space.ToWorld(challenge.carouselCenter);
            platform.rotation = space.Orientation * Quaternion.Euler(0, phase, 0);
            Physics.SyncTransforms();
            if (prizes != null) foreach (PrizePhysics prize in prizes)
            {
                if (!prize || !prize.gameObject.activeSelf || !prize.IsConfigured || !prize.Profile) continue;
                Rigidbody body = prize.Body;
                riders.Add(prize);
                // Each spawn owns its support height and physics profile. Only
                // X/Z and yaw are animated by the carousel mode.
                heights.Add(space.ToLocal(body.position).y);
                rotations.Add(Quaternion.Inverse(space.Orientation) * body.rotation);
                if (!body.isKinematic) body.velocity = body.angularVelocity = Vector3.zero;
                body.isKinematic = true; body.useGravity = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            staged = riders.Count > 0;
            ApplyLayout(true);
            Physics.SyncTransforms();
        }

        public void Tick(float deltaTime, bool aiming, bool paused)
        {
            if (!Active || !staged || !aiming || paused || !Finite(deltaTime) || deltaTime <= 0) return;
            float speed = Finite(definition.carouselDegreesPerSecond) ? Mathf.Clamp(definition.carouselDegreesPerSecond, -45, 45) : 0;
            phase = Mathf.Repeat(phase + speed * deltaTime, 360);
            ApplyLayout(false);
        }
        private void ApplyLayout(bool immediate)
        {
            if (!Active || !space || riders.Count == 0) return;
            Quaternion rotation = space.Orientation * Quaternion.Euler(0, phase, 0);
            if (immediate) platform.rotation = rotation; else platform.MoveRotation(rotation);
            for (int i = 0; i < riders.Count; i++)
            {
                PrizePhysics prize = riders[i];
                if (!prize || !prize.IsConfigured) continue;
                Vector3 local = OrbitPosition(definition.carouselCenter, definition.carouselRadius,
                    phase + i * 360f / riders.Count, heights[i]);
                Vector3 position = space.ToWorld(local);
                Quaternion orientation = rotation * rotations[i];
                if (immediate) { prize.Body.position = position; prize.Body.rotation = orientation; }
                else { prize.Body.MovePosition(position); prize.Body.MoveRotation(orientation); }
            }
        }

        public void BrakeForGrab()
        {
            if (!staged) return;
            // Never snap riders back to their authored spawn after the player
            // commits. Their actual current pose is the physical grasp pose.
            staged = false;
            if (platform) platform.transform.SetPositionAndRotation(platform.position, platform.rotation);
            foreach (PrizePhysics prize in riders)
            {
                if (!prize || !prize.IsConfigured) continue;
                Rigidbody body = prize.Body;
                Vector3 position = body.position; Quaternion rotation = body.rotation;
                body.isKinematic = false; body.useGravity = true;
                body.position = position; body.rotation = rotation;
                body.transform.SetPositionAndRotation(position, rotation);
                body.velocity = body.angularVelocity = Vector3.zero;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.WakeUp();
            }
            Physics.SyncTransforms();
        }
        public void Deactivate()
        {
            BrakeForGrab();
            riders.Clear(); heights.Clear(); rotations.Clear();
            if (platform) platform.gameObject.SetActive(false);
            definition = null; space = null; phase = 0;
        }
        private void OnDisable() => Deactivate();
    }
}

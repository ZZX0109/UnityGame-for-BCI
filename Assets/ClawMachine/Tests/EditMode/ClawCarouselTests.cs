using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ClawMachine.Tests.Shared;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawCarouselTests
    {
        [Test]
        public void CarouselGeometry_HasRepeatableSeedAndThreeSeparatedReachableSlots()
        {
            float phase = ClawCarousel.StartAngle(20261116, 15);
            Assert.AreEqual(phase, ClawCarousel.StartAngle(20261116, 15));
            Assert.GreaterOrEqual(phase, 0); Assert.Less(phase, 360);
            Vector3 center = new Vector3(0.20f, 0.80f, 0.12f);
            for (int degree = 0; degree < 360; degree += 5)
            {
                Vector3 previous = ClawCarousel.OrbitPosition(center, 0.21f, degree - 120, 0.96f);
                Vector3 point = ClawCarousel.OrbitPosition(center, 0.21f, degree, 0.96f);
                Assert.AreEqual(0.96f, point.y);
                Assert.Greater(Vector3.Distance(previous, point), 0.36f);
                Assert.That(point.x, Is.InRange(-0.01001f, 0.41001f));
                Assert.That(point.z, Is.InRange(-0.09001f, 0.33001f));
            }
            Assert.IsTrue(ClawTaskSpace.Finite(ClawCarousel.OrbitPosition(Vector3.one * float.NaN, float.NaN, float.NaN, 1)));
        }

        [Test]
        public void Carousel_ConfiguresOnlyOptInAndFreezesOnPauseOrAfterBrake()
        {
            using (var fixture = new CarouselFixture())
            {
                fixture.Configure();
                Assert.IsTrue(fixture.Carousel.Active); Assert.IsTrue(fixture.Carousel.IsStaged);
                float start = fixture.Carousel.AngleDegrees;
                fixture.Carousel.Tick(0.5f, true, true);
                Assert.AreEqual(start, fixture.Carousel.AngleDegrees);
                fixture.Carousel.Tick(0.5f, false, false);
                Assert.AreEqual(start, fixture.Carousel.AngleDegrees);
                fixture.Carousel.Tick(0.5f, true, false);
                Assert.AreEqual(Mathf.Repeat(start + 7, 360), fixture.Carousel.AngleDegrees, 0.00001f);
                fixture.Carousel.BrakeForGrab();
                float stopped = fixture.Carousel.AngleDegrees;
                fixture.Carousel.Tick(0.5f, true, false);
                Assert.AreEqual(stopped, fixture.Carousel.AngleDegrees);
                fixture.Definition.carouselEnabled = false;
                fixture.Carousel.Configure(fixture.Definition, fixture.Space, fixture.Prizes);
                Assert.IsFalse(fixture.Carousel.Active); Assert.IsFalse(fixture.Platform.gameObject.activeSelf);
            }
        }

        [Test]
        public void Braking_RestoresNativeBodiesAtCurrentPoseWithoutChangingMassOrCenterOfMass()
        {
            using (var fixture = new CarouselFixture())
            {
                fixture.Configure();
                var positions = new Vector3[fixture.Prizes.Length];
                var rotations = new Quaternion[fixture.Prizes.Length];
                for (int i = 0; i < fixture.Prizes.Length; i++)
                {
                    Rigidbody body = fixture.Prizes[i].Body;
                    positions[i] = body.position; rotations[i] = body.rotation;
                    Assert.IsTrue(body.isKinematic); Assert.IsFalse(body.useGravity);
                    Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative, body.collisionDetectionMode);
                }
                fixture.Carousel.BrakeForGrab();
                for (int i = 0; i < fixture.Prizes.Length; i++)
                {
                    PrizePhysics prize = fixture.Prizes[i]; Rigidbody body = prize.Body;
                    Assert.IsFalse(body.isKinematic); Assert.IsTrue(body.useGravity);
                    Assert.AreEqual(positions[i], body.position); Assert.Less(Quaternion.Angle(rotations[i], body.rotation), 0.0001f);
                    Assert.AreEqual(prize.Profile.mass, body.mass); Assert.AreEqual(prize.Profile.centerOfMass, body.centerOfMass);
                    Assert.AreEqual(CollisionDetectionMode.ContinuousDynamic, body.collisionDetectionMode);
                    Assert.AreEqual(Vector3.zero, body.velocity); Assert.AreEqual(Vector3.zero, body.angularVelocity);
                }
                fixture.Carousel.Deactivate();
                Assert.IsFalse(fixture.Platform.gameObject.activeSelf);
            }
        }

        [Test]
        public void CarouselTask_FailedRetryRestoresSameSeedAndPauseDoesNotUseAnotherAttempt()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ClawMachine/Prefabs/Machine/ClawMachine.prefab");
            ClawChallengeDefinition challenge = AssetDatabase.LoadAssetAtPath<ClawChallengeDefinition>("Assets/ClawMachine/Challenges/C04.asset");
            Assert.NotNull(prefab); Assert.NotNull(challenge);
            GameObject root = Object.Instantiate(prefab);
            try
            {
                GraspTaskController task = root.GetComponentInChildren<GraspTaskController>(true);
                Assert.NotNull(task); Assert.NotNull(task.Carousel);
                var driver = root.AddComponent<AuditManipulatorStub>();
                Assert.IsTrue(task.SetDriver(driver)); Assert.IsTrue(task.SetChallenge(challenge, true));
                Assert.IsTrue(task.IsConfigured); Assert.IsTrue(task.Carousel.IsStaged);
                Assert.AreEqual("cloud-bunny", task.Challenge.targetId);
                var positions = new Vector3[task.Prizes.Count];
                for (int i = 0; i < positions.Length; i++) positions[i] = task.Prizes[i].Body.position;
                float angle = task.Carousel.AngleDegrees;
                task.SetPaused(true); Tick(task);
                Assert.AreEqual(angle, task.Carousel.AngleDegrees); Assert.IsFalse(task.StartGrab());
                Assert.AreEqual(0, task.Session.AttemptId);
                task.SetPaused(false); Assert.IsTrue(task.StartGrab());
                Assert.IsFalse(task.Carousel.IsStaged); Assert.AreEqual(1, task.Session.AttemptId);
                Assert.IsTrue(task.Session.Advance(GraspState.ContactDescent)); Assert.IsTrue(task.Session.Advance(GraspState.Closing));
                Assert.IsTrue(task.Session.Advance(GraspState.Returning)); Assert.IsTrue(task.Session.Advance(GraspState.Result));
                task.Retry();
                Assert.AreEqual(GraspState.Aiming, task.Session.State); Assert.AreEqual(1, task.Session.AttemptId);
                Assert.AreEqual(angle, task.Carousel.AngleDegrees); Assert.IsTrue(task.Carousel.IsStaged);
                for (int i = 0; i < positions.Length; i++) Assert.AreEqual(positions[i], task.Prizes[i].Body.position);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Tick(GraspTaskController task)
        {
            typeof(GraspTaskController).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(task, null);
        }
        private static void Set(object owner, string field, object value)
        {
            FieldInfo info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, field); info.SetValue(owner, value);
        }
        private sealed class CarouselFixture : System.IDisposable
        {
            public readonly GameObject Root = new GameObject("Carousel native body fixture");
            public readonly ClawTaskSpace Space;
            public readonly ClawCarousel Carousel;
            public readonly Rigidbody Platform;
            public readonly PrizePhysics[] Prizes = new PrizePhysics[3];
            public readonly ClawChallengeDefinition Definition;
            private readonly PrizeProfile profile;
            public CarouselFixture()
            {
                var volume = Root.AddComponent<BoxCollider>(); volume.enabled = false;
                volume.center = Vector3.up; volume.size = Vector3.one * 2;
                var release = new GameObject("Release"); release.transform.SetParent(Root.transform, false);
                Space = Root.AddComponent<ClawTaskSpace>();
                Set(Space, "machineFrame", Root.transform); Set(Space, "workspaceVolume", volume); Set(Space, "releasePoint", release.transform);
                var platform = new GameObject("Platform"); platform.transform.SetParent(Root.transform, false);
                var support = platform.AddComponent<BoxCollider>(); support.size = new Vector3(0.68f, 0.04f, 0.68f);
                Platform = platform.AddComponent<Rigidbody>(); Platform.isKinematic = true; Platform.useGravity = false;
                Carousel = Root.AddComponent<ClawCarousel>(); Set(Carousel, "platform", Platform);
                profile = ScriptableObject.CreateInstance<PrizeProfile>(); profile.mass = 0.37f; profile.centerOfMass = Vector3.right * 0.02f;
                Definition = ScriptableObject.CreateInstance<ClawChallengeDefinition>(); Definition.challengeId = "C04";
                Definition.carouselEnabled = true; Definition.carouselDegreesPerSecond = 14;
                string[] ids = { "mint-parcel", "cloud-bunny", "amber-fox" };
                for (int i = 0; i < Prizes.Length; i++)
                {
                    var prizeObject = new GameObject(ids[i]); prizeObject.transform.SetParent(Root.transform, false);
                    var collider = prizeObject.AddComponent<BoxCollider>(); collider.size = Vector3.one * 0.2f;
                    Rigidbody body = prizeObject.AddComponent<Rigidbody>();
                    PrizePhysics prize = prizeObject.AddComponent<PrizePhysics>(); Prizes[i] = prize;
                    Set(prize, "body", body); Set(prize, "mainCollider", collider); Set(prize, "machineRoot", Root.transform); Set(prize, "slotId", ids[i]);
                    prize.Configure(new PrizeSpawnDefinition { prizeId = ids[i], displayName = ids[i], spawnPosition = new Vector3(i * 0.1f, 0.92f, 0), graspProfile = profile }, Definition.seed);
                }
            }
            public void Configure() => Carousel.Configure(Definition, Space, Prizes);
            public void Dispose() { Object.DestroyImmediate(Root); Object.DestroyImmediate(profile); Object.DestroyImmediate(Definition); }
        }
    }
}

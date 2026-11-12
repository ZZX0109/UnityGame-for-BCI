using UnityEngine;

namespace ClawMachine
{
    public sealed class PrizePhysics : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider mainCollider;
        [SerializeField] private Transform machineRoot;
        public Rigidbody Body => body;
        public Collider Collider => mainCollider;
        public string PrizeId { get; private set; }
        public PrizeProfile Profile { get; private set; }
        public bool Released { get; private set; }
        public int SessionId { get; private set; }
        public int AttemptId { get; private set; }
        public bool IsConfigured => body && mainCollider && machineRoot && mainCollider.attachedRigidbody == body;
        public void ResetPrize(ClawChallengeDefinition challenge)
        {
            if (!IsConfigured || !challenge || !challenge.graspProfile) return;
            Profile = challenge.graspProfile; PrizeId = challenge.prizeId;
            body.isKinematic = false; body.useGravity = true;
            body.position = machineRoot.TransformPoint(challenge.SpawnForSeed());
            body.rotation = machineRoot.rotation * Quaternion.Euler(challenge.spawnEuler);
            body.velocity = body.angularVelocity = Vector3.zero; body.mass = Profile.mass; body.centerOfMass = Profile.centerOfMass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            Released = false; SessionId = AttemptId = 0; body.WakeUp();
        }
        public void BeginAttempt(int session, int attempt) { SessionId = session; AttemptId = attempt; Released = false; }
        public void MarkReleased() { Released = true; }
        public void ClearVelocity() { if (body) body.velocity = body.angularVelocity = Vector3.zero; }
        public bool IsStable => IsConfigured && Profile && body.velocity.sqrMagnitude <= Profile.stableSpeed * Profile.stableSpeed && body.angularVelocity.sqrMagnitude < 0.25f;
    }
}

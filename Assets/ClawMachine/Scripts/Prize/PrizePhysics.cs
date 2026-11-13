using UnityEngine;

namespace ClawMachine
{
    public sealed class PrizePhysics : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider mainCollider;
        [SerializeField] private Collider[] contactColliders;
        [SerializeField] private Transform machineRoot;
        [SerializeField] private string slotId = "mint-parcel";
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        public Rigidbody Body => body;
        public Collider Collider => mainCollider;
        public Collider[] ContactColliders => contactColliders != null && contactColliders.Length > 0 ? contactColliders : new[] { mainCollider };
        public string SlotId => slotId;
        public string PrizeId { get; private set; }
        public string DisplayName { get; private set; }
        public string GripHint => Profile ? Profile.strategyHint : "";
        public PrizeProfile Profile { get; private set; }
        public bool Released { get; private set; }
        public int SessionId { get; private set; }
        public int AttemptId { get; private set; }
        public bool IsConfigured => body && mainCollider && machineRoot && mainCollider.attachedRigidbody == body;
        public Bounds CaptureBounds => mainCollider.bounds;
        public Bounds AllBounds
        {
            get { var b = mainCollider.bounds; foreach (Collider c in ContactColliders) if (c && c.enabled) b.Encapsulate(c.bounds); return b; }
        }
        public void Configure(PrizeSpawnDefinition spawn, int seed)
        {
            if (!IsConfigured || spawn == null || !spawn.graspProfile) return;
            Profile = spawn.graspProfile; PrizeId = spawn.prizeId; DisplayName = spawn.displayName;
            initialPosition = machineRoot.TransformPoint(ClawChallengeDefinition.PositionForSeed(spawn.spawnPosition, seed, spawn.prizeId));
            initialRotation = machineRoot.rotation * Quaternion.Euler(spawn.spawnEuler);
            Restore();
        }
        public void ResetPrize(ClawChallengeDefinition challenge)
        {
            if (!IsConfigured || !challenge) return;
            if (challenge.spawns != null)
                foreach (PrizeSpawnDefinition spawn in challenge.spawns) if (spawn.prizeId == slotId) { Configure(spawn, challenge.seed); return; }
            Configure(new PrizeSpawnDefinition { prizeId = challenge.prizeId, displayName = challenge.prizeId, spawnPosition = challenge.spawnPosition, spawnEuler = challenge.spawnEuler, graspProfile = challenge.graspProfile }, challenge.seed);
        }
        public void Restore()
        {
            if (!IsConfigured || !Profile) return;
            body.isKinematic = false; body.useGravity = true; body.position = initialPosition; body.rotation = initialRotation;
            body.velocity = body.angularVelocity = Vector3.zero; body.mass = Profile.mass; body.centerOfMass = Profile.centerOfMass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; body.interpolation = RigidbodyInterpolation.Interpolate;
            Released = false; SessionId = AttemptId = 0; body.WakeUp();
        }
        public bool TrySurfaceBelow(Vector3 origin, Vector3 down, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            return mainCollider && mainCollider.Raycast(new Ray(origin, down), out hit, 4);
        }
        public void BeginAttempt(int session, int attempt) { SessionId = session; AttemptId = attempt; Released = false; }
        public void MarkReleased() { Released = true; }
        public void ClearVelocity() { if (body) body.velocity = body.angularVelocity = Vector3.zero; }
        public bool IsStable => IsConfigured && Profile && body.velocity.sqrMagnitude <= Profile.stableSpeed * Profile.stableSpeed && body.angularVelocity.sqrMagnitude < 0.25f;
    }
}

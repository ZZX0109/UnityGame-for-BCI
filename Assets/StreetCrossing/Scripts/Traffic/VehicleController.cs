using UnityEngine;

namespace StreetCrossing.Traffic
{
    public sealed class VehicleController : MonoBehaviour
    {
        public VehicleKinematics Motion { get; } = new VehicleKinematics();
        public VehicleLanePath Lane { get; private set; }
        public int EncounterId { get; private set; }
        public float HalfWidth { get; private set; } = 0.9f;
        public float AccelerationMultiplier { get; private set; } = 1;
        private float halfHeight = 0.65f;
        public BoxCollider Body { get; private set; }
        public void Initialize(VehicleLanePath lane, int id, float distance, float speed, int variant = 0)
        {
            Lane = lane; EncounterId = id;
            float length = variant == 2 ? 4.8f : variant == 1 ? 3.7f : 4.2f;
            HalfWidth = variant == 2 ? 1.08f : variant == 1 ? 0.98f : 1.03f; halfHeight = variant == 2 ? 0.97f : 0.72f;
            AccelerationMultiplier = variant == 2 ? 0.75f : variant == 1 ? 1.12f : 1;
            Motion.Reset(distance, speed, length * 0.5f);
            Body = GetComponent<BoxCollider>(); Body.size = new Vector3(HalfWidth * 2, halfHeight * 2, length); Body.center = Vector3.up * (halfHeight + 0.12f);
            transform.SetPositionAndRotation(lane.Position(distance), lane.RotationAt(distance)); gameObject.SetActive(true);
        }
        public void ApplyPosition() { transform.SetPositionAndRotation(Lane.Position(Motion.Distance), Lane.RotationAt(Motion.Distance)); }
        public bool SweptContact(CharacterController player, Vector3 oldPosition)
        {
            Vector3 offset = transform.position - oldPosition;
            Vector3 centre = oldPosition + Vector3.up * (halfHeight + 0.12f);
            Vector3 extents = new Vector3(HalfWidth, halfHeight, Motion.BodyHalfLength);
            if (offset.sqrMagnitude > 0.000001f)
            {
                RaycastHit[] hits = Physics.BoxCastAll(centre, extents, offset.normalized, transform.rotation, offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++) if (hits[i].collider == player) return true;
            }
            // OverlapBox also catches an initial/resting physical contact, including zero-speed vehicles.
            Collider[] overlaps = Physics.OverlapBox(transform.position + Vector3.up * (halfHeight + 0.12f), extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps.Length; i++) if (overlaps[i] == player) return true;
            return false;
        }
    }
}

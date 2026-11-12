using UnityEngine;

namespace StreetCrossing.Traffic
{
    public sealed class VehicleController : MonoBehaviour
    {
        public VehicleKinematics Motion { get; } = new VehicleKinematics();
        public VehicleLanePath Lane { get; private set; }
        public int EncounterId { get; private set; }
        public BoxCollider Body { get; private set; }
        public void Initialize(VehicleLanePath lane, int id, float distance, float speed)
        {
            Lane = lane; EncounterId = id; Motion.Reset(distance, speed);
            Body = GetComponent<BoxCollider>();
            transform.SetPositionAndRotation(lane.Position(distance), lane.Rotation); gameObject.SetActive(true);
        }
        public void ApplyPosition() { transform.position = Lane.Position(Motion.Distance); }
        public bool SweptContact(CharacterController player, Vector3 oldPosition)
        {
            Vector3 offset = transform.position - oldPosition;
            Vector3 centre = oldPosition + Vector3.up * 0.77f;
            Vector3 extents = new Vector3(0.9f, 0.65f, VehicleKinematics.HalfLength);
            if (offset.sqrMagnitude > 0.000001f)
            {
                RaycastHit[] hits = Physics.BoxCastAll(centre, extents, offset.normalized, transform.rotation, offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++) if (hits[i].collider == player) return true;
            }
            // OverlapBox also catches an initial/resting physical contact, including zero-speed vehicles.
            Collider[] overlaps = Physics.OverlapBox(transform.position + Vector3.up * 0.77f, extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps.Length; i++) if (overlaps[i] == player) return true;
            return false;
        }
    }
}

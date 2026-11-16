using UnityEngine;
using StreetCrossing.Presentation;

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
        public Vector3 PreviousPosition { get; private set; }
        public float ContactSpeed { get; private set; }
        private RaycastHit[] sweepHits = new RaycastHit[8];
        private Collider[] overlapHits = new Collider[8];
        private StreetVehiclePresentation presentation;
        public void Initialize(VehicleLanePath lane, int id, float distance, float speed, int variant = 0)
        {
            Lane = lane; EncounterId = id;
            float length = variant == 2 ? 4.8f : variant == 1 ? 3.7f : 4.2f;
            HalfWidth = variant == 2 ? 1.08f : variant == 1 ? 0.98f : 1.03f; halfHeight = variant == 2 ? 0.97f : 0.72f;
            AccelerationMultiplier = variant == 2 ? 0.75f : variant == 1 ? 1.12f : 1;
            Motion.Reset(distance, speed, length * 0.5f);
            Body = GetComponent<BoxCollider>(); Body.size = new Vector3(HalfWidth * 2, halfHeight * 2, length); Body.center = Vector3.up * (halfHeight + 0.12f);
            transform.SetPositionAndRotation(lane.Position(distance), lane.RotationAt(distance)); gameObject.SetActive(true);
            PreviousPosition = transform.position; ContactSpeed = speed;
            presentation = GetComponentInChildren<StreetVehiclePresentation>();
            if (presentation) presentation.ResetVisuals();
        }
        public void CaptureMotion() { PreviousPosition = transform.position; ContactSpeed = Motion.Speed; }
        public void UpdateVisuals(float time)
        {
            if (presentation) presentation.TickVisuals(Vector3.Distance(PreviousPosition, transform.position), Motion.Speed,
                Motion.Speed < ContactSpeed - 0.01f, Lane.Kind, Motion.Distance, Lane.JunctionExit, time);
            ContactSpeed = Mathf.Max(ContactSpeed, Motion.Speed);
        }
        public void ApplyPosition() { transform.SetPositionAndRotation(Lane.Position(Motion.Distance), Lane.RotationAt(Motion.Distance)); }
        public bool SweptContact(CharacterController player, Vector3 oldPosition)
        {
            Vector3 offset = transform.position - oldPosition;
            Vector3 centre = oldPosition + Vector3.up * (halfHeight + 0.12f);
            Vector3 extents = new Vector3(HalfWidth, halfHeight, Motion.BodyHalfLength);
            if (offset.sqrMagnitude > 0.000001f)
            {
                int count;
                while ((count = Physics.BoxCastNonAlloc(centre, extents, offset.normalized, sweepHits, transform.rotation, offset.magnitude, 1 << player.gameObject.layer, QueryTriggerInteraction.Ignore)) == sweepHits.Length)
                    System.Array.Resize(ref sweepHits, sweepHits.Length * 2);
                for (int i = 0; i < count; i++) if (sweepHits[i].collider == player) return true;
            }
            // OverlapBox also catches an initial/resting physical contact, including zero-speed vehicles.
            int overlaps;
            while ((overlaps = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * (halfHeight + 0.12f), extents, overlapHits, transform.rotation, 1 << player.gameObject.layer, QueryTriggerInteraction.Ignore)) == overlapHits.Length)
                System.Array.Resize(ref overlapHits, overlapHits.Length * 2);
            for (int i = 0; i < overlaps; i++) if (overlapHits[i] == player) return true;
            return false;
        }
    }
}

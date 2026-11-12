using UnityEngine;

namespace ClawMachine
{
    public sealed class GripperController : MonoBehaviour
    {
        [SerializeField] private Rigidbody toolBody;
        [SerializeField] private Transform[] fingers;
        [SerializeField] private Transform[] contactPads;
        private FixedJoint grip;
        private PrizePhysics held;
        private float openness = 1;
        public bool IsConfigured => toolBody && fingers != null && fingers.Length == 3 && contactPads != null && contactPads.Length == 3;
        public bool IsHolding => held && grip;
        public int LastContacts { get; private set; }
        public bool Slipped { get; private set; }
        public void SetOpenness(float amount)
        {
            openness = Mathf.Clamp01(amount);
            if (fingers == null) return;
            for (int i = 0; i < fingers.Length; i++)
            {
                if (!fingers[i]) continue;
                float angle = (90 + i * 120) * Mathf.Deg2Rad;
                float radius = Mathf.Lerp(0.105f, 0.18f, openness);
                fingers[i].localPosition = new Vector3(Mathf.Cos(angle) * radius, 0.07f, Mathf.Sin(angle) * radius);
            }
        }
        public GraspFailure TryGrip(PrizePhysics prize)
        {
            Release(false); Slipped = false;
            GraspFailure result = GraspContactEvaluator.Evaluate(toolBody.position, prize, contactPads, out int contacts);
            LastContacts = contacts;
            if (result != GraspFailure.None) return result;
            // Preserve world pose: connectedAnchor is the current contact center, never teleport the prize.
            grip = toolBody.gameObject.AddComponent<FixedJoint>();
            grip.connectedBody = prize.Body; grip.autoConfigureConnectedAnchor = false;
            grip.anchor = Vector3.zero; grip.connectedAnchor = prize.Body.transform.InverseTransformPoint(toolBody.position);
            grip.breakForce = prize.Profile.breakForce; grip.breakTorque = prize.Profile.breakForce * 0.25f;
            grip.enableCollision = false; held = prize; return GraspFailure.None;
        }
        public void CheckGrip()
        {
            if (held && !grip) { Slipped = true; held.MarkReleased(); held = null; }
        }
        public void Release(bool released)
        {
            if (grip) { grip.connectedBody = null; Destroy(grip); grip = null; }
            if (held) { if (released) held.MarkReleased(); held = null; }
        }
        public void ResetGrip() { Release(false); Slipped = false; LastContacts = 0; SetOpenness(1); }
        private void OnDisable() { Release(false); }
        private void OnApplicationQuit() { Release(false); }
    }
}

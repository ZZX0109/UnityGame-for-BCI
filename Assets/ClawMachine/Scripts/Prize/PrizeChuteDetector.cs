using UnityEngine;

namespace ClawMachine
{
    public sealed class PrizeChuteDetector : MonoBehaviour
    {
        [SerializeField] private BoxCollider settlementVolume;
        private int sessionId, attemptId;
        private float dwell;
        public bool IsConfigured => settlementVolume && settlementVolume.isTrigger;
        public float StableTime => dwell;
        public void Arm(int session, int attempt) { sessionId = session; attemptId = attempt; dwell = 0; }
        public void Clear() { sessionId = attemptId = 0; dwell = 0; }
        public bool Observe(PrizePhysics prize, float dt)
        {
            if (!IsConfigured || !prize || !prize.IsConfigured || !prize.Profile || !prize.Released ||
                prize.SessionId != sessionId || prize.AttemptId != attemptId || !Contains(prize.AllBounds) || !prize.IsStable)
            { dwell = 0; return false; }
            dwell += Mathf.Max(0, dt); return dwell >= prize.Profile.stableDwell;
        }
        public bool Contains(Bounds prize)
        {
            // All eight corners must be within the oriented trigger. A first-touch callback cannot award.
            if (!IsConfigured) return false;
            Bounds local = new Bounds(settlementVolume.center, settlementVolume.size);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = prize.center + Vector3.Scale(prize.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                if (!local.Contains(settlementVolume.transform.InverseTransformPoint(p))) return false;
            }
            return true;
        }
    }
}

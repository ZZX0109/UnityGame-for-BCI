using UnityEngine;

namespace ClawMachine
{
    [CreateAssetMenu(menuName = "Claw Machine/Prize Profile")]
    public sealed class PrizeProfile : ScriptableObject
    {
        [TextArea] public string strategyHint = "对准规则礼盒的中心，三指接触侧面。";
        [Min(0.05f)] public float mass = 0.3f;
        public Vector3 centerOfMass;
        [Min(0.005f)] public float maxCenterOffset = 0.05f;
        [Min(0.001f)] public float contactTolerance = 0.008f;
        [Min(0.0001f)] public float maxPenetration = 0.003f;
        [Range(0.01f, 0.2f)] public float surfaceCaptureDepth = 0.09f;
        [Range(0.10f, 0.3f)] public float approachClearance = 0.16f;
        [Range(0.03f, 0.12f)] public float contactDescentSpeed = 0.075f;
        [Min(0.005f)] public float verticalTolerance = 0.045f;
        [Min(0.04f)] public float minimumContactSpan = 0.10f;
        [Min(1)] public float breakForce = 24;
        [Min(0.01f)] public float breakTorque = 1.2f;
        [Min(0.01f)] public float maximumGripDrift = 0.035f;
        [Min(0.05f)] public float stableSpeed = 0.08f;
        [Min(0.1f)] public float stableDwell = 0.3f;
        [Min(0.1f)] public float settleTimeout = 3;
    }
}

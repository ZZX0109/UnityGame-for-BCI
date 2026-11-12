using UnityEngine;

namespace ClawMachine
{
    [CreateAssetMenu(menuName = "Claw Machine/Prize Profile")]
    public sealed class PrizeProfile : ScriptableObject
    {
        [Min(0.05f)] public float mass = 0.3f;
        public Vector3 centerOfMass;
        [Min(0.005f)] public float maxCenterOffset = 0.05f;
        [Min(0.005f)] public float contactTolerance = 0.035f;
        [Min(0.005f)] public float verticalTolerance = 0.045f;
        [Min(1)] public float breakForce = 24;
        [Min(0.05f)] public float stableSpeed = 0.08f;
        [Min(0.1f)] public float stableDwell = 0.3f;
        [Min(0.1f)] public float settleTimeout = 2.5f;
    }
}

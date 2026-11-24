using UnityEngine.EventSystems;
namespace Neuralife.XR
{
    // Compatibility component for existing rigs. All VR UI uses stick/A navigation.
    public sealed class PicoRayInputModule : BaseInputModule
    {
        public static PicoRayInputModule Current { get; private set; }
        public bool HasTarget => false;
        public override bool IsModuleSupported() => PicoXR.Requested;
        protected override void OnEnable() { base.OnEnable(); Current = this; }
        protected override void OnDisable() { if (Current == this) Current = null; base.OnDisable(); }
        public void RefreshRay(PicoRig source) { }
        public override void Process() { }
    }
}

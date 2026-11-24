using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Produces a 20-control pose for one hand. Every input source (keyboard,
    /// controller grip, XR optical joints, replay, mirror) implements this
    /// contract so <see cref="HandInputRouter"/> can swap between providers
    /// without the driver knowing where the pose came from.
    ///
    /// Wrist pose and finger pose are deliberately separate: a host may track the
    /// wrist with a controller while the fingers come from a glove, and that
    /// combination must be labelled as such rather than presented as optical
    /// hand tracking.
    /// </summary>
    public interface IHandPoseProvider
    {
        /// <summary>Human-readable identifier shown in UI and logs.</summary>
        string ProviderName { get; }

        /// <summary>Whether the provider currently holds valid data.</summary>
        bool IsActive { get; }

        /// <summary>
        /// Category of the provided data:
        ///   "device"   – real hardware (controller, XR optical joints).
        ///   "replay"   – stored offline file.
        ///   "manual"   – keyboard, editor slider, explicit override.
        ///   "mirror"   – derived from the opposite hand.
        ///   "inferred" – EEG or auxiliary prediction.
        /// </summary>
        string SourceCategory { get; }

        /// <summary>
        /// The 20-control array for the given hand side, or null when no valid
        /// data is available. Callers must not mutate the returned array.
        /// </summary>
        float[] GetPose(SeatedHandSide side);

        /// <summary>
        /// Wrist position/rotation in world space. Returns false when this
        /// provider supplies no wrist tracking (e.g. keyboard).
        /// </summary>
        bool TryGetWrist(SeatedHandSide side, out Vector3 worldPosition, out Quaternion worldRotation);
    }
}
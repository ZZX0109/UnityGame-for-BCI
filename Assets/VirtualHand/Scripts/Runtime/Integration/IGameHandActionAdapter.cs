namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Each game implements this adapter to map recognised hand actions
    /// (grasp, point, release, etc.) to game‑specific behaviour without
    /// the hand module knowing about box‑pushing, crane controls or
    /// traffic‑light rules.
    /// </summary>
    public interface IGameHandActionAdapter
    {
        /// <summary>The game's own identifier, used in logs and debug UI.</summary>
        string GameName { get; }

        /// <summary>Called once per frame after the hand pose has been computed.</summary>
        void OnHandUpdate(
            HandTrackingFrame leftFrame,
            HandTrackingFrame rightFrame,
            HandGestureEvent leftGesture,
            HandGestureEvent rightGesture);
    }

    /// <summary>A discrete, single‑frame gesture recognised from the input.</summary>
    public enum HandGestureType
    {
        None,
        Open,
        Fist,
        Pinch,
        Point,
        GraspStart,
        GraspEnd,
        Release
    }

    public struct HandGestureEvent
    {
        public HandGestureType type;
        public SeatedHandSide side;
        public float confidence;
        public bool isSimulated;   // true when derived from grip/keyboard, not optical
        public double timestamp;
    }
}
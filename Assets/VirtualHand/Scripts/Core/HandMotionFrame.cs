using System;

namespace Neuralife.VirtualHand
{
    // Wire contract v1. Pose values are normalized rig controls, not measured joint degrees.
    [Serializable]
    public sealed class HandMotionFrame
    {
        public int version = 1;
        public double time;
        public double duration;
        public string intent = "rest";
        public float confidence = 1f;
        public string source = "manual";
        public string subject = "";
        public string run = "";
        public float[] leftJoints;
        public float[] rightJoints;
    }

    public interface IHandMotionSource
    {
        event Action<HandMotionFrame> FrameReady;
        bool IsPlaying { get; }
        double Position { get; }
        double Duration { get; }
        void Play();
        void Pause();
        void ResetPlayback();
    }
}

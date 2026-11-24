using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.XR
{
    // One session of concise, stationary-only guidance. No replay on pause/retry.
    public sealed class PicoQuietHints
    {
        private readonly HashSet<string> seen = new HashSet<string>();
        private bool started, sampled;
        private Vector3 anchor;
        private Quaternion rotation;
        private float introUntil, messageUntil, movingUntil;
        public bool ControlsVisible { get; private set; }
        public bool MessageVisible { get; private set; }
        public string Message { get; private set; }

        public void Tick(bool playing, float now, Vector3 position, Quaternion facing, bool locomotion)
        {
            ControlsVisible = MessageVisible = false;
            if (!playing) { sampled = false; return; }
            if (!started) { started = true; introUntil = now + 6; }
            bool moved = locomotion || (sampled && (Vector3.Distance(anchor, position) > 0.02f || Quaternion.Angle(rotation, facing) > 6));
            if (!sampled || moved) { anchor = position; rotation = facing; sampled = true; }
            if (moved) { movingUntil = now + 0.5f; introUntil = messageUntil = 0; }
            if (now < movingUntil) return;
            ControlsVisible = now < introUntil;
            MessageVisible = now < messageUntil;
        }
        public bool Notify(string message, float now)
        {
            if (string.IsNullOrEmpty(message) || now < movingUntil || !seen.Add(message)) return false;
            Message = message; messageUntil = now + 3;
            return true;
        }
    }
}

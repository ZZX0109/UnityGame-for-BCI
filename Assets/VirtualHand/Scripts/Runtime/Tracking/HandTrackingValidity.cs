using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Tracks whether a hand's tracking data is currently trustworthy, and for how
    /// long it may be reused after the last valid frame.
    ///
    /// Behaviour that the interaction layer depends on:
    ///   • A few missing frames hold the last good pose (avoids visible snapping).
    ///   • Past the hold window the hand is marked not-interactable, but the pose
    ///     is still held visually — critically it is NOT opened, because opening
    ///     would look like a release and the game would drop whatever it holds.
    ///   • Recovery reports a resync so the caller can re-align coordinates before
    ///     accepting new interaction.
    ///   • Each hand is independent: losing the left must not disturb a valid right.
    /// </summary>
    public sealed class HandTrackingValidity
    {
        /// <summary>Seconds of missing frames during which the last pose is reused.</summary>
        public float holdSeconds = 0.15f;

        /// <summary>Seconds after recovery before interaction events are accepted again.</summary>
        public float resyncSeconds = 0.35f;

        public bool HasEverTracked { get; private set; }
        public bool IsTracking { get; private set; }
        public bool IsInteractive { get; private set; }
        public bool NeedsResync { get; private set; }

        /// <summary>Seconds since the last frame that carried valid data.</summary>
        public float SecondsSinceValid => _sinceValid;

        /// <summary>Frames of consecutive misses since the last valid frame.</summary>
        public int MissingFrames { get; private set; }

        /// <summary>Ratio of joints that were valid in the most recent frame.</summary>
        public float LastJointValidityRatio { get; private set; }

        float _sinceValid = float.MaxValue;
        float _resyncRemaining;

        /// <summary>Feed one tracking frame. Pass null / all-invalid to report a miss.</summary>
        public void Report(HandTrackingFrame frame, float deltaTime)
        {
            _sinceValid += deltaTime;

            bool anyValid = frame.AnyValid;
            LastJointValidityRatio = JointRatio(frame);

            if (anyValid)
            {
                if (!IsTracking && HasEverTracked)
                {
                    // Recovered after a loss: require a resync before new events.
                    NeedsResync = true;
                    _resyncRemaining = resyncSeconds;
                }

                MissingFrames = 0;
                _sinceValid = 0f;
                IsTracking = true;
                HasEverTracked = true;
            }
            else
            {
                MissingFrames++;
                IsTracking = false;
            }

            if (_resyncRemaining > 0f)
            {
                _resyncRemaining -= deltaTime;
                NeedsResync = _resyncRemaining > 0f;
            }

            // Interactive only while genuinely tracked and past any resync window.
            IsInteractive = IsTracking && !NeedsResync;
        }

        /// <summary>True while the last known pose may still be displayed.</summary>
        public bool MayDisplayCachedPose => HasEverTracked && _sinceValid <= holdSeconds;

        /// <summary>Reset after a teleport, source switch or explicit recalibration.</summary>
        public void Reset()
        {
            _sinceValid = float.MaxValue;
            _resyncRemaining = 0f;
            MissingFrames = 0;
            IsTracking = false;
            IsInteractive = false;
            NeedsResync = false;
            LastJointValidityRatio = 0f;
        }

        static float JointRatio(HandTrackingFrame frame)
        {
            if (frame.jointValidity == null || frame.jointValidity.Length == 0)
                return frame.wristPositionValid || frame.wristRotationValid ? 1f : 0f;

            int valid = 0;
            for (int i = 0; i < frame.jointValidity.Length; i++)
                if (frame.jointValidity[i]) valid++;

            return (float)valid / frame.jointValidity.Length;
        }
    }
}
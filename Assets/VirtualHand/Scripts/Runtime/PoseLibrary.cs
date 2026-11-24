using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Authored 20-control poses for the four intents plus interpolation helpers.
    /// Control layout (finger-major):
    /// 0-3 thumb CMC flex, MCP flex, IP flex, opposition
    /// 4-7 index MCP,PIP,DIP,spread; 8-11 middle; 12-15 ring; 16-19 little.
    /// </summary>
    public static class PoseLibrary
    {
        public static float[] Rest()
        {
            var p = new float[20];
            // subtle natural curl so rest doesn't look dead
            for (int f = 0; f < 4; f++)
            {
                int o = (f + 1) * 4;
                p[o] = 0.10f + 0.02f * f; p[o + 1] = 0.14f; p[o + 2] = 0.10f; p[o + 3] = 0.5f;
            }
            p[0] = 0.05f; p[1] = 0.08f; p[2] = 0.05f; p[3] = 0.15f;
            return p;
        }

        public static float[] Fist()
        {
            var p = new float[20];
            for (int f = 0; f < 4; f++)
            {
                int o = (f + 1) * 4;
                p[o] = 0.95f; p[o + 1] = 1f; p[o + 2] = 0.9f; p[o + 3] = 0.15f;
            }
            p[0] = 0.55f; p[1] = 0.75f; p[2] = 0.7f; p[3] = 0.85f; // thumb wraps
            return p;
        }

        public static float[] Pinch()
        {
            var p = new float[20];
            // index half-closed meeting opposed thumb
            p[4] = 0.55f; p[5] = 0.35f; p[6] = 0.25f; p[7] = 0.3f;
            // others relaxed curl
            p[8] = 0.3f; p[9] = 0.4f; p[10] = 0.3f; p[11] = 0.4f;
            p[12] = 0.35f; p[13] = 0.45f; p[14] = 0.35f; p[15] = 0.4f;
            p[16] = 0.4f; p[17] = 0.5f; p[18] = 0.4f; p[19] = 0.35f;
            p[0] = 0.4f; p[1] = 0.5f; p[2] = 0.4f; p[3] = 1f; // full opposition
            return p;
        }

        public static float[] Point()
        {
            var p = Rest();
            p[4] = 0f; p[5] = 0f; p[6] = 0f; p[7] = 0.5f; // index straight
            for (int f = 2; f < 5; f++) // middle, ring, little curl
            {
                int o = f * 4;
                p[o] = 0.85f; p[o + 1] = 0.95f; p[o + 2] = 0.8f;
            }
            p[0] = 0.2f; p[1] = 0.3f; p[2] = 0.25f; p[3] = 0.4f;
            return p;
        }

        /// <summary>Pose for a named intent (rest|left|right|both) on one hand.</summary>
        public static float[] ForIntent(string intent, bool isLeftHand, string activeIntentForThisHand)
        {
            // "both" means this hand also grips; left/right grips only the matching hand.
            bool grip = activeIntentForThisHand == "fist";
            return grip ? Fist() : Rest();
        }

        /// <summary>Linear blend between two poses.</summary>
        public static float[] Lerp(float[] a, float[] b, float t)
        {
            var r = new float[20];
            for (int i = 0; i < 20; i++) r[i] = Mathf.Lerp(a[i], b[i], t);
            return r;
        }

        /// <summary>Authored grip pose used when a frame lacks a per-hand pose.</summary>
        public static float[] GripPose() => Fist();

        /// <summary>Authored open/rest pose used when a frame lacks a per-hand pose.</summary>
        public static float[] OpenPose() => Rest();

        // ---- profile-aware defaults (A's unified Blender/Unity 20-way poses) ----
        // Used only when a driver has a loaded rig profile; fall back to the
        // authored poses otherwise. No angle compensation is done here.

        static float[] ProfileOr(HandRigDriver driver, float[] profiled, float[] authored)
        {
            if (driver != null && driver.HasProfile && profiled != null &&
                profiled.Length == 20)
                return profiled;
            return authored;
        }

        /// <summary>Default rest/open pose for this driver (profile sample when available).</summary>
        public static float[] OpenPose(HandRigDriver driver) =>
            ProfileOr(driver, driver?.ProfileOpen, Rest());

        /// <summary>Default fist/grip pose for this driver (profile sample when available).</summary>
        public static float[] FistPose(HandRigDriver driver) =>
            ProfileOr(driver, driver?.ProfileFist, Fist());

        /// <summary>Default pinch pose for this driver (profile sample when available).</summary>
        public static float[] PinchPose(HandRigDriver driver) =>
            ProfileOr(driver, driver?.ProfilePinch, Pinch());
    }

    /// <summary>
    /// Maps an intent string to per-hand target pose keys, with hysteresis-free
    /// direct mapping. "left"/"right" only curls the matching hand; "both" curls both.
    /// </summary>
    public static class IntentMapper
    {
        public static bool HandShouldGrip(string intent, bool isLeftHand)
        {
            switch (intent)
            {
                case "both": return true;
                case "left": return isLeftHand;
                case "right": return !isLeftHand;
                default: return false; // rest
            }
        }
    }
}

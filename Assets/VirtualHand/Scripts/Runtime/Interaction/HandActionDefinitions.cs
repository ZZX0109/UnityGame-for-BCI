using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// One bindable hand action. Covers the continuous poses (Open/Fist/Pinch/
    /// Point/Release), the per-finger flexion channels, and the presentation and
    /// guidance commands that a player can map to a key.
    /// </summary>
    public enum HandAction
    {
        None = 0,

        // ---- whole-hand poses ----
        Open = 10,
        Fist = 11,
        Pinch = 12,
        Point = 13,
        Release = 14,

        // ---- single-finger flexion (allows combinations) ----
        IndexFlex = 20,
        MiddleFlex = 21,
        RingFlex = 22,
        LittleFlex = 23,
        ThumbFlex = 24,

        // ---- presentation / guidance ----
        ToggleAppearance = 30,
        GuidanceStart = 31,
        GuidanceEnd = 32
    }

    /// <summary>Whether a binding fires on press or needs to be held.</summary>
    public enum HandActionTrigger
    {
        OnPress = 0,
        WhileHeld = 1,
        OnRelease = 2
    }

    /// <summary>
    /// Static description of every action: display name, which hand it can drive,
    /// whether it is a continuous pose or an edge event, and the 20-control values
    /// it produces for a single finger.
    ///
    /// The 20-control layout is the frozen contract of <see cref="HandRigDriver"/>:
    ///   0-3  thumb  CMC flex, MCP flex, IP flex, opposition
    ///   4-7  index  MCP, PIP, DIP, spread
    ///   8-11 middle
    ///   12-15 ring
    ///   16-19 little
    /// </summary>
    public static class HandActionDefinitions
    {
        /// <summary>Finger index used by the control layout: 0 thumb … 4 little.</summary>
        public static int FingerOf(HandAction action)
        {
            switch (action)
            {
                case HandAction.ThumbFlex: return 0;
                case HandAction.IndexFlex: return 1;
                case HandAction.MiddleFlex: return 2;
                case HandAction.RingFlex: return 3;
                case HandAction.LittleFlex: return 4;
                default: return -1;
            }
        }

        /// <summary>True for actions that are a discrete command rather than a pose.</summary>
        public static bool IsCommand(HandAction action) =>
            action == HandAction.ToggleAppearance ||
            action == HandAction.GuidanceStart ||
            action == HandAction.GuidanceEnd ||
            action == HandAction.None;

        /// <summary>True for whole-hand poses.</summary>
        public static bool IsPose(HandAction action) =>
            action == HandAction.Open || action == HandAction.Fist ||
            action == HandAction.Pinch || action == HandAction.Point ||
            action == HandAction.Release;

        public static string DisplayName(HandAction action)
        {
            switch (action)
            {
                case HandAction.Open: return "张开 Open";
                case HandAction.Fist: return "握拳 Fist";
                case HandAction.Pinch: return "捏取 Pinch";
                case HandAction.Point: return "指向 Point";
                case HandAction.Release: return "松开 Release";
                case HandAction.IndexFlex: return "食指弯曲";
                case HandAction.MiddleFlex: return "中指弯曲";
                case HandAction.RingFlex: return "无名指弯曲";
                case HandAction.LittleFlex: return "小指弯曲";
                case HandAction.ThumbFlex: return "拇指弯曲";
                case HandAction.ToggleAppearance: return "切换外观";
                case HandAction.GuidanceStart: return "教学开始";
                case HandAction.GuidanceEnd: return "教学结束";
                default: return "未绑定";
            }
        }

        /// <summary>
        /// Resolves a whole-hand pose to 20 controls, preferring the driver's
        /// sampled native profile so the result matches the authored model.
        /// Returns a freshly allocated array the caller owns.
        /// </summary>
        public static float[] ResolvePose(HandAction action, HandRigDriver driver)
        {
            switch (action)
            {
                case HandAction.Open:
                case HandAction.Release:
                    return PoseLibrary.OpenPose(driver);
                case HandAction.Fist:
                    return PoseLibrary.FistPose(driver);
                case HandAction.Pinch:
                    return PoseLibrary.PinchPose(driver);
                case HandAction.Point:
                    return PoseLibrary.Point();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Writes one finger's flexion into an existing control array.
        /// <paramref name="amount"/> is 0 (straight) to 1 (fully curled).
        /// </summary>
        public static void ApplyFingerFlex(float[] controls, int finger, float amount)
        {
            if (controls == null || controls.Length != 20 || finger < 0 || finger > 4)
                return;

            amount = Mathf.Clamp01(amount);
            if (finger == 0)
            {
                // Thumb: CMC, MCP, IP flex, and a little opposition so the tip
                // comes across the palm instead of curling straight down.
                controls[0] = amount;
                controls[1] = amount;
                controls[2] = amount;
                controls[3] = 0.35f + 0.65f * amount;
                return;
            }

            int o = finger * 4;
            controls[o] = amount;
            controls[o + 1] = amount;
            controls[o + 2] = amount;
            // spread stays untouched so a combination of flexes keeps its splay
        }
    }
}
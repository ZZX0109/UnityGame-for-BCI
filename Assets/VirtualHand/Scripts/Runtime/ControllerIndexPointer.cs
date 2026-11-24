using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Reads a ray from calibrated index landmarks on the displayed skinned hand.
    /// It only returns a ray while that side is visibly driven by a tracked controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerIndexPointer : MonoBehaviour
    {
        [Serializable]
        public struct IndexAnatomy
        {
            [Tooltip("Landmark at the base of the distal index segment on this hand model.")]
            public Transform baseJoint;
            [Tooltip("Landmark at the visible fingertip. Place it at the desired ray origin.")]
            public Transform tip;
            [Tooltip("Optional fingertip surface offset, in the tip landmark's local space.")]
            public Vector3 tipSurfaceOffsetLocal;
        }

        public ControllerHandPair handPair;
        public IndexAnatomy leftIndexAnatomy;
        public IndexAnatomy rightIndexAnatomy;

        private Transform leftRendererRoot;
        private Transform rightRendererRoot;
        private SkinnedMeshRenderer[] leftRenderers;
        private SkinnedMeshRenderer[] rightRenderers;
        private ControllerHandPair presetPair;
        private SeatedHandSide presetSide = SeatedHandSide.Unspecified;
        private bool hasPresetRequest;

        /// <summary>Returns the actual visible index ray for an explicitly selected side.</summary>
        public bool TryGetIndexRay(SeatedHandSide side, out Ray ray)
        {
            ray = default;
            if (!isActiveAndEnabled || !handPair || !handPair.isActiveAndEnabled)
                return false;

            FirstPersonWrist wrist;
            IndexAnatomy anatomy;
            ControllerHandPair.PoseSource source;
            if (side == SeatedHandSide.Left)
            {
                wrist = handPair.leftWrist;
                anatomy = leftIndexAnatomy;
                source = handPair.LeftSource;
            }
            else if (side == SeatedHandSide.Right)
            {
                wrist = handPair.rightWrist;
                anatomy = rightIndexAnatomy;
                source = handPair.RightSource;
            }
            else
            {
                return false;
            }

            if (!handPair.TryGetPhysicalGrip(side, .15f, out _) ||
                source != ControllerHandPair.PoseSource.Controller || !wrist ||
                !wrist.isActiveAndEnabled || !wrist.visualRoot ||
                !wrist.visualRoot.gameObject.activeInHierarchy)
                return false;

            Transform visualRoot = wrist.visualRoot;
            if (!HasVisibleSkinnedRenderer(side, visualRoot))
                return false;

            return TryBuildRay(anatomy, visualRoot, out ray);
        }

        /// <summary>
        /// Explicitly enables or clears the visual pointing pose for one side.
        /// Grip samples and HandRigDriver profile data are not changed.
        /// </summary>
        public bool RequestPointingDisplayPreset(SeatedHandSide side, bool requested)
        {
            if (!isActiveAndEnabled || !handPair ||
                (side != SeatedHandSide.Left && side != SeatedHandSide.Right))
                return false;

            if (!handPair.SetPointingDisplayPreset(side, requested))
                return false;

            if (requested)
            {
                presetPair = handPair;
                presetSide = side;
                hasPresetRequest = true;
            }
            else if (hasPresetRequest && presetSide == side)
            {
                presetPair = null;
                presetSide = SeatedHandSide.Unspecified;
                hasPresetRequest = false;
            }

            return true;
        }

        private void OnDisable()
        {
            if (hasPresetRequest && presetPair)
                presetPair.SetPointingDisplayPreset(presetSide, false);
            presetPair = null;
            presetSide = SeatedHandSide.Unspecified;
            hasPresetRequest = false;
        }

        private bool HasVisibleSkinnedRenderer(SeatedHandSide side, Transform visualRoot)
        {
            if (side == SeatedHandSide.Left)
            {
                if (leftRendererRoot != visualRoot || leftRenderers == null)
                {
                    leftRendererRoot = visualRoot;
                    leftRenderers = visualRoot
                        ? visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        : Array.Empty<SkinnedMeshRenderer>();
                }
                return HasVisibleSkinnedRenderer(leftRenderers);
            }

            if (rightRendererRoot != visualRoot || rightRenderers == null)
            {
                rightRendererRoot = visualRoot;
                rightRenderers = visualRoot
                    ? visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    : Array.Empty<SkinnedMeshRenderer>();
            }
            return HasVisibleSkinnedRenderer(rightRenderers);
        }

        private static bool HasVisibleSkinnedRenderer(SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null)
                return false;

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sharedMesh)
                    return true;
            }
            return false;
        }

        private static bool TryBuildRay(IndexAnatomy anatomy, Transform visualRoot, out Ray ray)
        {
            ray = default;
            Transform baseJoint = anatomy.baseJoint;
            Transform tip = anatomy.tip;
            if (!visualRoot || !baseJoint || !tip || baseJoint == tip ||
                !baseJoint.IsChildOf(visualRoot) || !tip.IsChildOf(visualRoot) ||
                !baseJoint.gameObject.activeInHierarchy || !tip.gameObject.activeInHierarchy ||
                !IsFinite(anatomy.tipSurfaceOffsetLocal))
                return false;

            Vector3 origin = tip.TransformPoint(anatomy.tipSurfaceOffsetLocal);
            Vector3 direction = tip.position - baseJoint.position;
            float lengthSquared = direction.sqrMagnitude;
            if (!IsFinite(origin) || !IsFinite(direction) || !IsFinite(lengthSquared) || lengthSquared <= 1e-8f)
                return false;

            float length = Mathf.Sqrt(lengthSquared);
            if (!IsFinite(length) || length <= 1e-8f)
                return false;

            ray = new Ray(origin, direction / length);
            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

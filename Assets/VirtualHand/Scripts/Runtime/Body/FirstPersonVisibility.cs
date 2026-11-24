using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// First-person visibility switch for the avatar.  First-person mode hides the head
    /// renderers (plus any additional hidden renderers) so the wearer camera does not clip
    /// into the skull; every touched renderer returns to its initial enabled state when
    /// first person is left, mirroring <c>GameIntegrationAdapter.SetFirstPerson</c>.
    /// Only renderer visibility is touched: bones, animation, physics and transforms stay
    /// exactly as they are, and no fading is applied.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Body/First Person Visibility")]
    [DisallowMultipleComponent]
    public sealed class FirstPersonVisibility : MonoBehaviour
    {
        [Tooltip("Camera driven while first person is active. Unset falls back to Camera.main.")]
        public Camera FirstPersonCamera;

        [Tooltip("Head renderers hidden while first person is active (the body stays visible).")]
        public SkinnedMeshRenderer[] HiddenRenderers = new SkinnedMeshRenderer[0];

        [Tooltip("Extra renderers hidden while first person is active (helmets, hats, ...).")]
        public Renderer[] AdditionalHidden = new Renderer[0];

        readonly List<Renderer> snapshotRenderers = new List<Renderer>();
        readonly List<bool> snapshotEnabledStates = new List<bool>();

        /// <summary>True while first-person mode is active.</summary>
        public bool FirstPersonActive { get; private set; }

        /// <summary>
        /// The camera that drives first person: <see cref="FirstPersonCamera"/> when assigned,
        /// otherwise <c>Camera.main</c>.  May be null (for example in edit mode) and never throws.
        /// </summary>
        public Camera ResolvedFirstPersonCamera
        {
            get { return FirstPersonCamera != null ? FirstPersonCamera : Camera.main; }
        }

        /// <summary>Turns first-person mode on or off and applies it to the assigned renderers.</summary>
        public void SetFirstPerson(bool firstPerson)
        {
            if (firstPerson)
            {
                // Capture only on the off -> on transition so a repeated request never
                // snapshots the already hidden states.
                if (!FirstPersonActive)
                    CaptureSnapshot();
                ApplyHidden(true);
            }
            else if (FirstPersonActive)
            {
                RestoreSnapshot();
            }

            FirstPersonActive = firstPerson;
        }

        void OnEnable()
        {
            // Safety net: first person is still requested, so resume it.  The previous
            // snapshot was released by OnDisable, and the renderers currently show their
            // original states again, which is exactly what the new snapshot must hold.
            if (FirstPersonActive)
            {
                CaptureSnapshot();
                ApplyHidden(true);
            }
        }

        void OnDisable()
        {
            // Safety net: never leave the avatar headless because this component or its
            // host was disabled while first person was active.
            if (FirstPersonActive)
                RestoreSnapshot();
        }

        void OnDestroy()
        {
            if (FirstPersonActive)
                RestoreSnapshot();
        }

        void CaptureSnapshot()
        {
            snapshotRenderers.Clear();
            snapshotEnabledStates.Clear();
            CaptureFrom(HiddenRenderers);
            CaptureFrom(AdditionalHidden);
        }

        void CaptureFrom(Renderer[] renderers)
        {
            if (renderers == null)
                return;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                snapshotRenderers.Add(renderer);
                snapshotEnabledStates.Add(renderer.enabled);
            }
        }

        void CaptureFrom(SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null)
                return;
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                snapshotRenderers.Add(renderer);
                snapshotEnabledStates.Add(renderer.enabled);
            }
        }

        void RestoreSnapshot()
        {
            for (int i = 0; i < snapshotRenderers.Count; i++)
            {
                Renderer renderer = snapshotRenderers[i];
                if (renderer != null)
                    renderer.enabled = snapshotEnabledStates[i];
            }
            snapshotRenderers.Clear();
            snapshotEnabledStates.Clear();
        }

        void ApplyHidden(bool hidden)
        {
            ApplyHidden(HiddenRenderers, hidden);
            ApplyHidden(AdditionalHidden, hidden);
        }

        void ApplyHidden(Renderer[] renderers, bool hidden)
        {
            if (renderers == null)
                return;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    renderer.enabled = !hidden;
            }
        }

        void ApplyHidden(SkinnedMeshRenderer[] renderers, bool hidden)
        {
            if (renderers == null)
                return;
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer != null)
                    renderer.enabled = !hidden;
            }
        }
    }
}

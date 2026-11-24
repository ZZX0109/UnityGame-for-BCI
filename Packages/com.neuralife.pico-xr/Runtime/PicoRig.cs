using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Neuralife.XR
{
    /// <summary>Owns tracked poses. Desktop camera code must leave these transforms alone.</summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class PicoRig : MonoBehaviour
    {
        public Camera View { get; private set; }
        public Transform TrackingSpace { get; private set; }
        public bool HasTrackedPose { get; private set; }
        public float RoomscaleDistance { get; private set; }
        private CharacterController body;
        private float eyeHeight, fadeUntil;
        private bool previouslyTracked;
        private Image veil;

        public static PicoRig Attach(Camera camera, Transform playerRoot = null, float fallbackHeight = 1.65f)
        {
            if (!PicoXR.Requested || !camera) return null;
            PicoRig existing = camera.GetComponentInParent<PicoRig>();
            if (existing) return existing;
            if (!playerRoot)
            {
                playerRoot = new GameObject("PICO stationary player").transform;
                playerRoot.SetParent(camera.transform.parent, true);
                playerRoot.position = camera.transform.position - Vector3.up * fallbackHeight;
                playerRoot.rotation = Quaternion.Euler(0, camera.transform.eulerAngles.y, 0);
            }
            var rig = playerRoot.gameObject.AddComponent<PicoRig>();
            rig.View = camera; rig.eyeHeight = fallbackHeight;
            rig.body = playerRoot.GetComponent<CharacterController>();
            rig.TrackingSpace = new GameObject("PICO tracking space").transform;
            rig.TrackingSpace.SetParent(playerRoot, false);
            camera.transform.SetParent(rig.TrackingSpace, false);
            camera.transform.localPosition = Vector3.up * fallbackHeight;
            camera.transform.localRotation = Quaternion.identity;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.tag = "MainCamera";
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            camera.nearClipPlane = 0.05f;
            if (Application.isPlaying) XRDevice.DisableAutoXRCameraTracking(camera, true);
            foreach (AudioListener listener in FindObjectsOfType<AudioListener>()) listener.enabled = listener.gameObject == camera.gameObject;
            if (!camera.GetComponent<AudioListener>()) camera.gameObject.AddComponent<AudioListener>();
            rig.CreateVeil();
            return rig;
        }

        private void OnEnable() { Application.onBeforeRender += BeforeRender; }
        private void OnDisable() { Application.onBeforeRender -= BeforeRender; }
        private void BeforeRender() { PicoXR.ReadPoses(); ApplyPose(); }

        private void Update()
        {
            if (!View || !TrackingSpace) return;
            if (PicoXR.HeadTracked && !previouslyTracked) Recenter();
            previouslyTracked = PicoXR.HeadTracked;
            ApplyPose();
            if (PicoXR.HeadTracked && body && body.enabled)
            {
                float height = Mathf.Clamp(View.transform.position.y - transform.position.y + 0.08f, 0.5f, 2.4f);
                body.height = height; body.center = new Vector3(0, height * 0.5f, 0);
                Vector3 offset = View.transform.position - transform.position; offset.y = 0;
                Vector3 before = transform.position;
                body.Move(offset);
                Vector3 moved = transform.position - before;
                moved.y = 0;
                // Preserve the physical head pose while keeping the capsule below it.
                TrackingSpace.position -= moved;
                RoomscaleDistance += new Vector2(moved.x, moved.z).magnitude;
            }
            if (PicoXR.Controls.Recenter.Pressed) Recenter();
            if (PicoRayInputModule.Current) PicoRayInputModule.Current.RefreshRay(this);
            if (veil)
            {
                Vector3 error = View.transform.position - transform.position; error.y = 0;
                bool blocked = body && error.magnitude > 0.15f;
                float alpha = !PicoXR.HeadTracked || blocked ? 1 : Mathf.Clamp01((fadeUntil - Time.unscaledTime) / 0.12f);
                veil.color = new Color(0, 0, 0, alpha);
            }
        }

        private void ApplyPose()
        {
            if (!PicoXR.HeadTracked || !View || !TrackingSpace) return;
            HasTrackedPose = true;
            View.transform.localPosition = PicoXR.HeadPosition;
            View.transform.localRotation = PicoXR.HeadRotation;
        }

        /// <summary>Only the origin changes; the headset's local position/rotation remain sensor owned.</summary>
        public void Recenter()
        {
            if (!TrackingSpace || !View) return;
            Vector3 raw = PicoXR.HeadPosition;
            float yaw = PicoXR.HeadRotation.eulerAngles.y;
            TrackingSpace.localRotation = Quaternion.Euler(0, -yaw, 0);
            Vector3 centered = TrackingSpace.localRotation * raw;
            TrackingSpace.localPosition = new Vector3(-centered.x, PicoXR.FloorTracking ? 0 : eyeHeight - raw.y, -centered.z);
            fadeUntil = Time.unscaledTime + 0.12f;
            ApplyPose(); PicoCanvas.RecenterAll();
        }

        public void SnapTurn(float degrees)
        {
            if (!PicoXR.MovementAllowed || !View || Mathf.Approximately(degrees, 0)) return;
            Vector3 pivot = View.transform.position; pivot.y = transform.position.y;
            transform.RotateAround(pivot, Vector3.up, degrees);
            fadeUntil = Time.unscaledTime + 0.12f;
            PicoCanvas.RecenterAll();
        }

        private void CreateVeil()
        {
            var owner = new GameObject("VR comfort veil", typeof(RectTransform), typeof(Canvas));
            owner.transform.SetParent(View.transform, false);
            owner.transform.localPosition = new Vector3(0, 0, 0.08f);
            owner.GetComponent<RectTransform>().sizeDelta = new Vector2(0.5f, 0.5f);
            Canvas canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = View; canvas.sortingOrder = 32760;
            var fill = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(owner.transform, false);
            RectTransform rect = fill.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            veil = fill.GetComponent<Image>(); veil.raycastTarget = false; veil.color = Color.clear;
        }
    }
}

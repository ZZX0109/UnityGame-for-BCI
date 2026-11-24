using UnityEngine;
using UnityEngine.EventSystems;

namespace Neuralife.VirtualHand
{
    /// <summary>Orbit the hand preview only when the pointer is over its camera viewport.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public TaskController tasks;
        public Transform box;
        public RectTransform sidebar;
        static readonly Vector3 PreviewFocus = new Vector3(0f, 0.065f, 0f);
        Vector3 _focus = PreviewFocus;
        float _dist = 0.82f, _yaw = 0f, _pitch = -48f;
        Camera _camera;
        bool _dragging;

        public void FrameHands()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                _camera.nearClipPlane = 0.04f;
                _camera.farClipPlane = 25f;
            }
            ApplyPose();
        }

        void Awake() => FrameHands();

        void LateUpdate()
        {
            if (sidebar != null && Screen.width > 0)
            {
                float inset = RectTransformUtility.WorldToScreenPoint(null,
                    sidebar.TransformPoint(new Vector3(sidebar.rect.xMax, 0f))).x / Screen.width;
                inset = Mathf.Clamp(inset, 0f, 0.85f);
                _camera.rect = new Rect(inset, 0f, 1f - inset, 1f);
            }
            bool overModel = IsOverModel();
            if (Input.GetMouseButtonDown(1) && overModel) _dragging = true;
            if (Input.GetMouseButtonUp(1)) _dragging = false;

            if (_dragging && Input.GetMouseButton(1) && overModel)
            {
                _yaw += Input.GetAxis("Mouse X") * 3f;
                _pitch -= Input.GetAxis("Mouse Y") * 2f;
                _pitch = Mathf.Clamp(_pitch, -80f, -8f);
            }
            if (overModel)
            {
                _dist *= Mathf.Exp(-Input.GetAxis("Mouse ScrollWheel") * 0.8f);
                _dist = Mathf.Clamp(_dist, 0.45f, 2f);
            }
            ApplyPose();
        }

        bool IsOverModel()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera == null || !_camera.pixelRect.Contains(Input.mousePosition)) return false;
            return EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
        }

        void ApplyPose()
        {
            var q = Quaternion.Euler(_pitch, _yaw, 0f);
            bool active = tasks != null && tasks.Type != TaskController.TaskType.None && box != null;
            Vector3 target = PreviewFocus;
            float distance = _dist;
            if (active)
            {
                Bounds volume = new Bounds(box.position, Vector3.one * 0.12f);
                volume.Encapsulate(tasks.leftAnchor.position);
                volume.Encapsulate(tasks.rightAnchor.position);
                target = volume.center;
                distance *= Mathf.Max(1f, volume.size.magnitude / 0.53f);
            }
            _focus = Vector3.Lerp(_focus, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            transform.position = _focus + q * new Vector3(0f, 0f, distance);
            transform.LookAt(_focus);
        }
    }
}

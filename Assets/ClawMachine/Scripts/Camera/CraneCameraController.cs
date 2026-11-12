using UnityEngine;

namespace ClawMachine
{
    public sealed class CraneCameraController : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera[] views;
        [SerializeField] private Transform lookTarget;
        private Vector3[] defaultPositions;
        private int selected, distance = 1;
        public bool Smooth { get; set; } = true;
        public UnityEngine.Camera CurrentCamera => IsConfigured ? views[selected] : null;
        public string ViewName => selected == 0 ? "正面 Front" : selected == 1 ? "左侧 Left" : "右侧 Right";
        public string DistanceName => distance == 0 ? "近" : distance == 1 ? "标准" : "远";
        public bool IsConfigured => views != null && views.Length == 3 && views[0] && views[1] && views[2] && lookTarget;
        private void Awake()
        {
            if (!IsConfigured) return;
            defaultPositions = new Vector3[views.Length];
            for (int i = 0; i < views.Length; i++) defaultPositions[i] = views[i].transform.position;
            Select(1);
        }
        public void Select(int number)
        {
            if (!IsConfigured || number < 1 || number > 3) return;
            selected = number - 1;
            for (int i = 0; i < views.Length; i++) views[i].enabled = i == selected;
        }
        public void CycleDistance() { distance = (distance + 1) % 3; }
        public void Zoom(float amount) { if (amount != 0) distance = Mathf.Clamp(distance - (amount > 0 ? 1 : -1), 0, 2); }
        private void LateUpdate()
        {
            if (!IsConfigured || defaultPositions == null) return;
            float scale = distance == 0 ? 0.85f : distance == 1 ? 1 : 1.2f;
            UnityEngine.Camera camera = views[selected];
            Vector3 wanted = lookTarget.position + (defaultPositions[selected] - lookTarget.position) * scale;
            camera.transform.position = Smooth ? Vector3.Lerp(camera.transform.position, wanted, 1 - Mathf.Exp(-10 * Time.unscaledDeltaTime)) : wanted;
            camera.transform.LookAt(lookTarget.position);
        }
    }
}

using UnityEngine;

namespace StreetCrossing.Presentation
{
    [RequireComponent(typeof(TextMesh))]
    public sealed class WorldLabelFit : MonoBehaviour
    {
        public float maximumWidth = 3.3f;
        public float maximumHeight = 0.75f;
        private TextMesh text;
        private Renderer view;
        private string previous;
        private int refreshFrames;
        private void Awake() { text = GetComponent<TextMesh>(); view = GetComponent<Renderer>(); }
        private void LateUpdate()
        {
            if (text.text != previous) { previous = text.text; refreshFrames = 3; }
            if (refreshFrames-- <= 0) return;
            Bounds bounds = view.localBounds;
            if (bounds.size.x < 0.001f || bounds.size.y < 0.001f) return;
            float fit = Mathf.Min(1, Mathf.Min(maximumWidth / bounds.size.x, maximumHeight / bounds.size.y));
            transform.localScale = Vector3.one * fit;
        }
    }
}

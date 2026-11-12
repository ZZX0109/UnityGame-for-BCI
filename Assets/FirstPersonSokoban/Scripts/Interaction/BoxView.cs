using System.Collections;
using UnityEngine;

namespace FirstPersonSokoban.Interaction
{
    public sealed class BoxView : MonoBehaviour
    {
        private Renderer bodyRenderer;
        private Material normalMaterial;
        private Material goalMaterial;
        private Material deniedMaterial;
        private Coroutine feedbackRoutine;
        private bool isOnGoal;
        private GameObject highlightCap;
        private Material highlightMaterial;

        public Vector2Int GridPosition { get; private set; }
        public bool IsHighlighted => highlightCap != null && highlightCap.activeSelf;

        private void Awake()
        {
            Transform body = transform.Find("Body");
            if (body != null) bodyRenderer = body.GetComponent<Renderer>();
        }

        public void Initialize(Vector2Int gridPosition, Material normal, Material onGoal, Material denied)
        {
            GridPosition = gridPosition;
            normalMaterial = normal;
            goalMaterial = onGoal;
            deniedMaterial = denied;
            SetOnGoal(false);
            BuildHighlightCap();
        }

        /// <summary>瞄准时的高亮顶盖，不改箱体本身材质，避免闪烁反馈与归位材质互相覆盖。</summary>
        private void BuildHighlightCap()
        {
            if (highlightCap != null || normalMaterial == null) return;
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Aim Highlight Cap";
            Collider collider = cap.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Destroy(collider); }
            cap.transform.SetParent(transform, false);
            cap.transform.localPosition = new Vector3(0f, BoxDimensions.HighlightY, 0f);
            cap.transform.localScale = new Vector3(BoxDimensions.HighlightWidth,
                BoxDimensions.HighlightThickness, BoxDimensions.HighlightWidth);
            highlightMaterial = new Material(normalMaterial)
            {
                name = "Sokoban Aim Highlight (Runtime)",
                color = new Color(0.35f, 0.88f, 0.76f, 1f)
            };
            cap.GetComponent<Renderer>().sharedMaterial = highlightMaterial;
            cap.SetActive(false);
            highlightCap = cap;
        }

        public void SetHighlighted(bool highlighted)
        {
            if (highlightCap != null) highlightCap.SetActive(highlighted);
        }

        public void SetGridPosition(Vector2Int position) => GridPosition = position;

        public void SetOnGoal(bool complete)
        {
            isOnGoal = complete;
            if (bodyRenderer != null) bodyRenderer.sharedMaterial = complete ? goalMaterial : normalMaterial;
        }

        public void SnapTo(Vector3 position) => transform.position = position;

        private void OnDestroy()
        {
            // Only this instance owns the generated highlight. Resource materials stay shared.
            if (highlightMaterial != null) Destroy(highlightMaterial);
        }

        public void FlashDenied()
        {
            if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
            feedbackRoutine = StartCoroutine(DeniedFeedback());
        }

        private IEnumerator DeniedFeedback()
        {
            if (bodyRenderer != null && deniedMaterial != null) bodyRenderer.sharedMaterial = deniedMaterial;
            yield return new WaitForSeconds(0.16f);
            SetOnGoal(isOnGoal);
            feedbackRoutine = null;
        }
    }
}

using UnityEngine;

namespace FirstPersonSokoban.Interaction
{
    public sealed class GoalView : MonoBehaviour
    {
        private Renderer[] glowRenderers;
        private Material activeMaterial;
        private Material completeMaterial;
        public Vector2Int GridPosition { get; private set; }

        private void Awake() => glowRenderers = GetComponentsInChildren<Renderer>();

        public void Initialize(Vector2Int position, Material active, Material complete)
        {
            GridPosition = position;
            activeMaterial = active;
            completeMaterial = complete;
            SetComplete(false);
        }

        public void SetComplete(bool complete)
        {
            if (glowRenderers == null) return;
            Material material = complete ? completeMaterial : activeMaterial;
            foreach (Renderer glow in glowRenderers)
                if (glow != null && glow.name.StartsWith("Glow")) glow.sharedMaterial = material;
        }
    }
}

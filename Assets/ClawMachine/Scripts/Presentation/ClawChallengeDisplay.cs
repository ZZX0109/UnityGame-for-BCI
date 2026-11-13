using UnityEngine;

namespace ClawMachine
{
    /// <summary>Challenge identity through cabinet colour; no task or physics mutation.</summary>
    public sealed class ClawChallengeDisplay : MonoBehaviour
    {
        [SerializeField] private ClawGameManager game;
        [SerializeField] private Renderer[] accents;
        private MaterialPropertyBlock properties;
        private int shown = -1;
        private static readonly Color[] Palette = {
            new Color(0.46f, 0.73f, 0.64f), new Color(0.78f, 0.56f, 0.58f), new Color(0.82f, 0.59f, 0.32f)
        };
        private void OnEnable() { shown = -1; Refresh(); }
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            if (!game || accents == null) return;
            int index = Mathf.Clamp(game.SelectedChallengeIndex, 0, Palette.Length - 1);
            if (index == shown) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            foreach (Renderer panel in accents)
            {
                if (!panel) continue;
                panel.GetPropertyBlock(properties); properties.SetColor("_Color", Palette[index]); panel.SetPropertyBlock(properties);
            }
            shown = index;
        }
    }
}

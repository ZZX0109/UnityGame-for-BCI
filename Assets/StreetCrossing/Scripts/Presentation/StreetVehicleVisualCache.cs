using System.Collections.Generic;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>One complete model per variant, built lazily once for each pooled vehicle root.</summary>
    public sealed class StreetVehicleVisualCache : MonoBehaviour
    {
        private readonly Transform[] models = new Transform[3];
        private readonly Renderer[][] paint = new Renderer[3][];
        private MaterialPropertyBlock properties;
        private int selected;
        public int CreatedModels { get; private set; }
        public Transform Get(int variant) => models[Mathf.Clamp(variant, 0, 2)];
        public void Store(int variant, Transform model)
        {
            variant = Mathf.Clamp(variant, 0, 2); models[variant] = model; CreatedModels++;
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == "Painted body" || renderer.name == "Cabin") renderers.Add(renderer);
            paint[variant] = renderers.ToArray();
        }
        public void Select(int variant)
        {
            selected = Mathf.Clamp(variant, 0, 2);
            for (int i = 0; i < models.Length; i++) if (models[i] != null) models[i].gameObject.SetActive(i == selected);
        }
        public void SetPaint(Color colour)
        {
            if (paint[selected] == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.Clear(); properties.SetColor("_Color", colour);
            foreach (Renderer renderer in paint[selected]) renderer.SetPropertyBlock(properties);
        }
    }
}

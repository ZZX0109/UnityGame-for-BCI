using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Shared studio lighting rig for the generated demo scene and the rig
    /// preview captures, so product view and previews are lit identically.
    ///
    /// Design (product camera orbits from -Z looking toward +Z, see
    /// OrbitCamera): warm key from upper screen-left, rim from behind/above
    /// for edge separation, and an under-fill aimed UP from below the hands
    /// (run22: at rest both palms face -Y, so every above-horizon light left
    /// the palm side dark) acting as table bounce for the palm side. An
    /// explicit Trilight ambient (warm ground) keeps palm creases legible
    /// from every common angle without blowing the skin out (no wax look, no
    /// clipping on the brightest skin texels). Exactly three directional
    /// real-time lights; only the key carries soft shadows.
    /// </summary>
    public static class VirtualHandStudioLighting
    {
        public static void Create(List<GameObject> created = null)
        {
            var key = CreateDirectional(
                "Studio.KeyLight",
                euler: new Vector3(48f, -20f, 0f),
                color: new Color(1f, 0.96f, 0.90f),
                intensity: 1.0f,
                softShadows: true);
            created?.Add(key);

            // Under-fill: negative pitch aims forward.y > 0 (upward) so the
            // downward-facing palm side gets real light, like bounce off the
            // table. y=0 keeps it symmetric for the mirrored L/R palm normals.
            var fill = CreateDirectional(
                "Studio.FillLight",
                euler: new Vector3(-65f, 0f, 0f),
                color: new Color(0.98f, 0.94f, 0.88f),
                intensity: 0.55f,
                softShadows: false);
            created?.Add(fill);

            var rim = CreateDirectional(
                "Studio.RimLight",
                euler: new Vector3(35f, 165f, 0f),
                color: new Color(0.95f, 0.97f, 1.00f),
                intensity: 0.55f,
                softShadows: false);
            created?.Add(rim);

            // Explicit ambient instead of relying on editor defaults: a
            // Trilight gradient reads as studio bounce (cool sky above, warm
            // wood-table bounce below) and keeps shadowed palm creases from
            // going near-black while Flat ambient would look waxy.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.31f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.24f, 0.26f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.21f, 0.17f);
        }

        static GameObject CreateDirectional(string name, Vector3 euler,
            Color color, float intensity, bool softShadows)
        {
            var go = new GameObject(name);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = softShadows ? LightShadows.Soft : LightShadows.None;
            // lift the shadowed side a little; ambient alone would leave the
            // table shadow under a palm-down hand nearly black
            if (softShadows) light.shadowStrength = 0.8f;
            return go;
        }
    }
}

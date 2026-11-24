using UnityEngine;
using UnityEngine.Rendering;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Switches explicit avatar renderers between their original shared materials and a
    /// translucent cyan-blue style. Original material arrays are cached once and restored
    /// when the component is disabled or destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Neuralife/Virtual Hand/Avatar Visual Style")]
    public sealed class AvatarVisualStyle : MonoBehaviour
    {
        public const string ShaderName = "Neuralife/AvatarBlueOutline";

        private static readonly int InteriorColorId = Shader.PropertyToID("_InteriorColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int InteriorAlphaId = Shader.PropertyToID("_InteriorAlpha");
        private static readonly int RimAlphaId = Shader.PropertyToID("_RimAlpha");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int DepthPrepassId = Shader.PropertyToID("_DepthPrepass");
        private static readonly int ColorZTestId = Shader.PropertyToID("_ColorZTest");

        [Header("Targets")]
        [Tooltip("Only these renderers are changed. Assign the avatar renderers explicitly.")]
        public Renderer[] targets = new Renderer[0];

        [Tooltip("Apply the blue style when this component is enabled for the first time.")]
        public bool startInBlueMode;

        [Header("Blue material")]
        [ColorUsage(true, true)]
        public Color interiorColor = new Color(0.05f, 0.48f, 0.72f, 1f);

        [ColorUsage(true, true)]
        public Color rimColor = new Color(0.2f, 0.9f, 1f, 1f);

        [Range(0f, 1f)]
        public float interiorAlpha = 0.12f;

        [Range(0f, 1f)]
        public float rimAlpha = 0.95f;

        [Range(0.25f, 8f)]
        public float rimPower = 2f;

        [SerializeField]
        [Tooltip("Assign the Neuralife/AvatarBlueOutline shader asset to keep it referenced in player builds. If empty, Shader.Find is used as a fallback.")]
        private Shader blueShader;

        [Tooltip("Writes the nearest visible avatar surface to depth before shading it. Opaque scene depth still occludes the avatar. Each renderer and submesh runs its own shader passes; this does not globally prepass all avatar renderers or submeshes, so overlapping avatar surfaces can remain order-dependent.")]
        public bool depthPrepass = true;

        private sealed class MaterialSnapshot
        {
            public readonly Renderer renderer;
            public readonly Material[] originalMaterials;
            public Material[] blueMaterials;

            public MaterialSnapshot(Renderer targetRenderer, Material[] materials)
            {
                renderer = targetRenderer;
                originalMaterials = materials;
            }
        }

        private MaterialSnapshot[] originalSnapshots;
        private Material generatedBlueMaterial;
        private bool blueModeSelected;

        private void OnEnable()
        {
            EnsureOriginalSnapshots();
            if (startInBlueMode)
                blueModeSelected = true;

            if (blueModeSelected)
                SetBlueMode(true);
        }

        private void OnDisable()
        {
            RestoreOriginalMaterialArrays(originalSnapshots);
        }

        private void OnDestroy()
        {
            RestoreOriginalMaterialArrays(originalSnapshots);
            DestroyGeneratedBlueMaterial();
        }

        /// <summary>Switches the assigned renderers into or out of blue mode.</summary>
        public void SetBlueMode(bool enabled)
        {
            blueModeSelected = enabled;
            if (!enabled)
            {
                RestoreOriginalMaterialArrays(originalSnapshots);
                return;
            }

            EnsureOriginalSnapshots();
            Material sharedMaterial = GetOrCreateBlueMaterial();
            if (!sharedMaterial)
                return;

            ConfigureBlueMaterial(sharedMaterial);
            for (int i = 0; i < originalSnapshots.Length; i++)
            {
                MaterialSnapshot snapshot = originalSnapshots[i];
                if (snapshot == null || !snapshot.renderer)
                    continue;

                if (snapshot.blueMaterials == null)
                {
                    snapshot.blueMaterials = new Material[snapshot.originalMaterials.Length];
                }

                for (int slot = 0; slot < snapshot.blueMaterials.Length; slot++)
                    snapshot.blueMaterials[slot] = sharedMaterial;

                // Keep the renderer's existing material-slot count, including slots whose
                // original material was null. One generated material is shared across all
                // slots and target renderers owned by this component.
                snapshot.renderer.sharedMaterials = snapshot.blueMaterials;
            }
        }

        /// <summary>Reapplies exposed color and alpha settings while blue mode is active.</summary>
        public void RefreshBlueMaterial()
        {
            if (blueModeSelected)
                SetBlueMode(true);
        }

        /// <summary>Restores the original shared material arrays and selects original mode.</summary>
        public void RestoreOriginalMaterials()
        {
            SetBlueMode(false);
        }

        private Material GetOrCreateBlueMaterial()
        {
            if (generatedBlueMaterial)
                return generatedBlueMaterial;

            Shader shader = blueShader ? blueShader : Shader.Find(ShaderName);
            if (!shader)
            {
                Debug.LogError("AvatarVisualStyle could not find shader '" + ShaderName + "'. Assign it in the Inspector or run the Avatar Visual Style initialization menu item.", this);
                return null;
            }

            generatedBlueMaterial = new Material(shader)
            {
                name = "Neuralife Avatar Blue Outline (Generated)",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true
            };
            return generatedBlueMaterial;
        }

        private void ConfigureBlueMaterial(Material material)
        {
            material.SetColor(InteriorColorId, interiorColor);
            material.SetColor(RimColorId, rimColor);
            material.SetFloat(InteriorAlphaId, Mathf.Clamp01(interiorAlpha));
            material.SetFloat(RimAlphaId, Mathf.Clamp01(rimAlpha));
            material.SetFloat(RimPowerId, Mathf.Clamp(rimPower, 0.25f, 8f));
            material.SetFloat(DepthPrepassId, depthPrepass ? 1f : 0f);
            material.SetFloat(ColorZTestId, depthPrepass
                ? (float)CompareFunction.Equal
                : (float)CompareFunction.LessEqual);
        }

        private void EnsureOriginalSnapshots()
        {
            int targetCount = targets == null ? 0 : targets.Length;
            if (SnapshotsMatchTargets(targetCount))
                return;

            // If the explicit target list changed while blue mode was active, return the
            // old renderers to their captured arrays before taking snapshots of the new list.
            RestoreOriginalMaterialArrays(originalSnapshots);
            originalSnapshots = new MaterialSnapshot[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                Renderer targetRenderer = targets[i];
                if (targetRenderer)
                    originalSnapshots[i] = new MaterialSnapshot(targetRenderer, targetRenderer.sharedMaterials);
            }
        }

        private bool SnapshotsMatchTargets(int targetCount)
        {
            if (originalSnapshots == null || originalSnapshots.Length != targetCount)
                return false;

            for (int i = 0; i < targetCount; i++)
            {
                Renderer current = targets[i];
                Renderer cached = originalSnapshots[i] == null ? null : originalSnapshots[i].renderer;
                if (!current && !cached)
                    continue;
                if (!object.ReferenceEquals(current, cached))
                    return false;
            }
            return true;
        }

        private static void RestoreOriginalMaterialArrays(MaterialSnapshot[] snapshots)
        {
            if (snapshots == null)
                return;

            for (int i = 0; i < snapshots.Length; i++)
            {
                MaterialSnapshot snapshot = snapshots[i];
                if (snapshot != null && snapshot.renderer)
                    snapshot.renderer.sharedMaterials = snapshot.originalMaterials;
            }
        }

        private void DestroyGeneratedBlueMaterial()
        {
            if (!generatedBlueMaterial)
                return;

            if (Application.isPlaying)
                Object.Destroy(generatedBlueMaterial);
            else
                Object.DestroyImmediate(generatedBlueMaterial);

            generatedBlueMaterial = null;
        }
    }
}

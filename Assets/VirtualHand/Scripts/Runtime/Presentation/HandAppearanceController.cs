using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Controls the hand visual appearance, switching between realistic skin and
    /// translucent blue outline modes. It delegates to <see cref="AvatarVisualStyle"/>
    /// which owns the per-renderer material snapshots and the generated blue material.
    /// 
    /// This component is a thin lifecycle wrapper: it provides the public
    /// HandAppearanceMode enum and ensures that the shader reference is assigned
    /// for player builds.
    /// </summary>
    public enum HandAppearanceMode
    {
        Realistic = 0,
        BlueTransparent = 1
    }

    [AddComponentMenu("Neuralife/Virtual Hand/Hand Appearance Controller")]
    [DisallowMultipleComponent]
    public sealed class HandAppearanceController : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Assigned automatically from this hierarchy when left empty.")]
        public AvatarVisualStyle visualStyle;

        [Tooltip("Shader asset for the blue outline. Kept referenced for player builds.")]
        public Shader blueShader;

        [Header("State")]
        [SerializeField]
        HandAppearanceMode _mode = HandAppearanceMode.Realistic;
        public HandAppearanceMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                    return;
                _mode = value;
                ApplyMode();
            }
        }

        void Awake()
        {
            if (!visualStyle)
            {
                visualStyle = GetComponent<AvatarVisualStyle>();
                if (!visualStyle)
                    visualStyle = gameObject.AddComponent<AvatarVisualStyle>();
            }

            if (!blueShader && visualStyle)
            {
                blueShader = Shader.Find(AvatarVisualStyle.ShaderName);
#if UNITY_EDITOR
                // In the editor, try to load the asset to keep it referenced.
                if (!blueShader)
                    blueShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(
                        "Assets/VirtualHand/Shaders/AvatarBlueOutline.shader");
#endif
            }

            if (visualStyle && (visualStyle.targets == null || visualStyle.targets.Length == 0))
            {
                var list = new System.Collections.Generic.List<Renderer>();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r && r.enabled)
                        list.Add(r);
                if (list.Count > 0)
                    visualStyle.targets = list.ToArray();
            }

            ApplyMode();
        }

        void OnDestroy()
        {
            // VisualStyle cleans up its generated material in OnDestroy.
        }

        public void SetRealistic() => Mode = HandAppearanceMode.Realistic;
        public void SetBlueTransparent() => Mode = HandAppearanceMode.BlueTransparent;
        public void Toggle() => Mode = _mode == HandAppearanceMode.Realistic
            ? HandAppearanceMode.BlueTransparent
            : HandAppearanceMode.Realistic;

        void ApplyMode()
        {
            if (!visualStyle)
                return;

            switch (_mode)
            {
                case HandAppearanceMode.BlueTransparent:
                    visualStyle.SetBlueMode(true);
                    break;
                default:
                    visualStyle.SetBlueMode(false);
                    break;
            }
        }
    }
}
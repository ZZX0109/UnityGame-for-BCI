using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand
{
    /// <summary>DB8 clip entry: binds its serialized button at runtime (persistent
    /// across scene reloads) and loads exactly one clip file. Runtime assembly so
    /// the generated scene's references survive player builds.</summary>
    public class Db8ClipMenu : MonoBehaviour
    {
        public Button button;
        [HideInInspector] public string clipPath;

        void OnEnable() { if (button != null) button.onClick.AddListener(Load); }
        void OnDisable() { if (button != null) button.onClick.RemoveListener(Load); }

        void Load()
        {
            var ui = FindObjectOfType<DemoUI>();
            if (ui == null || string.IsNullOrEmpty(clipPath)) return;
            // Saved scenes may contain old absolute development paths. Resolve by
            // filename against the current StreamingAssets directory in every case.
            string path = Path.Combine(ui.db8Dir, Path.GetFileName(clipPath));
            ui.LoadReplayFile(path, "DB8姿态");
        }
    }

    /// <summary>Central UI font: prefers the generated Noto Sans CJK subset in
    /// Resources; falls back to the builtin font when it is absent.</summary>
    public static class UiFont
    {
        const string ResourceName = "Fonts/NotoSansCJK_Subset";
        static Font _cached;

        public static Font Load()
        {
            if (_cached == null)
                _cached = Resources.Load<Font>(ResourceName);
            return _cached != null ? _cached
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}

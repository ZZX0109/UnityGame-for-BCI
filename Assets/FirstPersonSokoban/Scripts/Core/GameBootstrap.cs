using FirstPersonSokoban.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstPersonSokoban.Core
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            // 默认质量配置开启 VSync，这里只是给出桌面端的帧率上限，不等于"已锁定 60FPS"。
            Application.targetFrameRate = 60;
            if (Application.isPlaying) Screen.fullScreen = GameSettings.FullScreen;
            ConfigureEnvironment();
            EnsureEventSystem();

            GridManager grid = gameObject.AddComponent<GridManager>();
            LevelLoader loader = gameObject.AddComponent<LevelLoader>();
            GameUIController ui = gameObject.AddComponent<GameUIController>();
            PuzzleGameManager game = gameObject.AddComponent<PuzzleGameManager>();
            game.Initialize(grid, loader, ui);
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.65f, 0.68f, 1f);
            RenderSettings.ambientIntensity = 0.85f;
            RenderSettings.fog = false;

            if (Object.FindObjectOfType<Light>() == null)
            {
                GameObject lightObject = new GameObject("Key Light · Soft Daylight");
                Light key = lightObject.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = new Color(0.88f, 0.95f, 1f);
                key.intensity = 1.15f;
                key.shadows = LightShadows.Soft;
                key.shadowStrength = 0.72f;
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            }
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Object.DontDestroyOnLoad(eventSystem);
        }
    }
}

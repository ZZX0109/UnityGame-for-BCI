using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Save;
using UnityEngine;

namespace StreetCrossing.UI
{
    /// <summary>Compact keyboard/mouse UI using existing IMGUI module; no new package or shared-game UI dependency.</summary>
    public sealed class StreetUIController : MonoBehaviour
    {
        private StreetGameManager game;
        private Font font;
        private GUIStyle title, body, small, button, card, subtitle, toggle;
        private Texture2D panel, white;
        private bool settings, confirmRestart;
        private Vector2 settingsScroll;
        public void Initialize(StreetGameManager owner, Font ownFont) { game = owner; font = ownFont; }
        public bool HasModal => settings || confirmRestart;
        public void CloseModal() { settings = confirmRestart = false; StreetPreferences.Save(); }
        public void ShowRestartConfirmation() { confirmRestart = true; settings = false; }
        private void Setup()
        {
            if (panel != null) return;
            panel = Solid(new Color(0.96f, 0.94f, 0.86f, 0.96f)); white = Solid(new Color(0.96f, 0.95f, 0.89f, 0.89f));
            title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(0.16f, 0.27f, 0.2f) } };
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 20, wordWrap = true, normal = { textColor = new Color(0.2f, 0.27f, 0.21f) } };
            subtitle = new GUIStyle(body) { fontSize = 23, fontStyle = FontStyle.Bold };
            small = new GUIStyle(body) { fontSize = 16 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 21, fixedHeight = 46, wordWrap = true, margin = new RectOffset(0, 0, 5, 5) };
            toggle = new GUIStyle(GUI.skin.toggle) { font = font, fontSize = 20, wordWrap = true, fixedHeight = 38 };
            toggle.normal.textColor = toggle.onNormal.textColor = toggle.hover.textColor = toggle.onHover.textColor = body.normal.textColor;
            card = new GUIStyle { normal = { background = panel }, padding = new RectOffset(28, 28, 24, 24) };
        }
        private static Texture2D Solid(Color color)
        {
            Texture2D texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
        private void OnGUI()
        {
            if (game == null) return; Setup();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Matrix4x4 previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale, height = Screen.height / scale;
            body.fontSize = StreetPreferences.LargeText ? 23 : 20; small.fontSize = StreetPreferences.LargeText ? 19 : 16;
            if (game.IsPlaying) HUD(width, height);
            else
            {
                Rect rect = new Rect((width - 490) * 0.5f, (height - 610) * 0.5f, 490, 610);
                GUILayout.BeginArea(rect, card);
                if (confirmRestart) RestartCard();
                else if (settings) SettingsCard();
                else if (game.State == StreetGameState.Menu) MainCard();
                else if (game.State == StreetGameState.Paused) PauseCard();
                else ResultCard();
                GUILayout.EndArea();
            }
            GUI.matrix = previous;
        }
        private void HUD(float width, float height)
        {
            GUI.DrawTexture(new Rect(24, 22, 470, 90), white);
            GUI.Label(new Rect(40, 30, 438, 32), "S01  /  前往对面的街角便利店", subtitle);
            string progress = game.Evaluation.IsCrossing ? "沿斑马线完成过街" : game.PlayerPosition.z > game.scenario.roadHalfWidth ? "继续走到便利店门前" : "走到路沿 · 观察行人灯与左右车流";
            GUI.Label(new Rect(40, 68, 438, 34), progress, small);
            GUI.DrawTexture(new Rect(24, height - 90, Mathf.Min(820, width - 48), 66), white);
            string hint = game.Notice.Length > 0 ? game.Notice : game.Evaluation.Feedback;
            GUI.Label(new Rect(40, height - 84, Mathf.Min(788, width - 80), 56), hint, small);
            GUI.Label(new Rect(width - 356, height - 38, 340, 28), "WASD 行走  /  鼠标观察  /  Esc 暂停", small);
            if (game.Evaluation.Violations > 0) GUI.Label(new Rect(width - 300, 28, 280, 54), "本次已记录违规\n可安全返回或重试", body);
        }
        private void MainCard()
        {
            GUILayout.Label("街角的一次过街", title);
            GUILayout.Label("FIRST PERSON STREET CROSSING", small);
            GUILayout.Space(14);
            GUILayout.Label("从住宅人行道出发，沿斑马线前往对面的街角便利店。", body);
            GUILayout.Space(10);
            GUILayout.Label("等待行人绿灯，转头观察左右车流，确认车辆已停稳。安全到达比速度更重要。", body);
            GUILayout.Space(12);
            GUILayout.Label("WASD 连续行走 · 鼠标环顾\nShift 快步走 · E 查看近处标识\nEsc 暂停 · R 确认重开", small);
            GUILayout.Space(16);
            if (GUILayout.Button("开始 S01 练习", button)) game.StartPractice();
            if (GUILayout.Button("设置", button)) settings = true;
            GUILayout.Label("最佳安全评价：" + StreetPreferences.BestStars(game.scenario.scenarioId) + " / 3", small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("游戏原型参数不代表现实交通法规。\n本版本不含临床验证或 BCI 硬件。", small);
        }
        private void PauseCard()
        {
            GUILayout.Label("练习已暂停", title); GUILayout.Space(12);
            GUILayout.Label("行人、车辆和信号使用同一暂停时钟。\n回来后可继续观察并安全过街。", body);
            GUILayout.Space(22);
            if (GUILayout.Button("继续练习", button)) game.Resume();
            if (GUILayout.Button("重新开始", button)) confirmRestart = true;
            if (GUILayout.Button("设置", button)) settings = true;
            if (GUILayout.Button("返回主菜单", button)) game.ReturnToMenu();
            GUILayout.FlexibleSpace();
            GUILayout.Label("失去窗口焦点会自动暂停。", small);
        }
        private void RestartCard()
        {
            GUILayout.Label("重新开始本次练习？", title); GUILayout.Space(18);
            GUILayout.Label("玩家、信号、车辆、观察记录和同一 seed 的车流将一起重置。本次过程保留在事件日志中。", body);
            GUILayout.Space(22);
            if (GUILayout.Button("确认重新开始", button)) { confirmRestart = false; game.StartPractice(); }
            if (GUILayout.Button("取消", button)) confirmRestart = false;
        }
        private void ResultCard()
        {
            CrossingEvaluator result = game.Evaluation;
            GUILayout.Label(result.SafetySuccess ? "安全抵达" : "本次练习结束", title);
            GUILayout.Space(8); GUILayout.Label(result.Feedback, body); GUILayout.Space(10);
            GUILayout.Label("安全评价  " + game.Stars + " / 3", subtitle);
            GUILayout.Label("进入时信号：" + EntryLabel(result) + "\n左右观察：" + (result.LeftObserved ? "左已记录" : "左未记录") + " / " + (result.RightObserved ? "右已记录" : "右未记录")
                + "\n违规 " + result.Violations + "  ·  危险接近 " + result.NearMisses + "  ·  碰撞 " + result.Collisions
                + "\n等待 " + result.WaitingSeconds.ToString("F1") + " 秒  ·  总时长 " + game.WorldTime.ToString("F1") + " 秒", body);
            GUILayout.Space(10);
            GUILayout.Label("评价关注安全、左右观察和危险接近；没有抢时间奖励。镜头朝向记录不代表眼动注意或医学能力评估。", small);
            GUILayout.Space(12);
            if (GUILayout.Button("再次练习", button)) game.StartPractice();
            if (GUILayout.Button("返回主菜单", button)) game.ReturnToMenu();
            GUILayout.FlexibleSpace(); GUILayout.Label(game.LogError == null ? "事件日志已保存至本机 StreetCrossing/Logs。" : "事件日志未能写入：" + game.LogError, small);
        }
        private static string EntryLabel(CrossingEvaluator result) => result.Attempts == 0 ? "尚未进入" : result.SignalAtEntry.ToString();
        private void SettingsCard()
        {
            GUILayout.Label("舒适与显示设置", title); GUILayout.Space(10);
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, true, GUILayout.Height(435));
            GUILayout.Label("鼠标灵敏度  " + StreetPreferences.Sensitivity.ToString("F1"), body);
            StreetPreferences.Sensitivity = GUILayout.HorizontalSlider(StreetPreferences.Sensitivity, 0.3f, 5);
            GUILayout.Label("视野 FOV  " + StreetPreferences.FieldOfView.ToString("F0") + "°", body);
            StreetPreferences.FieldOfView = GUILayout.HorizontalSlider(StreetPreferences.FieldOfView, 75, 85);
            GUILayout.Label("提示音音量", body); StreetPreferences.Volume = GUILayout.HorizontalSlider(StreetPreferences.Volume, 0, 1);
            StreetPreferences.ComfortMode = GUILayout.Toggle(StreetPreferences.ComfortMode, " 慢速舒适模式（1.05 m/s）", toggle);
            StreetPreferences.FastWalkEnabled = GUILayout.Toggle(StreetPreferences.FastWalkEnabled, " 允许 Shift 快步走（最高 1.85 m/s）", toggle);
            StreetPreferences.LargeText = GUILayout.Toggle(StreetPreferences.LargeText, " 放大辅助文字", toggle);
            bool fullscreen = GUILayout.Toggle(Screen.fullScreen, " 全屏显示", toggle); if (fullscreen != Screen.fullScreen) Screen.fullScreen = fullscreen;
            GUILayout.Space(10); GUILayout.Label("无镜头摇晃、无跳跃、无动态模糊。\n世界信号同时使用文字与灯位，辅助颜色区分。\n音效不是唯一过街信息。", small);
            GUILayout.EndScrollView(); GUILayout.Space(12);
            if (GUILayout.Button("保存并返回", button)) { StreetPreferences.Save(); settings = false; }
        }
        private void OnDestroy() { if (panel != null) Destroy(panel); if (white != null) Destroy(white); }
    }
}

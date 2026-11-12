using UnityEngine;

namespace ClawMachine
{
    // IMGUI keeps this isolated prototype independent of the Sokoban UI and runtime font loader.
    public sealed class ClawGameUI : MonoBehaviour
    {
        [SerializeField] private ClawGameManager game;
        [SerializeField] private Font uiFont;
        private GUIStyle title, subtitle, body, small, button, phase, panel;
        private Texture2D background, buttonTexture, hoverTexture;
        private readonly string[] phases = { "定位", "下降", "闭爪", "提升", "移送", "释放", "稳定", "结果" };
        private void EnsureStyles()
        {
            if (body != null) return;
            background = Texture(new Color(0.97f, 0.95f, 0.90f)); buttonTexture = Texture(new Color(0.18f, 0.37f, 0.31f)); hoverTexture = Texture(new Color(0.27f, 0.48f, 0.40f));
            title = Style(36, new Color(0.18f, 0.26f, 0.22f), true);
            subtitle = Style(15, new Color(0.32f, 0.43f, 0.36f), false);
            body = Style(19, new Color(0.20f, 0.28f, 0.25f), false);
            small = Style(15, new Color(0.36f, 0.41f, 0.37f), false);
            phase = Style(23, new Color(0.16f, 0.36f, 0.29f), true);
            button = Style(18, Color.white, false); button.alignment = TextAnchor.MiddleCenter;
            button.normal.background = buttonTexture; button.hover.background = hoverTexture; button.active.background = hoverTexture;
            panel = new GUIStyle { normal = { background = background }, padding = new RectOffset(20, 20, 20, 20) };
        }
        private GUIStyle Style(int size, Color color, bool bold)
        {
            return new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = true, richText = false, normal = { textColor = color } };
        }
        private static Texture2D Texture(Color color) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, color); t.Apply(); return t; }
        private void Label(float x, float y, float w, float h, string text, GUIStyle style) { GUI.Label(new Rect(x, y, w, h), text, style); }
        private bool Button(float y, string text, bool enabled = true)
        {
            bool old = GUI.enabled; GUI.enabled = enabled;
            bool hit = GUI.Button(new Rect(980, y, 260, 44), text, button); GUI.enabled = old; return hit;
        }
        private void OnGUI()
        {
            if (!game || !game.Task) return;
            EnsureStyles();
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            body.fontSize = Mathf.RoundToInt(19 * game.TextScale); small.fontSize = Mathf.RoundToInt(15 * game.TextScale);
            GUI.Box(new Rect(956, 0, 324, 720), GUIContent.none, panel);
            Label(980, 28, 260, 52, "拾光夹物所", title);
            Label(982, 83, 258, 25, "CLAW ATELIER / C01", subtitle);
            Label(28, 24, 860, 38, "三轴观察 · 从容练习", phase);
            Label(28, 64, 860, 28, game.Cameras.ViewName + "  /  " + game.Cameras.DistanceName + "距离", small);
            if (game.Task.Session.State == GraspState.Error)
            {
                Label(980, 145, 260, 110, "场景未就绪，请检查组件连接。", body);
                Label(980, 265, 260, 150, game.Task.Diagnostic, small);
                if (Button(540, "返回菜单")) game.ReturnToMenu();
            }
            else if (game.ResetConfirmation) ResetPanel();
            else if (game.SettingsOpen) SettingsPanel();
            else if (game.IsPaused) PausePanel();
            else if (game.InMenu) MenuPanel();
            else PlayPanel();
            Label(28, 670, 900, 34, "A / D  左右 X      W / S  前后 Z      1 / 2 / 3  观察视角      滚轮  距离", small);
            Label(980, 666, 260, 40, "本地模拟 · 无硬件连接\n抓持代理 / 重力投放", small);
            GUI.matrix = saved;
        }
        private void MenuPanel()
        {
            Label(980, 145, 260, 50, "一件薄荷色礼物，\n五次耐心尝试。", phase);
            Label(980, 223, 260, 115, "先从正面判断左右，再从侧面判断深度。把三爪中心对准礼物，按 Enter 完成抓取。", body);
            Label(980, 355, 260, 70, "目标：薄荷礼盒\n只有礼物落入左前方出口才获奖。", small);
            if (Button(460, "开始 C01 定位教学")) game.StartGame();
            if (Button(516, "观察距离 · " + game.Cameras.DistanceName)) game.Cameras.CycleDistance();
            if (Button(572, "设置")) game.OpenSettings();
        }
        private void PlayPanel()
        {
            ClawSession session = game.Task.Session;
            Label(980, 140, 260, 35, "C01 · 单物体定位", phase);
            Label(980, 184, 260, 64, "剩余尝试  " + session.Remaining + " / " + game.Task.Challenge.attemptLimit + "\n目标  薄荷礼盒", body);
            Label(980, 258, 260, 40, StateName(session.State), phase);
            Label(980, 310, 260, 120, Hint(session), body);
            if (session.State == GraspState.Result)
            {
                if (Button(460, session.Awarded || session.Remaining == 0 ? "查看本局结果" : "下一次尝试 · Enter")) game.Retry();
            }
            else if (session.State == GraspState.End)
            {
                if (Button(460, "同一布局重新练习")) game.StartGame();
            }
            else if (Button(460, session.State == GraspState.Aiming ? "开始抓取 · Enter" : "自动抓取中…", session.State == GraspState.Aiming)) game.Grab();
            if (Button(516, "暂停 · Esc")) game.Pause(true);
            if (Button(572, "重新挑战 · R")) game.RequestReset();
            int index = Mathf.Clamp((int)session.State - (int)GraspState.Aiming, 0, 7);
            for (int i = 0; i < phases.Length; i++) Label(28 + i * 112, 621, 110, 30, (i == index ? "● " : "○ ") + phases[i], i == index ? body : small);
        }
        private void PausePanel()
        {
            Label(980, 150, 260, 45, "已暂停", phase);
            Label(980, 213, 260, 100, "运动、重力和本次抓取计时均已冻结。可以继续观察三个方向。", body);
            if (Button(380, "继续 · Esc")) game.Pause(false);
            if (Button(436, "设置")) game.OpenSettings();
            if (Button(492, "重新挑战")) game.RequestReset();
            if (Button(548, "取消抓取并返回菜单")) game.ReturnToMenu();
        }
        private void ResetPanel()
        {
            Label(980, 150, 260, 45, "重新挑战？", phase);
            Label(980, 213, 260, 140, "当前抓取和本局次数将被清除。礼物恢复原位，使用相同 seed 布局。", body);
            if (Button(430, "确认重新挑战")) game.ConfirmReset();
            if (Button(486, "保留当前进度")) game.DeclineReset();
        }
        private void SettingsPanel()
        {
            Label(980, 150, 260, 40, "设置", phase);
            Label(980, 215, 260, 30, "机械提示音  " + Mathf.RoundToInt(game.Volume * 100) + "%", body);
            game.SetVolume(GUI.HorizontalSlider(new Rect(982, 263, 254, 25), game.Volume, 0, 1));
            Label(980, 310, 260, 30, "文字大小  " + Mathf.RoundToInt(game.TextScale * 100) + "%", body);
            game.SetTextScale(GUI.HorizontalSlider(new Rect(982, 358, 254, 25), game.TextScale, 0.9f, 1.2f));
            if (Button(420, "镜头平滑  " + (game.Cameras.Smooth ? "开" : "关"))) game.Cameras.Smooth = !game.Cameras.Smooth;
            if (Button(476, "观察距离 · " + game.Cameras.DistanceName)) game.Cameras.CycleDistance();
            if (Button(548, "返回")) game.CloseSettings();
        }
        private static string StateName(GraspState state)
        {
            switch (state) {
                case GraspState.Aiming: return "定位 / AIM"; case GraspState.Lowering: return "下降 / LOWER";
                case GraspState.Closing: return "闭爪 / GRIP"; case GraspState.Lifting: return "提升 / LIFT";
                case GraspState.Transporting: return "移送 / RETURN"; case GraspState.Releasing: return "释放 / RELEASE";
                case GraspState.Settling: return "等待稳定 / SETTLE"; case GraspState.Result: return "本次结果";
                case GraspState.End: return "本局结束"; default: return "准备中";
            }
        }
        private static string Hint(ClawSession session)
        {
            if (session.State == GraspState.Aiming) return "对准礼盒中心。切换视角后，W 始终朝机柜内移动。";
            if (session.State != GraspState.Result && session.State != GraspState.End) return "现在只需观察。\n抓到后还要安全投放，才算成功。";
            if (session.Awarded) return "礼物已稳稳落入出口！\n谢谢你的耐心观察。";
            switch (session.Failure) {
                case GraspFailure.Misaligned: return "抓点偏离中心。\n下次用侧面再检查深度。";
                case GraspFailure.NoContact: return "接触不足，未夹住礼物。\n请对准礼盒中部。";
                case GraspFailure.UnstableGrip: return "抓持不稳定。\n请检查物体重心与抓点。";
                case GraspFailure.Slipped: return "礼物在移送中滑落了。\n请更靠近中心抓取。";
                case GraspFailure.Timeout: return "机构未按时到位。\n请重新挑战。";
                default: return "礼物没有稳定进入出口。\n再观察一次，慢慢来。";
            }
        }
        private void OnDestroy() { if (background) Destroy(background); if (buttonTexture) Destroy(buttonTexture); if (hoverTexture) Destroy(hoverTexture); }
    }
}

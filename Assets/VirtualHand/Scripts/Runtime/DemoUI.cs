using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Minimal Chinese UGUI controller: replay load/play/pause/restart, clear mode
    /// label, manual preview with keyboard, intent/source readout. Keyboard and
    /// orbit camera use keys that don't collide with UI focus.
    /// </summary>
    public class DemoUI : MonoBehaviour
    {
        public ReplayController replay;
        public TaskController tasks;
        public Text statusText;      // top status line (mode/intent/source)
        public Text warningText;     // visible rejection warnings
        public Slider progress;

        string _mode = "手动预览";
        bool _paused;

        void Start()
        {
            if (replay == null) replay = GetComponent<ReplayController>();
        }

        void Update()
        {
            HandleKeys();
            if (replay != null && replay.GetComponent<ReplaySource>() is var src && src != null)
                _paused = !src.IsPlaying;
            UpdateText();
        }

        void HandleKeys()
        {
            // Space = play/pause toggle, R = restart, 1..5 = authored poses (manual mode)
            if (Input.GetKeyDown(KeyCode.Space))
            {
                var src = replay?.GetComponent<ReplaySource>();
                if (src != null)
                {
                    if (src.IsPlaying) src.Pause(); else src.Play();
                }
            }
            if (Input.GetKeyDown(KeyCode.R)) { replay?.Restart(); SetModeLabel(_mode); }
            if (Input.GetKeyDown(KeyCode.Alpha1)) replay?.SetManualPose(PoseLibrary.Rest(), PoseLibrary.Rest());
            if (Input.GetKeyDown(KeyCode.Alpha2)) replay?.SetManualPose(PoseLibrary.Fist(), PoseLibrary.Fist());
            if (Input.GetKeyDown(KeyCode.Alpha3)) replay?.SetManualPose(PoseLibrary.Pinch(), PoseLibrary.Pinch());
            if (Input.GetKeyDown(KeyCode.Alpha4)) replay?.SetManualPose(PoseLibrary.Point(), PoseLibrary.Point());
            if (Input.GetKeyDown(KeyCode.Alpha5)) replay?.SetManualPose(PoseLibrary.Rest(), PoseLibrary.Fist());
        }

        public void SetModeLabel(string mode) => _mode = mode;

        public void LoadReplayFile(string path)
        {
            try
            {
                var data = ReplaySource.LoadFile(path, ReplayParseMode.Tolerant);
                replay.GetComponent<ReplaySource>().Open(data, loop: false);
                _mode = Path.GetFileNameWithoutExtension(path);
                if (warningText != null) warningText.text =
                    data.rejectedLines > 0 ? $"已拒绝 {data.rejectedLines} 行非法帧" : "";
            }
            catch (Exception e)
            {
                if (warningText != null) warningText.text = "加载失败: " + e.Message;
            }
        }

        void UpdateText()
        {
            if (statusText == null || replay == null) return;
            string intentZh = replay.CurrentIntent switch
            {
                "rest" => "静息", "left" => "左手", "right" => "右手", "both" => "双手",
                _ => replay.CurrentIntent
            };
            string srcZh = replay.CurrentSource switch
            {
                "prediction" => "EEG预测", "labels" => "真实标签", "glove" => "数据手套",
                "manual" => "手动", _ => replay.CurrentSource
            };
            string task = tasks != null && tasks.Type != TaskController.TaskType.None
                ? $" | 任务: {tasks.Type} {tasks.StageLabel}" : "";
            statusText.text = $"模式: {_mode} | 意图: {intentZh} ({replay.CurrentConfidence:0.00}) | " +
                              $"来源: {srcZh} | {replay.SubjectRun}{task}";
        }
    }
}

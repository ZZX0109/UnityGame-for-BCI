using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Minimal Chinese UGUI controller: replay load/play/pause/restart, clear source
    /// labels, manual preview, paired EEG prediction/label entries, and visible
    /// time/mirror state. Prediction mode NEVER loads a ground-truth file as control.
    /// </summary>
    public class DemoUI : MonoBehaviour
    {
        public ReplayController replay;
        public TaskController tasks;
        public Text statusText;
        public Text warningText;
        public Slider progress;

        [Header("Default replay locations (relative to game data)")]
        public string db8Dir = Path.Combine("Assets", "StreamingAssets", "Replays", "db8");
        public string eegDir = Path.Combine("Assets", "StreamingAssets", "Replays", "eeg");
        public string currentSubject = "S001";

        string _mode = "手动预览";
        string _manualPose;
        ReplaySource _source;

        void Start()
        {
            if (replay == null) replay = GetComponent<ReplayController>();
            if (tasks == null) tasks = GetComponent<TaskController>();
            _source = replay != null ? replay.source : GetComponent<ReplaySource>();
            if (!Directory.Exists(db8Dir))
            {
                string sa = Path.Combine(Application.dataPath, "StreamingAssets", "Replays");
                if (Directory.Exists(Path.Combine(sa, "db8")))
                {
                    db8Dir = Path.Combine(sa, "db8");
                    eegDir = Path.Combine(sa, "eeg");
                }
            }
            LoadReplayFile(Path.Combine(db8Dir, "db8_S1_cylindrical_grip.jsonl"), "DB8姿态");
        }

        void Update()
        {
            HandleKeys();
            if (_source != null && _source.Duration > 0 && progress != null)
                progress.value = (float)(_source.Position / _source.Duration);
            UpdateText();
        }

        void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlayPause();
            if (Input.GetKeyDown(KeyCode.R)) RestartReplay();
            if (Input.GetKeyDown(KeyCode.Alpha1)) Manual("双手静息", PoseLibrary.Rest(), PoseLibrary.Rest());
            if (Input.GetKeyDown(KeyCode.Alpha2)) Manual("双手抓握", PoseLibrary.Fist(), PoseLibrary.Fist());
            if (Input.GetKeyDown(KeyCode.Alpha3)) PreviewPinch();
            if (Input.GetKeyDown(KeyCode.Alpha4)) Manual("双手食指伸出", PoseLibrary.Point(), PoseLibrary.Point());
            if (Input.GetKeyDown(KeyCode.Alpha5)) Manual("左手静息  右手抓握", PoseLibrary.Rest(), PoseLibrary.Fist());
        }

        public void PreviewPinch()
        {
            if (replay == null) return;
            Manual("双手捏握", PoseLibrary.PinchPose(replay.leftHand), PoseLibrary.PinchPose(replay.rightHand));
        }

        void Manual(string pose, float[] left, float[] right)
        {
            replay?.SetManualPose(left, right);
            _manualPose = pose;
            Warn("");
        }

        public void TogglePlayPause()
        {
            if (_source == null) return;
            if (_source.IsPlaying) _source.Pause(); else _source.Play();
        }

        public void RestartReplay()
        {
            replay?.Restart();
            SetModeLabel(_mode);
        }

        public void LoadDb8Clip() =>
            LoadReplayFile(FirstFile(db8Dir, "db8_S1_*.jsonl"), "DB8姿态");

        public void LoadEegPredictions() => LoadEegPredictions(currentSubject);
        public void LoadLabels() => LoadLabels(currentSubject);

        /// <summary>Prediction file only; label file is never used as a fallback.</summary>
        public void LoadEegPredictions(string subject)
        {
            currentSubject = NormalizeSubject(subject);
            LoadReplayFile(Path.Combine(eegDir, $"eeg_{currentSubject}_predictions.jsonl"),
                           $"EEG预测 {currentSubject}");
        }

        /// <summary>Independent label reference for the same subject/trial window.</summary>
        public void LoadLabels(string subject)
        {
            currentSubject = NormalizeSubject(subject);
            LoadReplayFile(Path.Combine(eegDir, $"eeg_{currentSubject}_truth.jsonl"),
                           $"明确标签 {currentSubject}");
        }

        public void SelectSubject(string subject)
        {
            currentSubject = NormalizeSubject(subject);
            Warn("已选择被试 " + currentSubject + "；请分别点击预测或标签按钮");
        }

        static string NormalizeSubject(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "S001";
            string s = subject.Trim().ToUpperInvariant();
            if (s.StartsWith("S")) s = s.Substring(1);
            if (!int.TryParse(s, out int n) || n < 1 || n > 999) return "S001";
            return "S" + n.ToString("000");
        }

        static string FirstFile(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) throw new IOException("回放目录不存在: " + dir);
            string[] files = Directory.GetFiles(dir, pattern);
            if (files.Length == 0) throw new IOException("目录中无匹配文件: " + pattern);
            Array.Sort(files, StringComparer.Ordinal);
            return files[0];
        }

        public void LoadReplayFile(string path) => LoadReplayFile(path, null);

        public void LoadReplayFile(string path, string modeLabel)
        {
            try
            {
                if (replay == null) { Warn("回放控制器未就绪"); return; }
                if (_source == null) _source = replay.GetComponent<ReplaySource>();
                var data = ReplaySource.LoadFile(path, ReplayParseMode.Tolerant);
                replay.Restart();
                _source.Open(data, loop: false);
                SetModeLabel(modeLabel ?? Path.GetFileNameWithoutExtension(path));
                Warn(data.rejectedLines > 0
                    ? $"已拒绝 {data.rejectedLines} 行非法帧（共载入 {data.frames.Count} 帧）"
                    : $"已载入 {data.frames.Count} 帧");
            }
            catch (Exception e)
            {
                Warn("加载失败: " + e.Message);
            }
        }

        void Warn(string msg)
        {
            if (warningText != null) warningText.text = msg;
        }

        public void SetModeLabel(string mode) => _mode = mode;

        void UpdateText()
        {
            if (statusText == null || replay == null) return;
            string taskLine = "";
            if (tasks != null && tasks.Type != TaskController.TaskType.None)
            {
                string taskName = tasks.Type == TaskController.TaskType.Carry ? "双手搬运" :
                    (tasks.LeftIsSender ? "左手交右手" : "右手交左手");
                string stage = tasks.CurrentStage switch
                {
                    TaskController.Stage.Approach => "等待抓取", TaskController.Stage.Lift => "抬起",
                    TaskController.Stage.Transfer => "等待接手", TaskController.Stage.Place => "等待松手",
                    TaskController.Stage.Done => "已放下", _ => "移动中"
                };
                taskLine = tasks.ControlsTaskHands ? $"任务辅助握持  {taskName}  {stage}\n"
                    : $"{taskName}  {stage}  已恢复回放\n";
            }
            if (replay.IsManualPreview)
            {
                statusText.text = taskLine + "手动预览\n" + _manualPose;
                return;
            }
            if (replay.CurrentSource == "manual" && replay.SubjectRun.Length == 0)
            {
                statusText.text = taskLine + _mode + (_source != null && _source.IsPlaying ? "\n等待首个数据帧" : "\n点击播放查看动作");
                return;
            }
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
            string validity = replay.HasValidInput ? "有效" : "空隙/失效";
            string mirror = tasks != null && tasks.ControlsTaskHands ? "显示辅助姿态" :
                (replay.IsMirroredDisplay ? "右手=左手镜像" : "右手=独立显示");
            statusText.text = taskLine + $"{srcZh}  {replay.SubjectRun}\n" +
                              $"意图 {intentZh}  {replay.CurrentConfidence:0.00}  {validity}\n" +
                              $"{mirror}  {replay.Position:0.0}/{replay.Duration:0.0}s";
        }
    }
}

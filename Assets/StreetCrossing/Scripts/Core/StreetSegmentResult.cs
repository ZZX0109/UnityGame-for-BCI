using StreetCrossing.Evaluation;
using StreetCrossing.Traffic;

namespace StreetCrossing.Core
{
    /// <summary>Immutable per-leg evidence; later legs cannot overwrite an earlier violation.</summary>
    public sealed class StreetSegmentResult
    {
        public int SegmentIndex { get; }
        public bool SafetySuccess { get; }
        public int Stars { get; }
        public TrafficPhase SignalAtEntry { get; }
        public bool LeftObserved { get; }
        public bool RightObserved { get; }
        public int Violations { get; }
        public int NearMisses { get; }
        public int StationaryContacts { get; }
        public int MovingCollisions { get; }
        public float WaitingSeconds { get; }
        public string Feedback { get; }
        public string Summary => "第" + (SegmentIndex + 1) + "段：" + (SafetySuccess ? "安全完成" : "需重新练习") + "；" + Feedback
            + "\n进入信号 " + EntrySignalText + "；左侧观察 " + (LeftObserved ? "已完成" : "未完成") + "；右侧观察 " + (RightObserved ? "已完成" : "未完成")
            + "\n违规 " + Violations + "；危险接近 " + NearMisses + "；静止接触 " + StationaryContacts + "；行驶碰撞 " + MovingCollisions + "；等待 " + WaitingSeconds.ToString("F1") + " 秒。";
        private string EntrySignalText => SignalAtEntry == TrafficPhase.Walk ? "行人通行" : SignalAtEntry == TrafficPhase.WalkFlashing ? "行人清空" : SignalAtEntry == TrafficPhase.VehicleGreen ? "机动车通行" : SignalAtEntry == TrafficPhase.VehicleAmber ? "机动车黄灯" : "全红清空";
        public StreetSegmentResult(int index, CrossingEvaluator evaluation)
        {
            SegmentIndex = index; SafetySuccess = evaluation.SafetySuccess; SignalAtEntry = evaluation.SignalAtEntry;
            LeftObserved = evaluation.LeftObserved; RightObserved = evaluation.RightObserved; Violations = evaluation.Violations;
            NearMisses = evaluation.NearMisses; StationaryContacts = evaluation.StationaryContacts; MovingCollisions = evaluation.MovingCollisions;
            WaitingSeconds = evaluation.WaitingSeconds; Feedback = evaluation.Feedback;
            Stars = SafetySuccess ? 1 + (NearMisses == 0 ? 1 : 0) + (StationaryContacts == 0 ? 1 : 0) : 0;
        }
    }
}

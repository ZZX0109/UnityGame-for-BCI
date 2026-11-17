using System;
using System.Collections.Generic;

namespace StreetCrossing.Core
{
    public static class StreetMissionCatalog
    {
        private static readonly IReadOnlyList<StreetMissionDefinition> missions = Array.AsReadOnly(new[]
        {
            Mission("S01", 1, "去便利店", "从住宅门口出发，按步骤在路沿等待、看灯并左右观察，安全到达对面便利店。",
                StreetAssistance.Guided, StreetDestinationKind.Convenience),
            Mission("S01", 2, "选择出发时机", "从不同信号阶段来到路沿，自己判断何时等待、何时出发，再到便利店。",
                StreetAssistance.RiskOnly, StreetDestinationKind.Convenience, prewarm: 20f),
            Mission("S01", 3, "回到住宅", "从便利店返回住宅入口。需要时可请求帮助，独立完成反向过街。",
                StreetAssistance.OnDemand, StreetDestinationKind.Residence, reverse: true),
            Mission("S02", 1, "去书店", "去对面的书店，先确认双向四条车道，不能只看最近的一辆车。",
                StreetAssistance.Guided, StreetDestinationKind.Bookshop),
            Mission("S02", 2, "看清被挡住的车道", "混合车型与排队可能挡住远处车道。先在人行道安全位置重新观察，再去书店。",
                StreetAssistance.RiskOnly, StreetDestinationKind.Bookshop, prewarm: 20f),
            Mission("S02", 3, "寻找药店", "先辨认药店标识，独立确认过街条件，过街后沿人行道走到药店。",
                StreetAssistance.OnDemand, StreetDestinationKind.Pharmacy),
            Mission("S03", 1, "去咖啡店", "去路口对面的咖啡店，认识转弯路线与信号切换时的清空过程。",
                StreetAssistance.Guided, StreetDestinationKind.Cafe),
            Mission("S03", 2, "先换站位再观察", "道路被遮挡时，先在人行道内自主调整到安全观察位置，再去咖啡店。",
                StreetAssistance.RiskOnly, StreetDestinationKind.Cafe, prewarm: 8f),
            Mission("S03", 3, "咖啡店往返", "去咖啡店后返回起点。两段分别确认信号、观察与道路，任何一段违规都不能安全完成。",
                StreetAssistance.OnDemand, StreetDestinationKind.Cafe, roundTrip: true)
        });

        private static readonly Dictionary<string, IReadOnlyList<StreetMissionDefinition>> districts = BuildDistricts();

        public static IReadOnlyList<StreetMissionDefinition> All => missions;

        public static IReadOnlyList<StreetMissionDefinition> ForDistrict(string districtId)
        {
            if (districtId == null) throw new ArgumentNullException(nameof(districtId));
            if (!districts.TryGetValue(districtId, out IReadOnlyList<StreetMissionDefinition> result))
                throw new ArgumentException("Unknown mission district.", nameof(districtId));
            return result;
        }

        private static StreetMissionDefinition Mission(string district, int tier, string title, string description,
            StreetAssistance assistance, StreetDestinationKind destination, bool reverse = false,
            bool roundTrip = false, float prewarm = 0f)
            => new StreetMissionDefinition(district + "-T" + tier, district, title, description, tier, 1,
                assistance, reverse, roundTrip, destination, prewarm);

        private static Dictionary<string, IReadOnlyList<StreetMissionDefinition>> BuildDistricts()
        {
            var result = new Dictionary<string, IReadOnlyList<StreetMissionDefinition>>(StringComparer.Ordinal);
            foreach (string district in new[] { "S01", "S02", "S03" })
            {
                var group = new List<StreetMissionDefinition>();
                foreach (StreetMissionDefinition mission in missions)
                    if (mission.districtId == district) group.Add(mission);
                result.Add(district, group.AsReadOnly());
            }
            return result;
        }
    }
}

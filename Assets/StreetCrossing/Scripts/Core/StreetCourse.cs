using System;
using System.Collections.Generic;

namespace StreetCrossing.Core
{
    public sealed class StreetCourse
    {
        public readonly string Title, Instruction, District;
        public readonly int MissionIndex;
        private StreetCourse(string title, string instruction, string district, int mission)
        {
            Title = title; Instruction = instruction; District = district; MissionIndex = mission;
        }
        public static readonly IReadOnlyList<StreetCourse> Stages = Array.AsReadOnly(new[] {
            new StreetCourse("熟悉环境", "沿人行道走到斑马线入口，停在路沿，看向对面的行人灯。留在人行道上。", "S01", 0),
            new StreetCourse("单方向观察", "停在路沿，向左观察近侧来车，直到看清左侧道路。本练习到观察为止。", "S01", 0),
            new StreetCourse("双方向判断", "在路沿看清左右来车和行人灯，确认车辆已停，再沿斑马线去便利店。", "S01", 0),
            new StreetCourse("复杂车流", "先看清双向四条车道。近车停下后仍需确认远侧车道，再去书店。", "S02", 1),
            new StreetCourse("干扰和遮挡", "公交站牌挡住左侧近车道，沿人行道调整站位，看清转弯车流后再过街。", "S03", 1),
            new StreetCourse("生活情境任务", "去街角咖啡店再返回住宅。往返要分别确认行人灯和两侧车流。", "S03", 2)
        });
    }
}

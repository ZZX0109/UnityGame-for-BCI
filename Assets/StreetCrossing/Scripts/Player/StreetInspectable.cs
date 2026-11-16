using UnityEngine;

namespace StreetCrossing.Player
{
    /// <summary>Only explicitly authored street signs may answer the inspect action.</summary>
    public sealed class StreetInspectable : MonoBehaviour
    {
        public string title = "道路标识";
        [TextArea] public string description = "查看行人灯和左右车流，确认安全后再进入横道。";
        public string Content => string.IsNullOrWhiteSpace(description) ? title : title + "：" + description;
    }
}

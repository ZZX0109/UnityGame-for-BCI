using UnityEngine;

namespace StreetCrossing.Player
{
    /// <summary>Local input boundary for future adapters. This release contains no EEG, UDP or hardware integration.</summary>
    public interface IStreetInput
    {
        Vector2 Movement { get; }
        Vector2 Look { get; }
        bool FastWalk { get; }
        bool InteractPressed { get; }
        bool PausePressed { get; }
        bool RestartPressed { get; }
    }
    public sealed class LegacyStreetInput : IStreetInput
    {
        public Vector2 Movement => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        public Vector2 Look => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        public bool FastWalk => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public bool InteractPressed => Input.GetKeyDown(KeyCode.E);
        public bool PausePressed => Input.GetKeyDown(KeyCode.Escape);
        public bool RestartPressed => Input.GetKeyDown(KeyCode.R);
    }
}

using UnityEngine;

namespace ClawMachine
{
    public struct ClawInputFrame
    {
        public Vector2 Move;
        public bool Grab, Pause, Reset;
        public int View;
        public float Zoom;
    }
    public interface IClawInputSource { ClawInputFrame Read(); }
    // Keyboard is an adapter, not part of task/driver logic. No EEG/network/hardware adapter is implemented.
    public sealed class ClawInputController : MonoBehaviour, IClawInputSource
    {
        public ClawInputFrame Read()
        {
            return new ClawInputFrame {
                Move = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                    (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0)),
                Grab = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space),
                Pause = Input.GetKeyDown(KeyCode.Escape), Reset = Input.GetKeyDown(KeyCode.R),
                View = Input.GetKeyDown(KeyCode.Alpha1) ? 1 : Input.GetKeyDown(KeyCode.Alpha2) ? 2 : Input.GetKeyDown(KeyCode.Alpha3) ? 3 : 0,
                Zoom = Input.mouseScrollDelta.y
            };
        }
    }
}

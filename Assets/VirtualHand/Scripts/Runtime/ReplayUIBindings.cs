using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Persistent button->handler wiring for the generated demo scene. Editor-
    /// added runtime delegates (Button.onClick.AddListener) are NOT serialized
    /// into the scene, so the builder stores plain component references (Buttons,
    /// DemoUI, TaskController) and this component binds concrete runtime methods
    /// OnEnable / unbinds OnDisable — nothing here depends on editor code or on
    /// delegates that cannot survive a scene save/reload.
    /// </summary>
    public class ReplayUIBindings : MonoBehaviour
    {
        public Button db8Button;
        public Button eegButton;
        public Button labelsButton;
        public Button subjectS001Button;
        public Button subjectS002Button;
        public Button subjectS003Button;
        public Button playPauseButton;
        public Button restartButton;
        public Button carryButton;
        public Button handoffL2RButton;
        public Button handoffR2LButton;

        [HideInInspector] public DemoUI ui;     // assigned by the scene builder
        public TaskController tasks;            // serialized scene reference

        void OnEnable()
        {
            BindButtons();
        }

        void OnDisable()
        {
            // paired unbind so reload/disable never stacks duplicate listeners
            if (ui != null)
            {
                Unbind(db8Button, ui.LoadDb8Clip);
                Unbind(eegButton, ui.LoadEegPredictions);
                Unbind(labelsButton, ui.LoadLabels);
                Unbind(subjectS001Button, SelectS001);
                Unbind(subjectS002Button, SelectS002);
                Unbind(subjectS003Button, SelectS003);
                Unbind(playPauseButton, ui.TogglePlayPause);
                Unbind(restartButton, ui.RestartReplay);
            }
            Unbind(carryButton, StartCarry);
            Unbind(handoffL2RButton, StartHandoffL2R);
            Unbind(handoffR2LButton, StartHandoffR2L);
        }

        /// <summary>Bind all serialized buttons to their concrete handlers.
        /// Public so tests can invoke the exact OnEnable work: EditMode does not
        /// call OnEnable on plain MonoBehaviours (no ExecuteAlways), while the
        /// running game always does — same statements, same order.</summary>
        public void BindButtons()
        {
            if (ui != null)
            {
                Bind(db8Button, ui.LoadDb8Clip);
                Bind(eegButton, ui.LoadEegPredictions);
                Bind(labelsButton, ui.LoadLabels);
                Bind(subjectS001Button, SelectS001);
                Bind(subjectS002Button, SelectS002);
                Bind(subjectS003Button, SelectS003);
                Bind(playPauseButton, ui.TogglePlayPause);
                Bind(restartButton, ui.RestartReplay);
            }
            Bind(carryButton, StartCarry);
            Bind(handoffL2RButton, StartHandoffL2R);
            Bind(handoffR2LButton, StartHandoffR2L);
        }

        static void Bind(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.AddListener(a);
        }

        static void Unbind(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.RemoveListener(a);
        }

        public void SelectS001() { if (ui != null) ui.SelectSubject("S001"); }
        public void SelectS002() { if (ui != null) ui.SelectSubject("S002"); }
        public void SelectS003() { if (ui != null) ui.SelectSubject("S003"); }

        // ---- task entries: concrete runtime methods over serialized refs ----

        public void StartCarry()
        {
            if (tasks == null) { Debug.LogWarning("[Bindings] TaskController missing"); return; }
            if (!TryFindTaskProps(out var box, out var zone)) return;
            Vector3[] path =
            {
                box.transform.position + new Vector3(0f, 0.19f, 0.3f),  // lift waypoint
                zone != null ? zone.transform.position + Vector3.up * 0.06f
                             : box.transform.position,
            };
            tasks.Begin(TaskController.TaskType.Carry, path, box);
        }

        public void StartHandoffL2R() => StartHandoff(leftToRight: true);
        public void StartHandoffR2L() => StartHandoff(leftToRight: false);

        void StartHandoff(bool leftToRight)
        {
            if (tasks == null) { Debug.LogWarning("[Bindings] TaskController missing"); return; }
            if (!TryFindTaskProps(out var box, out var zone)) return;
            Vector3[] path =
            {
                box.transform.position + new Vector3(0f, 0.19f, 0.3f),  // transfer point
                zone != null ? zone.transform.position + Vector3.up * 0.06f
                             : box.transform.position,
            };
            tasks.Begin(TaskController.TaskType.Handoff, path, box, leftIsSender: leftToRight);
        }

        bool TryFindTaskProps(out GameObject box, out GameObject zone)
        {
            tasks.End(); // restore the previous scene positions before constructing the next path
            box = GameObject.Find("CarryBox");
            zone = GameObject.Find("DropZone");
            if (box == null) Debug.LogWarning("[Bindings] CarryBox missing");
            if (zone == null) Debug.LogWarning("[Bindings] DropZone missing; task release disabled");
            return box != null && zone != null;
        }
    }
}

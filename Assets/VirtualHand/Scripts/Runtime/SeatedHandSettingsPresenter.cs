using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// UGUI presenter for the seated-hand preferences. It edits only an in-memory
    /// profile until Save is pressed. It does not control tracking or scene objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatedHandSettingsPresenter : MonoBehaviour
    {
        public const string DefaultPlayerPrefsKey = "Neuralife.VirtualHand.SeatedHandSettings";

        [Header("Persistence")]
        [SerializeField] private string playerPrefsKey = DefaultPlayerPrefsKey;

        [Header("Panel")]
        [SerializeField] private CanvasGroup panelGroup;

        [Header("Buttons")]
        [SerializeField] private Button openButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button affectedUnspecifiedButton;
        [SerializeField] private Button affectedLeftButton;
        [SerializeField] private Button affectedRightButton;
        [SerializeField] private Button operatingUnspecifiedButton;
        [SerializeField] private Button operatingLeftButton;
        [SerializeField] private Button operatingRightButton;
        [SerializeField] private Button measuredHandsButton;
        [SerializeField] private Button singleOperatingHandButton;
        [SerializeField] private Button mirrorButton;
        [SerializeField] private Button captureLeftWristButton;
        [SerializeField] private Button captureRightWristButton;

        [Header("Button labels: bind each button's child Unity UI Text")]
        [SerializeField] private Text openButtonLabel;
        [SerializeField] private Text saveButtonLabel;
        [SerializeField] private Text cancelButtonLabel;
        [SerializeField] private Text resetButtonLabel;
        [SerializeField] private Text affectedUnspecifiedLabel;
        [SerializeField] private Text affectedLeftLabel;
        [SerializeField] private Text affectedRightLabel;
        [SerializeField] private Text operatingUnspecifiedLabel;
        [SerializeField] private Text operatingLeftLabel;
        [SerializeField] private Text operatingRightLabel;
        [SerializeField] private Text measuredHandsLabel;
        [SerializeField] private Text singleOperatingHandLabel;
        [SerializeField] private Text mirrorLabel;
        [SerializeField] private Text captureLeftWristLabel;
        [SerializeField] private Text captureRightWristLabel;

        [Header("Headings and feedback")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text affectedHeadingText;
        [SerializeField] private Text operatingHeadingText;
        [SerializeField] private Text displayHeadingText;
        [SerializeField] private Text resetHintText;
        [SerializeField] private Text displayModeHintText;
        [SerializeField] private Text statusText;

        private SeatedHandSettings draft;
        private bool isOpen;
        private bool hasUnsavedChanges;
        private string statusMessage = string.Empty;

        /// <summary>The exact preference key this presenter reads and writes.</summary>
        public string PlayerPrefsKey => playerPrefsKey;

        private void Awake()
        {
            SetChineseCopy();
            SetPanelVisible(false);
            RefreshView();
        }

        private void OnDisable()
        {
            // Disabling the presenter is equivalent to cancelling its staged edits.
            draft = null;
            isOpen = false;
            hasUnsavedChanges = false;
            statusMessage = string.Empty;
            SetPanelVisible(false);
        }

        /// <summary>Loads the saved profile again each time the panel is opened.</summary>
        public void Open()
        {
            if (isOpen)
                return;

            isOpen = true;
            draft = null;
            hasUnsavedChanges = false;
            statusMessage = string.Empty;
            SetPanelVisible(true);

            if (string.IsNullOrWhiteSpace(playerPrefsKey))
            {
                SetStatus("设置无法载入：请在检查器中填写 PlayerPrefs 键。 ");
                RefreshView();
                return;
            }

            draft = SeatedHandSettings.Load(playerPrefsKey);
            RefreshView();
        }

        /// <summary>Saves the staged profile to this presenter's one configured key.</summary>
        public void Save()
        {
            if (!isOpen || draft == null)
                return;

            if (!hasUnsavedChanges)
            {
                SetStatus("没有待保存的更改。");
                RefreshView();
                return;
            }

            if (string.IsNullOrWhiteSpace(playerPrefsKey))
            {
                SetStatus("保存失败：请在检查器中填写 PlayerPrefs 键。");
                RefreshView();
                return;
            }

            if (draft.Save(playerPrefsKey))
            {
                hasUnsavedChanges = false;
                SetStatus("已保存。");
            }
            else
            {
                // SeatedHandSettings.Save rejects protected newer schemas and invalid data
                // without changing PlayerPrefs. Keep the panel open so the user can respond.
                SetStatus("保存失败：设置版本较新或内容无效；已保存的数据未更改。");
            }

            RefreshView();
        }

        /// <summary>Discards staged edits. This method never writes PlayerPrefs.</summary>
        public void Cancel()
        {
            if (!isOpen)
                return;

            draft = null;
            isOpen = false;
            hasUnsavedChanges = false;
            statusMessage = string.Empty;
            SetPanelVisible(false);
            RefreshView();
        }

        /// <summary>
        /// Stages defaults for the three choices shown here. Save is still required.
        /// Wrist offsets and the settings model's newer-schema protection are preserved.
        /// </summary>
        public void ResetDraftToDefaults()
        {
            if (!CanEdit())
                return;

            bool changed = draft.affectedHand != SeatedHandSide.Unspecified ||
                           draft.operatingHand != SeatedHandSide.Unspecified ||
                           draft.displayMode != SeatedHandDisplayMode.MeasuredHands;

            if (!changed)
            {
                SetStatus("本页选项已是默认值。");
                RefreshView();
                return;
            }

            // Do not call ResetToDefaults here: this presenter owns only these three
            // choices, and ResetToDefaults would also reset host-owned wrist offsets.
            draft.affectedHand = SeatedHandSide.Unspecified;
            draft.operatingHand = SeatedHandSide.Unspecified;
            draft.displayMode = SeatedHandDisplayMode.MeasuredHands;
            MarkDraftChanged();
        }

        public void SetAffectedHand(SeatedHandSide side)
        {
            if (!CanEdit() || !IsValidSide(side) || draft.affectedHand == side)
                return;

            draft.affectedHand = side;
            MarkDraftChanged();
        }

        public void SetOperatingHand(SeatedHandSide side)
        {
            if (!CanEdit() || !IsValidSide(side) || draft.operatingHand == side)
                return;

            draft.operatingHand = side;
            MarkDraftChanged();
        }

        public void SetDisplayMode(SeatedHandDisplayMode mode)
        {
            if (!CanEdit() || !IsValidDisplayMode(mode) || draft.displayMode == mode)
                return;

            // Mirror is only a stored, deliberate display-mode choice. The host owns
            // any rendering behavior associated with it.
            draft.displayMode = mode;
            MarkDraftChanged();
        }

        // Parameterless wrappers are convenient persistent UGUI Button callbacks.
        public void ChooseAffectedUnspecified() => SetAffectedHand(SeatedHandSide.Unspecified);
        public void ChooseAffectedLeft() => SetAffectedHand(SeatedHandSide.Left);
        public void ChooseAffectedRight() => SetAffectedHand(SeatedHandSide.Right);
        public void ChooseOperatingUnspecified() => SetOperatingHand(SeatedHandSide.Unspecified);
        public void ChooseOperatingLeft() => SetOperatingHand(SeatedHandSide.Left);
        public void ChooseOperatingRight() => SetOperatingHand(SeatedHandSide.Right);
        public void ChooseMeasuredHands() => SetDisplayMode(SeatedHandDisplayMode.MeasuredHands);
        public void ChooseSingleOperatingHand() => SetDisplayMode(SeatedHandDisplayMode.SingleOperatingHand);
        public void ChooseMirror() => SetDisplayMode(SeatedHandDisplayMode.Mirror);

        /// <summary>Displays a capture error in the panel's existing status area.</summary>
        public void ReportWristCaptureError(string message)
        {
            SetStatus(message ?? string.Empty);
            RefreshView();
        }

        /// <summary>Stages a captured offset in the open draft. This method never writes preferences.</summary>
        public bool ApplyCapturedWristOffset(SeatedHandSide side, SeatedHandSettings.WristOffset offset)
        {
            if (!CanEdit() ||
                !IsFinite(offset.translationMeters) ||
                !IsFinite(offset.rotationEulerDegrees))
            {
                return false;
            }

            if (side == SeatedHandSide.Left)
            {
                if (IsSameOffset(draft.leftWristOffset, offset))
                {
                    SetStatus(string.Empty);
                    RefreshView();
                    return true;
                }

                draft.leftWristOffset = offset;
            }
            else if (side == SeatedHandSide.Right)
            {
                if (IsSameOffset(draft.rightWristOffset, offset))
                {
                    SetStatus(string.Empty);
                    RefreshView();
                    return true;
                }

                draft.rightWristOffset = offset;
            }
            else
            {
                return false;
            }

            MarkDraftChanged();
            return true;
        }

        private static bool IsSameOffset(SeatedHandSettings.WristOffset first, SeatedHandSettings.WristOffset second)
        {
            return first.translationMeters == second.translationMeters &&
                   first.rotationEulerDegrees == second.rotationEulerDegrees;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool CanEdit()
        {
            return isOpen && draft != null;
        }

        private void MarkDraftChanged()
        {
            hasUnsavedChanges = true;
            SetStatus("有未保存的更改。");
            RefreshView();
        }

        private void SetChineseCopy()
        {
            SetText(titleText, "坐位手部设置");
            SetText(affectedHeadingText, "患侧");
            SetText(operatingHeadingText, "操作侧（独立选择）");
            SetText(displayHeadingText, "显示方式");
            SetText(openButtonLabel, "设置");
            SetText(saveButtonLabel, "保存");
            SetText(cancelButtonLabel, "取消");
            SetText(resetButtonLabel, "恢复本页默认");
            SetText(resetHintText, "仅恢复本页选项；点击保存后生效。腕部偏移会保留。");
            SetText(statusText, string.Empty);
        }

        private void RefreshView()
        {
            bool canEdit = CanEdit();
            SetButtonInteractable(openButton, !isOpen);
            SetButtonInteractable(saveButton, canEdit && hasUnsavedChanges);
            SetButtonInteractable(cancelButton, isOpen);
            SetButtonInteractable(resetButton, canEdit);
            SetButtonInteractable(affectedUnspecifiedButton, canEdit);
            SetButtonInteractable(affectedLeftButton, canEdit);
            SetButtonInteractable(affectedRightButton, canEdit);
            SetButtonInteractable(operatingUnspecifiedButton, canEdit);
            SetButtonInteractable(operatingLeftButton, canEdit);
            SetButtonInteractable(operatingRightButton, canEdit);
            SetButtonInteractable(measuredHandsButton, canEdit);
            SetButtonInteractable(singleOperatingHandButton, canEdit);
            SetButtonInteractable(mirrorButton, canEdit);
            SetButtonInteractable(captureLeftWristButton, canEdit);
            SetButtonInteractable(captureRightWristButton, canEdit);

            SetText(affectedUnspecifiedLabel, SelectionLabel("未指定", canEdit && draft.affectedHand == SeatedHandSide.Unspecified));
            SetText(affectedLeftLabel, SelectionLabel("左手", canEdit && draft.affectedHand == SeatedHandSide.Left));
            SetText(affectedRightLabel, SelectionLabel("右手", canEdit && draft.affectedHand == SeatedHandSide.Right));
            SetText(operatingUnspecifiedLabel, SelectionLabel("未指定", canEdit && draft.operatingHand == SeatedHandSide.Unspecified));
            SetText(operatingLeftLabel, SelectionLabel("左手", canEdit && draft.operatingHand == SeatedHandSide.Left));
            SetText(operatingRightLabel, SelectionLabel("右手", canEdit && draft.operatingHand == SeatedHandSide.Right));
            SetText(measuredHandsLabel, SelectionLabel("采集到的双手", canEdit && draft.displayMode == SeatedHandDisplayMode.MeasuredHands));
            SetText(singleOperatingHandLabel, SelectionLabel("仅操作手", canEdit && draft.displayMode == SeatedHandDisplayMode.SingleOperatingHand));
            SetText(mirrorLabel, SelectionLabel("对侧手镜像", canEdit && draft.displayMode == SeatedHandDisplayMode.Mirror));
            SetText(captureLeftWristLabel, "采集左手腕部中立位");
            SetText(captureRightWristLabel, "采集右手腕部中立位");

            SetText(displayModeHintText,
                canEdit && draft.displayMode == SeatedHandDisplayMode.Mirror
                    ? "操作手由手柄追踪。对侧手依身体中线镜像显示，请先选定操作手。"
                    : "把操作手放在舒适的位置，采集该侧腕位后保存。 ");
            SetText(statusText, statusMessage);
        }

        private static string SelectionLabel(string label, bool selected)
        {
            return selected ? label + "（已选）" : label;
        }

        private void SetStatus(string message)
        {
            statusMessage = message;
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelGroup == null)
                return;

            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
                button.interactable = interactable;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
                target.text = value;
        }

        private static bool IsValidSide(SeatedHandSide side)
        {
            return side == SeatedHandSide.Unspecified ||
                   side == SeatedHandSide.Left ||
                   side == SeatedHandSide.Right;
        }

        private static bool IsValidDisplayMode(SeatedHandDisplayMode mode)
        {
            return mode == SeatedHandDisplayMode.MeasuredHands ||
                   mode == SeatedHandDisplayMode.SingleOperatingHand ||
                   mode == SeatedHandDisplayMode.Mirror;
        }
    }
}

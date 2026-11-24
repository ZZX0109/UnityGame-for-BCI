using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Bindable configuration for one hand action: which action, which hand, which
    /// control, how it triggers, and how the hand behaves when it ends.
    /// </summary>
    [Serializable]
    public sealed class HandActionBinding
    {
        public HandAction action = HandAction.Open;
        public SeatedHandSide side = SeatedHandSide.Right;

        [Tooltip("Input System control path, e.g. <Keyboard>/1 or <XRController>/trigger.")]
        public string bindingPath = "";

        public HandActionTrigger trigger = HandActionTrigger.OnPress;

        [Tooltip("Return to the natural rest pose when the key is released.")]
        public bool restoreRestOnRelease = true;

        [Tooltip("Seconds to blend from the previous pose into this action's pose.")]
        [Range(0.02f, 2f)] public float transitionSeconds = 0.15f;

        [Tooltip("Allow this action to stack on top of other active finger flexes.")]
        public bool allowCombination = true;

        /// <summary>
        /// Stable key used for save data, conflict reports and action names.
        ///
        /// The separator is deliberately '.' and NOT '/': the Input System treats a
        /// slash in a lookup string as a "map/action" path, so an action literally
        /// named "Open/Left" can never be found through
        /// InputActionAsset.FindAction("Open/Left") — it is parsed as map "Open",
        /// action "Left". Keeping slashes out of the name avoids that entire trap.
        /// </summary>
        public string Key => action + "." + side;
    }

    /// <summary>
    /// Player-editable key bindings for the virtual hand, stored separately from the
    /// three games' own input settings.
    ///
    /// Implementation notes that matter for correctness:
    ///   • The InputActionAsset is generated from JSON with deterministic action and
    ///     binding ids derived from the action key. Random ids would silently break
    ///     reloading of saved Binding Overrides, because the override JSON refers to
    ///     those ids.
    ///   • Overrides are stored through the Input System's own serializer in a
    ///     VirtualHand-only PlayerPrefs key, so nothing the games own is touched.
    ///   • Conflict detection is advisory: it reports overlapping controls so the UI
    ///     can offer a rebind. It never silently steals a game key.
    /// </summary>
    [CreateAssetMenu(menuName = "Neuralife/VirtualHand/Hand Action Binding Profile",
                     fileName = "HandActionBindingProfile")]
    public class HandActionBindingProfile : ScriptableObject
    {
        public const string MapName = "VirtualHand";
        public const string AssetName = "VirtualHandActions";
        public const string PrefsKey = "Neuralife.VirtualHand.BindingOverrides.v1";

        [Tooltip("Bindings shipped as the defaults. Reset restores exactly this list.")]
        public List<HandActionBinding> bindings = new List<HandActionBinding>();

        /// <summary>
        /// Controls the host reserves for its own gameplay. Binding one of these is
        /// reported as a conflict rather than silently applying.
        /// </summary>
        public List<string> reservedPaths = new List<string>();

        /// <summary>
        /// Deterministic id for a seed string, so saved overrides keep working across
        /// sessions and across edits to unrelated actions.
        /// </summary>
        public static Guid StableId(string seed)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("Neuralife.VirtualHand/" + seed));
                return new Guid(hash);
            }
        }

        /// <summary>
        /// Generates the .inputactions JSON in memory. Written by hand rather than via
        /// AddAction/AddBinding because only the serialized form lets us pin the ids.
        /// </summary>
        public string BuildAssetJson()
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"name\":\"").Append(AssetName).Append("\",\"maps\":[{");
            sb.Append("\"name\":\"").Append(MapName).Append("\",");
            sb.Append("\"id\":\"").Append(StableId("map/" + MapName)).Append("\",");
            sb.Append("\"actions\":[");

            bool first = true;
            foreach (var b in bindings)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.bindingPath))
                    continue;

                if (!first) sb.Append(',');
                first = false;

                sb.Append("{\"name\":\"").Append(Escape(b.Key)).Append("\",");
                sb.Append("\"type\":\"").Append(IsButton(b.action) ? "Button" : "Value").Append("\",");
                sb.Append("\"id\":\"").Append(StableId("action/" + b.Key)).Append("\",");
                sb.Append("\"expectedControlType\":\"\",\"processors\":\"\",");
                sb.Append("\"interactions\":\"\",\"initialStateCheck\":false}");
            }

            sb.Append("],\"bindings\":[");
            first = true;
            foreach (var b in bindings)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.bindingPath))
                    continue;

                if (!first) sb.Append(',');
                first = false;

                sb.Append("{\"name\":\"").Append(Escape(b.Key)).Append("\",");
                sb.Append("\"id\":\"").Append(StableId("binding/" + b.Key + "@" + b.bindingPath)).Append("\",");
                sb.Append("\"path\":\"").Append(Escape(b.bindingPath)).Append("\",");
                sb.Append("\"interactions\":\"\",\"processors\":\"\",\"groups\":\"\",");
                sb.Append("\"action\":\"").Append(Escape(b.Key)).Append("\",");
                sb.Append("\"isComposite\":false,\"isPartOfComposite\":false}");
            }

            sb.Append("]}],\"controlSchemes\":[]}");
            return sb.ToString();
        }

        static bool IsButton(HandAction action) =>
            action == HandAction.Open || action == HandAction.Fist ||
            action == HandAction.Pinch || action == HandAction.Point ||
            action == HandAction.Release || action == HandAction.ToggleAppearance ||
            action == HandAction.GuidanceStart || action == HandAction.GuidanceEnd;

        static string Escape(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

#if ENABLE_INPUT_SYSTEM
        InputActionAsset _asset;

        /// <summary>The live asset. Rebuilt on demand; ids are deterministic.</summary>
        public InputActionAsset Asset => _asset ?? (_asset = BuildAsset());

        /// <summary>Builds the asset from the current binding list and loads overrides.</summary>
        public InputActionAsset BuildAsset()
        {
            InputActionAsset asset;
            try
            {
                asset = InputActionAsset.FromJson(BuildAssetJson());
            }
            catch (Exception e)
            {
                Debug.LogError("[HandActionBindingProfile] generated action JSON was rejected: " + e.Message);
                return null;
            }

            asset.name = AssetName;
            LoadOverrides(asset);
            return asset;
        }

        /// <summary>Restores the saved Binding Overrides JSON, if the ids still match.</summary>
        public void LoadOverrides(InputActionAsset asset = null)
        {
            asset = asset ?? _asset;
            if (asset == null)
                return;

            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                // A stale or hand-edited blob must not break startup: report and fall
                // back to the shipped defaults.
                Debug.LogWarning("[HandActionBindingProfile] ignoring unreadable binding overrides: " + e.Message);
                PlayerPrefs.DeleteKey(PrefsKey);
            }
        }

        /// <summary>Persists the current overrides. Only touches the VirtualHand key.</summary>
        public void SaveOverrides()
        {
            if (_asset == null)
                return;
            PlayerPrefs.SetString(PrefsKey, _asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Drops every override and returns to the shipped defaults.
        /// Returns a freshly built asset, because overrides live inside the instance.
        /// </summary>
        public InputActionAsset ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            _asset = BuildAsset();
            return _asset;
        }

        /// <summary>
        /// Points an action at a new control and persists it as a Binding Override.
        /// Returns the action key whose binding was already on that control, or null.
        ///
        /// The authored <see cref="HandActionBinding.bindingPath"/> is deliberately
        /// left untouched. A rebind is runtime user state, so it belongs in the
        /// override blob — which is also the only form of persistence that survives in
        /// a player build, where the profile asset is read-only. Because the authored
        /// path never changes, the derived binding id stays stable and the saved
        /// override keeps resolving across sessions; and ResetToDefaults is a genuine
        /// restore rather than a partial one.
        /// </summary>
        public string Rebind(string actionKey, string newPath)
        {
            var action = FindAction(actionKey);
            if (action == null || string.IsNullOrWhiteSpace(newPath))
                return null;

            string replaced = null;
            foreach (var b in bindings)
            {
                if (b == null || b.Key == actionKey)
                    continue;
                if (EffectivePath(b.Key) == newPath)
                {
                    replaced = b.Key;
                    break;
                }
            }

            if (action.bindings.Count > 0)
                action.ApplyBindingOverride(0, newPath);

            // The override now lives on the asset, so it can be serialized. Nulling the
            // asset before this point would make SaveOverrides a silent no-op.
            SaveOverrides();

            return replaced;
        }

        /// <summary>
        /// The control path currently in force for an action: the override when one
        /// is set, otherwise the authored default.
        /// </summary>
        public string EffectivePath(string actionKey)
        {
            var action = FindAction(actionKey);
            if (action == null || action.bindings.Count == 0)
                return null;
            return action.bindings[0].effectivePath;
        }

        /// <summary>
        /// Map-scoped lookup. Always resolving through the map avoids the
        /// "map/action" parsing in InputActionAsset.FindAction entirely.
        /// </summary>
        public InputAction FindAction(string actionKey)
        {
            var asset = Asset;
            if (asset == null || string.IsNullOrEmpty(actionKey))
                return null;

            var map = asset.FindActionMap(MapName);
            return map?.FindAction(actionKey);
        }
#endif

        /// <summary>
        /// Reports bindings that collide with host-reserved controls. Duplicates
        /// within the profile are reported too, because one key must not trigger two
        /// different operations.
        /// </summary>
        public List<string> DetectConflicts()
        {
            var problems = new List<string>();
            var seen = new Dictionary<string, string>();

            foreach (var b in bindings)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.bindingPath))
                    continue;

                if (reservedPaths != null && reservedPaths.Contains(b.bindingPath))
                    problems.Add($"“{b.bindingPath}” 已被游戏占用，动作 {HandActionDefinitions.DisplayName(b.action)} 需要重新绑定");

                if (seen.TryGetValue(b.bindingPath, out string other))
                    problems.Add($"“{b.bindingPath}” 同时绑定到 {other} 和 {b.Key}");
                else
                    seen[b.bindingPath] = b.Key;
            }

            return problems;
        }

        /// <summary>Builds the shipped default layout: digits for poses, letters for extras.</summary>
        public static HandActionBindingProfile CreateDefault()
        {
            var profile = CreateInstance<HandActionBindingProfile>();
            profile.name = "HandActionBindingProfile";

            void Add(HandAction action, SeatedHandSide side, string path,
                     HandActionTrigger trigger = HandActionTrigger.OnPress,
                     bool restore = true, float transition = 0.15f)
            {
                profile.bindings.Add(new HandActionBinding
                {
                    action = action,
                    side = side,
                    bindingPath = path,
                    trigger = trigger,
                    restoreRestOnRelease = restore,
                    transitionSeconds = transition,
                    allowCombination = true
                });
            }

            // Left poses on 1..5, right poses on 6..0: the same physical keys the
            // legacy DemoUI used, so existing muscle memory keeps working.
            Add(HandAction.Open, SeatedHandSide.Left, "<Keyboard>/1");
            Add(HandAction.Fist, SeatedHandSide.Left, "<Keyboard>/2");
            Add(HandAction.Pinch, SeatedHandSide.Left, "<Keyboard>/3");
            Add(HandAction.Point, SeatedHandSide.Left, "<Keyboard>/4");
            Add(HandAction.Release, SeatedHandSide.Left, "<Keyboard>/5");

            Add(HandAction.Open, SeatedHandSide.Right, "<Keyboard>/6");
            Add(HandAction.Fist, SeatedHandSide.Right, "<Keyboard>/7");
            Add(HandAction.Pinch, SeatedHandSide.Right, "<Keyboard>/8");
            Add(HandAction.Point, SeatedHandSide.Right, "<Keyboard>/9");
            Add(HandAction.Release, SeatedHandSide.Right, "<Keyboard>/0");

            Add(HandAction.ToggleAppearance, SeatedHandSide.Right, "<Keyboard>/b",
                restore: false, transition: 0.05f);
            Add(HandAction.GuidanceStart, SeatedHandSide.Right, "<Keyboard>/g",
                restore: false, transition: 0.05f);
            Add(HandAction.GuidanceEnd, SeatedHandSide.Right, "<Keyboard>/h",
                restore: false, transition: 0.05f);

            return profile;
        }

        /// <summary>
        /// Default reserved set for the three shipped games, so a fresh profile does
        /// not sit on top of their core controls.
        /// </summary>
        public static List<string> DefaultReservedPaths() => new List<string>
        {
            // Game01 推箱子
            "<Keyboard>/w", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d",
            "<Keyboard>/e", "<Keyboard>/z", "<Keyboard>/r",
            // Game02 抓娃娃机
            "<Keyboard>/space", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow",
            "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/f",
            // Game03 过马路
            "<Keyboard>/escape", "<Keyboard>/tab", "<Keyboard>/shift"
        };
    }
}
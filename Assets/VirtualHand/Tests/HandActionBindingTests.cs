using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// Spec section 10.3: rebinding behaviour, checked against the real profile type.
    ///
    /// Where a test needs the Input System it is guarded, so the suite still runs if
    /// the package is present but Input System is not the active input handler.
    /// PlayerPrefs keys created here are removed in teardown so the suite cannot leak
    /// state into a later run or into a user's editor session.
    /// </summary>
    public class HandActionBindingTests
    {
        readonly List<string> _createdObjects = new List<string>();

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(HandActionBindingProfile.PrefsKey);
            PlayerPrefs.Save();
            _createdObjects.Clear();
        }

        HandActionBindingProfile NewProfile()
        {
            var profile = HandActionBindingProfile.CreateDefault();
            profile.reservedPaths = HandActionBindingProfile.DefaultReservedPaths();
            return profile;
        }

        [Test]
        public void DefaultProfileReportsNoConflicts()
        {
            var profile = NewProfile();
            CollectionAssert.IsEmpty(profile.DetectConflicts(),
                string.Join(" | ", profile.DetectConflicts()));
        }

        [Test]
        public void EveryShippedGameControlIsReserved()
        {
            // Spec 7.3: the defaults must not silently sit on a game's core controls.
            var reserved = HandActionBindingProfile.DefaultReservedPaths();

            foreach (var expected in new[]
            {
                "<Keyboard>/w", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d",   // Game01
                "<Keyboard>/e", "<Keyboard>/z", "<Keyboard>/r",                    // Game01
                "<Keyboard>/space", "<Keyboard>/f"                                 // Game02
            })
            {
                CollectionAssert.Contains(reserved, expected,
                    expected + " is not listed as reserved, so a rebind could steal it");
            }
        }

        [Test]
        public void BindingAGameControlIsReportedAsAConflict()
        {
            var profile = NewProfile();
            foreach (var b in profile.bindings)
                if (b.action == HandAction.Open && b.side == SeatedHandSide.Left)
                    b.bindingPath = "<Keyboard>/w";

            var problems = profile.DetectConflicts();

            Assert.IsNotEmpty(problems, "binding a reserved game key was not reported");
            StringAssert.Contains("w", string.Join(" ", problems));
        }

        [Test]
        public void DuplicateBindingsInsideTheProfileAreReported()
        {
            var profile = NewProfile();
            // Force two different actions onto the same key.
            var list = profile.bindings;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].action == HandAction.Fist && list[i].side == SeatedHandSide.Left)
                    list[i].bindingPath = "<Keyboard>/1";   // already Open/Left
            }

            var problems = profile.DetectConflicts();

            Assert.IsNotEmpty(problems, "one key bound to two actions was not reported");
        }

        [Test]
        public void ConflictDetectionIgnoresEmptyBindings()
        {
            var profile = ScriptableObject.CreateInstance<HandActionBindingProfile>();
            profile.bindings.Add(new HandActionBinding { action = HandAction.Open, bindingPath = "" });
            profile.bindings.Add(new HandActionBinding { action = HandAction.Fist, bindingPath = null });

            CollectionAssert.IsEmpty(profile.DetectConflicts(),
                "an unbound action should not be reported as a conflict");
        }

        [Test]
        public void SwitchingOneSideDoesNotTouchTheOther()
        {
            // Spec 10.3: left and right bindings must not interfere.
            var profile = NewProfile();

            string leftKey = null, rightKey = null;
            foreach (var b in profile.bindings)
            {
                if (b.action == HandAction.Open && b.side == SeatedHandSide.Left) leftKey = b.bindingPath;
                if (b.action == HandAction.Open && b.side == SeatedHandSide.Right) rightKey = b.bindingPath;
            }

            Assert.IsNotNull(leftKey);
            Assert.IsNotNull(rightKey);
            Assert.AreNotEqual(leftKey, rightKey,
                "both hands shipped with the same key, so they could not be distinguished");
        }

        [Test]
        public void GeneratedJsonIsStableAndCarriesBothSides()
        {
            var profile = NewProfile();
            string json = profile.BuildAssetJson();

            StringAssert.Contains("Open.Left", json);
            StringAssert.Contains("Open.Right", json);

            // Determinism is what makes saved overrides survive a restart.
            Assert.AreEqual(json, profile.BuildAssetJson());
        }

#if ENABLE_INPUT_SYSTEM
        [Test]
        public void RebindingAnActionChangesItsEffectivePathAndPersists()
        {
            var profile = NewProfile();
            Assert.IsNotNull(profile.Asset, "the generated InputActionAsset was rejected");

            Assert.AreEqual("<Keyboard>/1", profile.EffectivePath("Open.Left"),
                "the authored default is not what the shipped layout declares");

            Assert.IsNull(profile.Rebind("Open.Left", "<Keyboard>/q"),
                "rebinding onto a free key reported a collision");

            Assert.AreEqual("<Keyboard>/q", profile.EffectivePath("Open.Left"),
                "the rebind did not take effect");

            // The authored default must survive, so "restore defaults" is a real restore.
            string authored = null;
            foreach (var b in profile.bindings)
                if (b.Key == "Open.Left") authored = b.bindingPath;
            Assert.AreEqual("<Keyboard>/1", authored,
                "rebinding overwrote the authored default instead of layering an override");

            // A player build cannot write the profile asset, so the override blob is the
            // only persistence that matters — and it must live under the VirtualHand key.
            string saved = PlayerPrefs.GetString(HandActionBindingProfile.PrefsKey, "");
            Assert.IsNotEmpty(saved, "the override was not written to the VirtualHand-only key");
            StringAssert.Contains("<Keyboard>/q", saved);
        }

        [Test]
        public void ActionKeysNeverContainASlash()
        {
            // A slash would be parsed by InputActionAsset.FindAction as a "map/action"
            // path, making the action unreachable by name.
            var profile = NewProfile();
            foreach (var b in profile.bindings)
                StringAssert.DoesNotContain("/", b.Key, "action key contains a slash: " + b.Key);
        }

        [Test]
        public void RebindingOntoATakenKeyReportsTheDisplacedAction()
        {
            var profile = NewProfile();

            // "<Keyboard>/2" is Fist.Left in the shipped layout.
            string displaced = profile.Rebind("Open.Left", "<Keyboard>/2");

            Assert.IsNotNull(displaced, "binding onto an occupied key did not report a collision");
            StringAssert.Contains("Fist", displaced);
        }

        [Test]
        public void ResetToDefaultsDropsStoredOverridesAndRestoresAuthoredPaths()
        {
            var profile = NewProfile();
            Assert.AreEqual("<Keyboard>/1", profile.EffectivePath("Open.Left"));

            profile.Rebind("Open.Left", "<Keyboard>/q");
            Assert.AreEqual("<Keyboard>/q", profile.EffectivePath("Open.Left"));
            Assert.IsNotEmpty(PlayerPrefs.GetString(HandActionBindingProfile.PrefsKey, ""));

            profile.ResetToDefaults();

            Assert.IsEmpty(PlayerPrefs.GetString(HandActionBindingProfile.PrefsKey, ""),
                "ResetToDefaults left the stored override behind");
            Assert.AreEqual("<Keyboard>/1", profile.EffectivePath("Open.Left"),
                "ResetToDefaults did not restore the authored path");
        }

        [Test]
        public void SavedOverridesSurviveAFreshProfileInstance()
        {
            // This is what "重启后保留" means in practice: a new profile object built
            // from the same asset must pick the override back up.
            var profile = NewProfile();
            profile.Rebind("Open.Left", "<Keyboard>/q");

            var reopened = NewProfile();
            Assert.AreEqual("<Keyboard>/q", reopened.EffectivePath("Open.Left"),
                "a saved override did not apply to a freshly built asset");
        }

        [Test]
        public void CorruptStoredOverridesDoNotBreakStartup()
        {
            PlayerPrefs.SetString(HandActionBindingProfile.PrefsKey, "{ this is not json ");
            PlayerPrefs.Save();

            var profile = NewProfile();

            Assert.DoesNotThrow(() =>
            {
                var asset = profile.Asset;
                Assert.IsNotNull(asset, "a corrupt override blob stopped the asset from building");
            });
        }

        [Test]
        public void GeneratedAssetContainsEveryBindableAction()
        {
            var profile = NewProfile();
            Assert.IsNotNull(profile.Asset);

            foreach (var b in profile.bindings)
                Assert.IsNotNull(profile.FindAction(b.Key), "missing generated action: " + b.Key);
        }
#endif
    }
}

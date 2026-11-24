using System;
using System.Reflection;
using Neuralife.VirtualHand;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand.EditorTests
{
    public sealed class SeatedHandSettingsPresenterTests
    {
        private string key;
        private string otherKey;
        private GameObject root;
        private SeatedHandSettingsPresenter presenter;
        private Text affectedLeftLabel;
        private Text affectedRightLabel;
        private Text displayMirrorLabel;

        [SetUp]
        public void SetUp()
        {
            key = "VirtualHand.Tests." + Guid.NewGuid().ToString("N");
            otherKey = key + ".Other";
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.SetString(otherKey, "keep");

            root = new GameObject("Seated hand settings presenter test");
            CanvasGroup panel = root.AddComponent<CanvasGroup>();
            presenter = root.AddComponent<SeatedHandSettingsPresenter>();
            SetField("playerPrefsKey", key);
            SetField("panelGroup", panel);

            affectedLeftLabel = CreateLabel("Affected left");
            affectedRightLabel = CreateLabel("Affected right");
            displayMirrorLabel = CreateLabel("Display mirror");
            SetField("affectedLeftLabel", affectedLeftLabel);
            SetField("affectedRightLabel", affectedRightLabel);
            SetField("mirrorLabel", displayMirrorLabel);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.DeleteKey(otherKey);
        }

        [Test]
        public void SaveThenReopenLoadsStagedChoicesAndPreservesOtherKeys()
        {
            presenter.Open();
            presenter.SetAffectedHand(SeatedHandSide.Left);
            presenter.SetOperatingHand(SeatedHandSide.Right);
            presenter.SetDisplayMode(SeatedHandDisplayMode.Mirror);

            // Edits remain staged until Save is called.
            SeatedHandSettings beforeSave = SeatedHandSettings.Load(key);
            Assert.AreEqual(SeatedHandSide.Unspecified, beforeSave.affectedHand);

            presenter.Save();
            SeatedHandSettings saved = SeatedHandSettings.Load(key);
            Assert.AreEqual(SeatedHandSide.Left, saved.affectedHand);
            Assert.AreEqual(SeatedHandSide.Right, saved.operatingHand);
            Assert.AreEqual(SeatedHandDisplayMode.Mirror, saved.displayMode);
            Assert.AreEqual("keep", PlayerPrefs.GetString(otherKey));

            presenter.Cancel();
            presenter.Open();
            StringAssert.Contains("（已选）", affectedLeftLabel.text);
            StringAssert.Contains("（已选）", displayMirrorLabel.text);
        }

        [Test]
        public void CancelDiscardsDraftAndReopenShowsSavedProfile()
        {
            SeatedHandSettings initial = SeatedHandSettings.CreateDefaults();
            initial.affectedHand = SeatedHandSide.Right;
            Assert.IsTrue(initial.Save(key));

            presenter.Open();
            presenter.SetAffectedHand(SeatedHandSide.Left);
            presenter.Cancel();

            Assert.AreEqual(SeatedHandSide.Right, SeatedHandSettings.Load(key).affectedHand);
            presenter.Open();
            StringAssert.Contains("（已选）", affectedRightLabel.text);
            StringAssert.DoesNotContain("（已选）", affectedLeftLabel.text);
        }

        [Test]
        public void NewerStoredSchemaIsNotOverwritten()
        {
            const string futureJson = "{\"schemaVersion\":2,\"affectedHand\":2}";
            PlayerPrefs.SetString(key, futureJson);

            presenter.Open();
            presenter.SetAffectedHand(SeatedHandSide.Left);
            presenter.Save();

            Assert.AreEqual(futureJson, PlayerPrefs.GetString(key));
        }

        private Text CreateLabel(string labelName)
        {
            GameObject labelObject = new GameObject(
                labelName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            labelObject.transform.SetParent(root.transform, false);
            return labelObject.GetComponent<Text>();
        }

        private void SetField(string fieldName, object value)
        {
            FieldInfo field = typeof(SeatedHandSettingsPresenter).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Presenter field not found: " + fieldName);
            field.SetValue(presenter, value);
        }
    }
}

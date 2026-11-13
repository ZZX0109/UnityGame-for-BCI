using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ClawMachine.Tests.PlayMode
{
    public sealed class ClawCanvasSmokeTests
    {
        private const string ScenePath = "Assets/ClawMachine/Scenes/ClawMachineScene.unity";

        [UnityTest]
        public IEnumerator Canvas_UsesOwnedChineseFontAndReleasesItsRuntimeAssetsOnDisable()
        {
            Time.timeScale = 1;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("ClawMachineScene", LoadSceneMode.Single);
#endif
            yield return null;
            ClawGameUI ui = Object.FindObjectOfType<ClawGameUI>();
            Assert.NotNull(ui);
            Assert.IsTrue(ui.IsReady, ui.BootstrapError);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, ui.OwnedCanvas.renderMode);
            CanvasScaler scaler = ui.OwnedCanvas.GetComponent<CanvasScaler>();
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(new Vector2(1280, 720), scaler.referenceResolution);
            Assert.IsTrue(ui.OwnedFont.TryAddCharacters("拾光夹物所安全接近低速接触重心画中画"));
            Assert.IsTrue(ui.OwnedFont.HasCharacters("拾光夹物所安全接近低速接触重心画中画"));
            Assert.AreEqual("TextMeshPro/Mobile/Bitmap", ui.OwnedFont.material.shader.name);
            foreach (TMP_Text text in ui.OwnedCanvas.GetComponentsInChildren<TMP_Text>(true)) Assert.AreSame(ui.OwnedFont, text.font);
            Assert.IsNotNull(ui.OwnedCanvas.GetComponentInChildren<ScrollRect>(true), "Long menu/settings content must remain reachable at short display heights.");
            // These transitions rebuild text while the Canvas is already active.
            // No TMP component may briefly enable with the global default font null.
            ClawGameManager game = Object.FindObjectOfType<ClawGameManager>();
            Assert.NotNull(game);
            game.OpenSettings();
            yield return null;
            game.CloseSettings();
            Assert.IsTrue(game.SelectChallenge(1));
            yield return null;
            foreach (TMP_Text text in ui.OwnedCanvas.GetComponentsInChildren<TMP_Text>(true)) Assert.AreSame(ui.OwnedFont, text.font);
            LogAssert.NoUnexpectedReceived();
            TMP_FontAsset oldFont = ui.OwnedFont;
            ui.enabled = false;
            yield return null;
            Assert.IsFalse(ui.IsReady);
            Assert.IsFalse(oldFont, "The runtime font asset must not leak across scene/UI lifetimes.");
            ui.enabled = true;
            yield return null;
            Assert.IsTrue(ui.IsReady, ui.BootstrapError);
            Assert.AreNotSame(oldFont, ui.OwnedFont);
            Assert.AreEqual(1, ui.GetComponentsInChildren<Canvas>(true).Length);
        }
    }
}

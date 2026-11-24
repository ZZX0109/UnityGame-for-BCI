#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Neuralife.XR.Tests
{
    public sealed class PicoWorldUITests
    {
        [Test] public void TriggerCannotClickUIOrCreateRay()
        {
            var root = new GameObject("No ray test", typeof(EventSystem), typeof(PicoRayInputModule));
            var target = new GameObject("Target", typeof(RectTransform), typeof(Button));
            try {
                int clicks = 0; target.GetComponent<Button>().onClick.AddListener(() => clicks++);
                var module = root.GetComponent<PicoRayInputModule>();
                PicoXR.Controls.Sample(true,true,true,Vector2.zero,Vector2.zero,false,false,false,false,false,false);
                PicoXR.Controls.Sample(true,true,true,Vector2.zero,Vector2.zero,true,false,false,false,false,false);
                module.RefreshRay(null); module.Process();
                PicoXR.Controls.Sample(true,true,true,Vector2.zero,Vector2.zero,false,false,false,false,false,false);
                module.Process();
                Assert.IsFalse(module.HasTarget); Assert.AreEqual(0, clicks);
                Assert.IsEmpty(root.GetComponentsInChildren<LineRenderer>());
            } finally { Object.DestroyImmediate(target); Object.DestroyImmediate(root); }
        }
        [Test] public void InstructionsExpireAndMovementDismissesHintsWithoutReplay()
        {
            var hints = new PicoQuietHints();
            hints.Tick(true,1,Vector3.zero,Quaternion.identity,false); Assert.IsTrue(hints.ControlsVisible);
            hints.Tick(true,8,Vector3.zero,Quaternion.identity,false); Assert.IsFalse(hints.ControlsVisible);
            hints.Tick(false,9,Vector3.zero,Quaternion.identity,false);
            hints.Tick(true,10,Vector3.zero,Quaternion.identity,false); Assert.IsFalse(hints.ControlsVisible);
            Assert.IsTrue(hints.Notify("Move closer",10));
            hints.Tick(true,10.1f,Vector3.zero,Quaternion.identity,false); Assert.IsTrue(hints.MessageVisible);
            hints.Tick(true,10.2f,Vector3.forward,Quaternion.identity,true); Assert.IsFalse(hints.MessageVisible);
            hints.Tick(true,11,Vector3.forward,Quaternion.identity,false); Assert.IsFalse(hints.MessageVisible);
            Assert.IsFalse(hints.Notify("Move closer",11));
            var early = new PicoQuietHints(); early.Tick(true,1,Vector3.zero,Quaternion.identity,true);
            early.Tick(true,2,Vector3.zero,Quaternion.identity,false); Assert.IsFalse(early.ControlsVisible);
        }
    }
}
#endif

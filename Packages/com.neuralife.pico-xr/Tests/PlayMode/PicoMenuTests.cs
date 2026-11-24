#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Neuralife.XR.Tests
{
    public sealed class PicoMenuTests
    {
        [UnityTest]
        public IEnumerator ConfirmWithoutRayDefaultsToStartAndCannotSkipNextPageWhileHeld()
        {
            bool preview = SessionState.GetBool(PicoXR.PreviewKey, false);
            EventSystem previous = EventSystem.current;
            var root = new GameObject("Menu regression");
            try
            {
                SessionState.SetBool(PicoXR.PreviewKey, true);
                var events = new GameObject("Test events", typeof(EventSystem)); events.transform.SetParent(root.transform);
                EventSystem.current = events.GetComponent<EventSystem>();
                var menu = root.AddComponent<PicoMenuNavigation>();
                var first = new GameObject("First page"); first.transform.SetParent(root.transform);
                var second = new GameObject("Second page"); second.transform.SetParent(root.transform);
                Button option = Button(first.transform, "Other choice");
                Button start = Button(first.transform, "Start");
                Button disabled = Button(first.transform, "Unavailable"); disabled.interactable = false;
                Button next = Button(second.transform, "Continue");
                int starts = 0, continuations = 0;
                start.onClick.AddListener(() => { starts++; first.SetActive(false); PicoMenuNavigation.SetPage(second.transform, next); });
                next.onClick.AddListener(() => continuations++);
                PicoMenuNavigation.SetPage(first.transform, start);
                Assert.AreSame(start.gameObject, EventSystem.current.currentSelectedGameObject);
                menu.ProcessInput(Vector2.zero, false, false);
                menu.ProcessInput(Vector2.zero, true, true);
                Assert.AreEqual(1, starts);
                menu.ProcessInput(Vector2.zero, true, true);
                Assert.AreEqual(0, continuations, "Held A must not skip a newly opened page.");
                menu.ProcessInput(Vector2.zero, false, false);
                menu.ProcessInput(Vector2.zero, true, true);
                Assert.AreEqual(1, continuations);
                first.SetActive(true); PicoMenuNavigation.SetPage(first.transform, start);
                menu.ProcessInput(Vector2.zero, false, false);
                menu.ProcessInput(Vector2.down, false, false);
                Assert.AreSame(option.gameObject, EventSystem.current.currentSelectedGameObject, "Navigation wraps past disabled choices.");
                PicoMenuNavigation.SetPage(null);
                menu.ProcessInput(Vector2.zero, true, true);
                Assert.AreEqual(1, starts, "Closed menus cannot consume commands.");
                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (previous) EventSystem.current = previous;
                SessionState.SetBool(PicoXR.PreviewKey, preview);
            }
        }

        [UnityTest]
        public IEnumerator OverlayRemainsVisibleBehindOpaqueWall()
        {
            var root = new GameObject("Overlay rendering regression");
            var texture = new RenderTexture(128, 128, 24);
            var material = new Material(Resources.Load<Shader>("NeuralifeVRUI"));
            var readback = new Texture2D(128, 128, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Camera camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.blue;
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(root.transform);
                wall.transform.position = Vector3.forward; wall.transform.localScale = new Vector3(4, 4, 0.2f);
                var canvas = new GameObject("Behind wall", typeof(RectTransform), typeof(Canvas)); canvas.transform.SetParent(root.transform);
                canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; canvas.GetComponent<Canvas>().worldCamera = camera;
                canvas.transform.position = Vector3.forward * 2; canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(2, 2);
                var target = new GameObject("Readable menu", typeof(RectTransform), typeof(Image)); target.transform.SetParent(canvas.transform, false);
                target.GetComponent<RectTransform>().sizeDelta = new Vector2(2, 2);
                target.GetComponent<Image>().color = Color.green; target.GetComponent<Image>().material = material;
                yield return null;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); readback.Apply();
                Color pixel = readback.GetPixel(64, 64);
                Assert.That(pixel.g, Is.GreaterThan(0.8f)); Assert.That(pixel.r, Is.LessThan(0.2f), "The wall must not occlude the menu.");
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(root);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(readback); Object.DestroyImmediate(material);
            }
        }
        private static Button Button(Transform parent, string name)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); owner.transform.SetParent(parent, false);
            return owner.GetComponent<Button>();
        }
    }
}
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neuralife.XR
{
    // Menus never depend on aiming. A is consumed before gameplay can interpret it.
    [DefaultExecutionOrder(-7000)]
    public sealed class PicoMenuNavigation : MonoBehaviour
    {
        public static PicoMenuNavigation Current { get; private set; }
        public static int ConfirmFrame { get; private set; } = -1;
        public bool HasMenu => page && page.gameObject.activeInHierarchy;
        private Transform page;
        private Selectable preferredSelection;
        private bool releaseRequired, axisArmed;
        private float nextMove;
        private readonly List<Selectable> choices = new List<Selectable>();
        private readonly List<Selectable> candidates = new List<Selectable>();
        private Selectable highlighted;
        private GameObject focusBorder;

        private void OnEnable() { Current = this; }
        private void OnDisable() { if (Current == this) Current = null; }
        public static void SetPage(Transform root, Selectable preferred = null)
        {
            if (!PicoXR.Requested || !Current) return;
            Current.page = root;
            Current.preferredSelection = preferred;
            Current.releaseRequired = true;
            Current.axisArmed = false;
            Current.RefreshChoices();
            if (root) Current.Select(preferred && Current.choices.Contains(preferred) ? preferred :
                Current.choices.Count > 0 ? Current.choices[0] : null);
            else Current.Select(null);
            PicoCanvas.RecenterAll();
        }
        private void RefreshChoices()
        {
            choices.Clear(); candidates.Clear();
            if (!HasMenu) return;
            page.GetComponentsInChildren(false, candidates);
            foreach (Selectable item in candidates)
                if (item && item.IsActive() && item.IsInteractable() && !(item is Scrollbar)) choices.Add(item);
        }
        private void Update()
        {
            if (!HasMenu || !PicoXR.HeadTracked) return;
            ProcessInput(PicoXR.Controls.Move, PicoXR.Controls.Undo.Pressed, PicoXR.Controls.Undo.Held);
        }
        // Public for hardware-independent regression of the same menu path.
        public void ProcessInput(Vector2 axis, bool confirmPressed, bool confirmHeld)
        {
            if (!HasMenu || !EventSystem.current) return;
            RefreshChoices();
            if (choices.Count == 0) return;
            Selectable selected = EventSystem.current.currentSelectedGameObject ?
                EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>() : null;
            if (!choices.Contains(selected)) { selected = choices.Contains(preferredSelection) ? preferredSelection : choices[0]; Select(selected); }
            if (releaseRequired) { if (!confirmHeld) releaseRequired = false; }
            else if (confirmPressed)
            {
                ConfirmFrame = Time.frameCount;
                releaseRequired = true;
                Debug.Log("Neuralife VR menu confirm: " + selected.name);
                ExecuteEvents.Execute(selected.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                return;
            }
            if (axis.magnitude < 0.25f) { axisArmed = true; nextMove = 0; return; }
            if (!axisArmed || Time.unscaledTime < nextMove) return;
            if (selected is Slider slider && Mathf.Abs(axis.x) > 0.6f)
                slider.value += Mathf.Sign(axis.x) * (slider.maxValue - slider.minValue) * 0.1f;
            else if (Mathf.Abs(axis.y) > 0.6f)
                Select(choices[(choices.IndexOf(selected) + (axis.y > 0 ? choices.Count - 1 : 1)) % choices.Count]);
            else return;
            nextMove = Time.unscaledTime + 0.3f;
        }
        private void Select(Selectable item)
        {
            if (focusBorder) focusBorder.SetActive(false);
            highlighted = item;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(item ? item.gameObject : null);
            if (!item) return;
            Transform border = item.transform.Find("VR focus border");
            if (!border)
            {
                var owner = new GameObject("VR focus border", typeof(RectTransform));
                owner.transform.SetParent(item.transform, false);
                var rect = (RectTransform)owner.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                owner.AddComponent<LayoutElement>().ignoreLayout = true;
                for (int edge = 0; edge < 4; edge++)
                {
                    var segment = new GameObject("Edge", typeof(RectTransform), typeof(Image)); segment.transform.SetParent(owner.transform, false);
                    var r = (RectTransform)segment.transform;
                    r.anchorMin = edge == 0 ? new Vector2(0, 1) : edge == 3 ? new Vector2(1, 0) : Vector2.zero;
                    r.anchorMax = edge == 1 ? new Vector2(1, 0) : edge == 2 ? new Vector2(0, 1) : Vector2.one;
                    r.pivot = edge == 0 ? new Vector2(0.5f, 1) : edge == 1 ? new Vector2(0.5f, 0) : edge == 2 ? new Vector2(0, 0.5f) : new Vector2(1, 0.5f);
                    r.sizeDelta = edge < 2 ? new Vector2(0, 4) : new Vector2(4, 0); r.anchoredPosition = Vector2.zero;
                    var image = segment.GetComponent<Image>(); image.color = new Color(1, 0.55f, 0.02f, 1); image.raycastTarget = false;
                }
                border = owner.transform;
            }
            border.gameObject.SetActive(true); focusBorder = border.gameObject;
            ScrollRect scroll = item.GetComponentInParent<ScrollRect>();
            if (scroll && scroll.content && scroll.viewport && item.transform.IsChildOf(scroll.content))
            {
                Canvas.ForceUpdateCanvases();
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, item.transform);
                Rect viewport = scroll.viewport.rect;
                float offset = bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y :
                    bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y : 0;
                scroll.content.anchoredPosition += Vector2.up * offset;
                scroll.StopMovement();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>Shared look and parts for the title screen, pause menu, options and the Vibe book.</summary>
    public static class MenuKit
    {
        public static readonly Color Lime = new Color32(0xD4, 0xF0, 0x3C, 0xFF);   // the logo's highlighter green
        public static readonly Color Night = new Color32(0x0E, 0x12, 0x14, 0xF0);
        public static readonly Color Panel = new Color32(0x16, 0x1B, 0x1E, 0xF4);
        public static readonly Color Dim = new Color32(0x7E, 0x88, 0x80, 0xFF);

        /// <summary>A scrolling block of text (mouse wheel / PageUp / PageDown via ScrollBy).</summary>
        public static Text ScrollText(string name, Transform parent, Font font, int size, Color color, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, out ScrollRect scroll)
        {
            var viewport = UiKit.Box(name, parent, Color.clear, anchorMin, anchorMax, offsetMin, offsetMax);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.raycastTarget = true;
            var text = UiKit.Label("Body", viewport.transform, font, size, color, new Vector2(0, 1), Vector2.one);
            var rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 100);
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = rt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return text;
        }

        public static void ScrollBy(ScrollRect scroll, float pages)
        {
            if (scroll == null) return;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + pages);
        }

        public static bool Up(Keyboard kb) => kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
        public static bool Down(Keyboard kb) => kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
        public static bool Left(Keyboard kb) => kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;
        public static bool Right(Keyboard kb) => kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
        public static bool Confirm(Keyboard kb) => kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame;
        public static bool Back(Keyboard kb) => kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
    }

    /// <summary>
    /// A vertical list of menu rows: W/S or arrows choose, Enter / Space / E picks, A/D or arrows change a setting row,
    /// the mouse hovers and clicks. The owner calls Tick() from its Update while the list is showing.
    /// </summary>
    public class MenuList
    {
        public class Entry
        {
            public string label;
            public Func<string> value;      // a setting row: shown on the right, changed with Adjust
            public Action act;
            public Action<int> adjust;
            public bool enabled = true;
            public string note;             // shown under the list when this row is selected
            internal Image bg;
            internal Text text, valueText;
        }

        public readonly RectTransform Root;
        public int Index { get; private set; }
        public IReadOnlyList<Entry> Entries => entries;
        public Text Note { get; set; }

        /// <summary>Unselected rows (the Vibe book overrides these for its paper pages).</summary>
        public Color IdleRow = new Color(0, 0, 0, 0.35f);
        public Color IdleText = UiKit.Paper;

        private readonly List<Entry> entries = new List<Entry>();
        private readonly float rowHeight;
        private readonly int fontSize;
        private readonly TextAnchor align;

        public MenuList(Transform parent, Vector2 anchorMin, Vector2 anchorMax, float rowHeight = 30f, int fontSize = 18,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            Root = UiKit.Rect(new GameObject("Menu", typeof(RectTransform)), anchorMin, anchorMax);
            Root.SetParent(parent, false);
            Root.anchorMin = anchorMin;
            Root.anchorMax = anchorMax;
            Root.offsetMin = Root.offsetMax = Vector2.zero;
            this.rowHeight = rowHeight;
            this.fontSize = fontSize;
            this.align = align;
        }

        public void Clear()
        {
            foreach (var e in entries) if (e.bg != null) UnityEngine.Object.Destroy(e.bg.gameObject);
            entries.Clear();
            Index = 0;
        }

        public Entry Add(string label, Action act, bool enabled = true, string note = null)
        {
            var e = new Entry { label = label, act = act, enabled = enabled, note = note };
            Build(e);
            return e;
        }

        public Entry AddSetting(string label, Func<string> value, Action<int> adjust, string note = null)
        {
            var e = new Entry { label = label, value = value, adjust = adjust, act = () => adjust(1), note = note };
            Build(e);
            return e;
        }

        private void Build(Entry e)
        {
            int i = entries.Count;
            entries.Add(e);
            e.bg = UiKit.RoundBox("Row" + i, Root, Color.clear, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -(i + 1) * rowHeight + 2), new Vector2(0, -i * rowHeight - 2), 2.5f);
            e.bg.raycastTarget = true;
            e.text = UiKit.Label("Label", e.bg.transform, UiKit.UiFont, fontSize, UiKit.Paper, Vector2.zero, Vector2.one, align, FontStyle.Bold,
                new Vector2(12, 0), new Vector2(-12, 0));
            if (e.value != null)
                e.valueText = UiKit.Label("Value", e.bg.transform, UiKit.UiFont, fontSize, UiKit.Paper, new Vector2(0.5f, 0), Vector2.one,
                    TextAnchor.MiddleRight, FontStyle.Normal, new Vector2(0, 0), new Vector2(-12, 0));
            var trigger = e.bg.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Select(i));
            trigger.triggers.Add(enter);
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(data =>
            {
                Select(i);
                if (e.adjust != null && data is PointerEventData p) e.adjust(ClickDirection(e, p));
                else Activate();
                Refresh();
            });
            trigger.triggers.Add(click);
            Refresh();
        }

        /// <summary>
        /// A click on a setting: the ◀ half of its value goes down, anywhere else goes up (right-click always goes down).
        /// </summary>
        private static int ClickDirection(Entry e, PointerEventData p)
        {
            if (p.button == PointerEventData.InputButton.Right) return -1;
            if (e.valueText == null) return 1;
            var rt = e.bg.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, p.position, p.pressEventCamera, out var local)) return 1;
            float w = UiKit.TextWidth(e.valueText, e.valueText.text);
            float right = rt.rect.xMax - 12f, mid = right - w / 2f;
            return local.x >= right - w - 16f && local.x < mid ? -1 : 1;
        }

        public void Select(int i)
        {
            if (entries.Count == 0) return;
            Index = Mathf.Clamp(i, 0, entries.Count - 1);
            Refresh();
        }

        public void SelectFirstEnabled()
        {
            Index = Mathf.Max(0, entries.FindIndex(e => e.enabled));
            Refresh();
        }

        public void Activate()
        {
            if (Index < 0 || Index >= entries.Count) return;
            var e = entries[Index];
            if (!e.enabled) { Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Error, 0.4f); return; }
            Unlikely.Core.Sfx.Play(e.adjust != null ? Unlikely.Core.Sfx.Id.Move : Unlikely.Core.Sfx.Id.Confirm, 0.8f);
            e.act?.Invoke();
        }

        public void Refresh()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.bg == null) continue;
                bool sel = i == Index;
                e.bg.color = sel ? (e.enabled ? MenuKit.Lime : new Color(0.3f, 0.32f, 0.3f, 0.9f)) : IdleRow;
                Color c = sel ? (e.enabled ? new Color32(0x10, 0x12, 0x10, 0xFF) : UiKit.Muted) : (e.enabled ? IdleText : MenuKit.Dim);
                e.text.color = c;
                e.text.text = (sel ? ">_ " : "   ") + e.label;
                if (e.valueText != null)
                {
                    e.valueText.color = c;
                    e.valueText.text = "◀  " + e.value() + "  ▶";
                }
            }
            if (Note != null) Note.text = Index >= 0 && Index < entries.Count ? entries[Index].note ?? "" : "";
        }

        /// <summary>Keyboard: move, adjust, pick. Returns true if something was picked this frame.</summary>
        public bool Tick()
        {
            var kb = Keyboard.current;
            if (kb == null || entries.Count == 0) return false;
            if (MenuKit.Up(kb)) Move(-1);
            if (MenuKit.Down(kb)) Move(1);
            var e = entries[Index];
            if (e.adjust != null)
            {
                if (MenuKit.Left(kb)) { e.adjust(-1); Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move, 0.7f); Refresh(); }
                if (MenuKit.Right(kb)) { e.adjust(1); Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move, 0.7f); Refresh(); }
            }
            if (MenuKit.Confirm(kb) && e.enabled) { Activate(); Refresh(); return true; }
            return false;
        }

        private void Move(int d)
        {
            for (int step = 0; step < entries.Count; step++)
            {
                Index = (Index + d + entries.Count) % entries.Count;
                if (entries[Index].enabled) break;
            }
            Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move, 0.7f);
            Refresh();
        }
    }
}

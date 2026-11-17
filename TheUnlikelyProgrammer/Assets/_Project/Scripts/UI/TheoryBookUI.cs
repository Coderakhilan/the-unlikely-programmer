using System;
using Unlikely.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Vibe only: "The Theory of Everything", written by future Donnie. B opens it any time Donnie is free;
    /// W/S chooses a chapter, PageUp/PageDown or the wheel scrolls, B or Esc closes.
    /// The text lives in Data/Guide/theory_of_everything.json (edit it there).
    /// </summary>
    public class TheoryBookUI : MonoBehaviour
    {
        public const string ItemId = Inventory.Permanent;
        public const string ItemName = "The Theory of Everything (future Donnie)";

        [Serializable] public class Chapter { public string title; public string[] lines; }
        [Serializable] public class Book { public string title; public string subtitle; public Chapter[] chapters; }

        public static TheoryBookUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        public Book Content => book;
        public int Current { get; private set; }

        [SerializeField] private TextAsset json;
        private Book book;
        private GameObject root;
        private MenuList chapters;
        private Text heading, body;
        private ScrollRect scroll;
        private float openedAt;

        public void Configure(TextAsset guide) => json = guide;

        private void Awake()
        {
            Instance = this;
            book = Parse(json != null ? json.text : null);
            var canvas = UiKit.MakeCanvas(transform, "TheoryCanvas", 300);
            var dim = UiKit.Box("Theory", canvas.transform, new Color(0, 0, 0, 0.75f), Vector2.zero, Vector2.one);
            root = dim.gameObject;
            dim.raycastTarget = true;

            // a book: dark cover, paper pages
            var cover = UiKit.Framed("Cover", dim.transform, new Color32(0x2A, 0x22, 0x1A, 0xFF), new Color32(0x14, 0x10, 0x0C, 0xFF),
                new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero, 4f);
            var left = UiKit.Box("Contents", cover.transform, new Color32(0xE4, 0xDC, 0xC6, 0xFF), new Vector2(0, 0), new Vector2(0.32f, 1), new Vector2(10, 10), new Vector2(-3, -10));
            var right = UiKit.Box("Page", cover.transform, UiKit.Paper, new Vector2(0.32f, 0), new Vector2(1, 1), new Vector2(3, 10), new Vector2(-10, -10));

            var ink = new Color32(0x24, 0x22, 0x20, 0xFF);
            UiKit.Label("BookTitle", left.transform, UiKit.UiFont, 17, ink, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.UpperLeft, FontStyle.Bold,
                new Vector2(14, -52), new Vector2(-10, -10)).text = book.title;
            UiKit.Label("Subtitle", left.transform, UiKit.UiFont, 10, new Color32(0x6A, 0x60, 0x50, 0xFF), new Vector2(0, 1), new Vector2(1, 1), TextAnchor.UpperLeft,
                FontStyle.Italic, new Vector2(14, -72), new Vector2(-10, -50)).text = book.subtitle;

            chapters = new MenuList(left.transform, Vector2.zero, Vector2.one, 22f, 11);
            chapters.IdleRow = new Color(0, 0, 0, 0.05f);
            chapters.IdleText = new Color32(0x3A, 0x34, 0x2C, 0xFF);
            chapters.Root.offsetMin = new Vector2(6, 30);
            chapters.Root.offsetMax = new Vector2(-6, -80);
            for (int i = 0; i < book.chapters.Length; i++)
            {
                int c = i;
                chapters.Add(book.chapters[i].title, () => Show(c));
            }
            UiKit.Label("Keys", left.transform, UiKit.UiFont, 10, new Color32(0x6A, 0x60, 0x50, 0xFF), new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleLeft,
                FontStyle.Normal, new Vector2(14, 6), new Vector2(-6, 26)).text = "W/S chapter   PgUp/PgDn scroll   B/Esc close";

            heading = UiKit.Label("Heading", right.transform, UiKit.UiFont, 20, ink, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(20, -46), new Vector2(-20, -10));
            body = MenuKit.ScrollText("Text", right.transform, UiKit.UiFont, 13, ink, Vector2.zero, Vector2.one, new Vector2(20, 14), new Vector2(-16, -52), out scroll);
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static Book Parse(string text)
        {
            Book b = null;
            try { if (!string.IsNullOrEmpty(text)) b = JsonUtility.FromJson<Book>(text); } catch (Exception e) { Debug.LogWarning("Theory book: " + e.Message); }
            if (b == null || b.chapters == null || b.chapters.Length == 0)
                b = new Book { title = "The Theory of Everything", subtitle = "", chapters = new[] { new Chapter { title = "(empty)", lines = new[] { "The pages are blank." } } } };
            return b;
        }

        public void Open()
        {
            if (IsOpen) return;
            root.SetActive(true);
            openedAt = Time.unscaledTime;
            Show(Current);
        }

        public void Close() => root.SetActive(false);

        public void Show(int chapter)
        {
            Current = Mathf.Clamp(chapter, 0, book.chapters.Length - 1);
            chapters.Select(Current);
            var c = book.chapters[Current];
            heading.text = c.title;
            body.text = c.lines != null ? string.Join("\n", c.lines) : "";
            scroll.verticalNormalizedPosition = 1f;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!IsOpen)
            {
                // B opens it whenever Donnie has it and nothing else owns the keyboard
                if (kb.bKey.wasPressedThisFrame && Inventory.Has(ItemId) && !UiKit.AnyModalOpen
                    && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen))
                    Open();
                return;
            }
            if (Time.unscaledTime - openedAt > 0.05f && (kb.escapeKey.wasPressedThisFrame || kb.bKey.wasPressedThisFrame)) { Close(); return; }
            int before = chapters.Index;
            chapters.Tick();
            if (chapters.Index != before) Show(chapters.Index);
            if (kb.pageDownKey.wasPressedThisFrame) MenuKit.ScrollBy(scroll, -0.3f);
            if (kb.pageUpKey.wasPressedThisFrame) MenuKit.ScrollBy(scroll, 0.3f);
        }
    }
}

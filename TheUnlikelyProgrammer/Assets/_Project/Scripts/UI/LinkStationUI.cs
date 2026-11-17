using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Floor 3's link station (Executive office A): a map of the floor's five computers. Each computer holds one table;
    /// pointing it at another computer lets it read that one's table too (a line with an arrow: "reads from").
    /// Once the key map is printed, the columns that point at another table are marked, with where they point.
    ///   ↑↓ / W S / click: choose a computer     ←→ / A D / the ‹ › buttons: change what it reads from
    ///   Space / Enter / DECRYPT: decrypt with a keycard       Esc / Tab / CLOSE: log off
    /// </summary>
    public class LinkStationUI : MonoBehaviour
    {
        public sealed class Node
        {
            public string id, name, place, table, task;
            public string[] columns;
            public Vector2 pos;           // centre on the map panel, reference px (map panel is 600 x 400)
        }

        public static LinkStationUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        private static readonly Color Screen = new Color32(0x0E, 0x14, 0x12, 0xFF);
        private static readonly Color Green = new Color32(0x7C, 0xE0, 0x9A, 0xFF);
        private static readonly Color Dim = new Color32(0x3E, 0x6E, 0x50, 0xFF);
        private static readonly Color NodeFill = new Color32(0x14, 0x22, 0x1B, 0xFF);
        private static readonly Color Sel = new Color32(0xFF, 0xC8, 0x5A, 0xFF);

        private GameObject root;
        private RectTransform map;
        private Text detail, keymapText, statusText;
        private readonly List<GameObject> drawn = new List<GameObject>();

        private Node[] nodes;
        private Func<string, string> getLink;
        private Action<string, string> setLink;
        private Func<Dictionary<string, string>> keyMap;      // "table.column" -> "table.column"; null/empty until printed
        private Func<string, bool> taskSolved;
        private Func<string> decryptStatus;
        private Action onDecrypt, onClose;
        private int selected;
        private int openedFrame;

        private void Awake()
        {
            Instance = this;
            Build();
            root.SetActive(false);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Open(Node[] machines, Func<string, string> linkOf, Action<string, string> link, Func<Dictionary<string, string>> keys,
            Func<string, bool> solved, Func<string> decryptLine, Action decrypt, Action closed)
        {
            nodes = machines;
            getLink = linkOf;
            setLink = link;
            keyMap = keys;
            taskSolved = solved;
            decryptStatus = decryptLine;
            onDecrypt = decrypt;
            onClose = closed;
            selected = Mathf.Clamp(selected, 0, nodes.Length - 1);
            openedFrame = Time.frameCount;
            UiKit.EnsureEventSystem();
            root.SetActive(true);
            Redraw();
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            var c = onClose;
            onClose = null;
            c?.Invoke();
        }

        /// <summary>Select a computer (by index) (tests / clicks).</summary>
        public void Select(int i) { selected = (i + nodes.Length) % nodes.Length; Redraw(); }

        /// <summary>Step the selected computer's link through the others, then "nothing".</summary>
        public void Cycle(int dir)
        {
            string from = nodes[selected].id;
            var options = new List<string>();
            foreach (var n in nodes) if (n.id != from) options.Add(n.id);
            options.Add(null);
            int at = options.IndexOf(getLink(from));
            setLink(from, options[(at + dir + options.Count) % options.Count]);
            Redraw();
        }

        public void Decrypt()
        {
            var d = onDecrypt;
            onDecrypt = null;
            Close();
            d?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) { Close(); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select(selected - 1);
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select(selected + 1);
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Cycle(-1);
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Cycle(1);
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Decrypt();
        }

        // ------------------------------------------------------------------ drawing

        private Node Find(string id) => id == null ? null : Array.Find(nodes, n => n.id == id);

        private void Redraw()
        {
            foreach (var g in drawn) Destroy(g);
            drawn.Clear();
            var keys = keyMap?.Invoke();
            bool haveKeys = keys != null && keys.Count > 0;

            // the lines first, so the boxes sit on top of them
            for (int i = 0; i < nodes.Length; i++)
            {
                var to = Find(getLink(nodes[i].id));
                if (to == null) continue;
                bool sel = i == selected;
                Arrow(nodes[i].pos, to.pos, sel ? Sel : Dim, sel ? 4f : 2f);
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                bool sel = i == selected;
                bool readByMe = Find(getLink(nodes[selected].id)) == n;
                var border = sel ? Sel : readByMe ? Green : Dim;
                var box = UiKit.Framed("Node_" + n.id, map, NodeFill, border, new Vector2(0, 0), new Vector2(0, 0),
                    n.pos - new Vector2(88, 60), n.pos + new Vector2(88, 60), sel ? 3f : 2f);
                drawn.Add(box.transform.parent.gameObject);
                var btn = box.transform.parent.gameObject.AddComponent<Button>();
                box.transform.parent.GetComponent<Image>().raycastTarget = true;
                int idx = i;
                btn.onClick.AddListener(() => Select(idx));

                var sb = new System.Text.StringBuilder();
                sb.Append("<b>").Append(n.name).Append("</b>\n<size=9>").Append(n.place).Append("</size>\n");
                sb.Append("<color=#FFC85A>table: <b>").Append(n.table).Append("</b></color>\n<size=10>");
                for (int c = 0; c < n.columns.Length; c++)
                {
                    string col = n.columns[c];
                    string key = n.table + "." + col;
                    // a column that points at another table: "holder  → staff"
                    if (haveKeys && keys.TryGetValue(key, out var target))
                        sb.Append("<color=#FFC85A>  ").Append(col).Append("  → ").Append(target.Split('.')[0]).Append("</color>");
                    else sb.Append("  ").Append(col);
                    if (c < n.columns.Length - 1) sb.Append("\n");
                }
                sb.Append("</size>");
                var t = UiKit.Label("Text", box.transform, UiKit.Mono, 10, Green, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal, new Vector2(6, 4), new Vector2(-6, -4));
                t.text = sb.ToString();
                if (!string.IsNullOrEmpty(n.task))
                {
                    bool done = taskSolved != null && taskSolved(n.id);
                    var tag = UiKit.Label("Task", box.transform, UiKit.UiFont, 11, done ? Green : Sel, new Vector2(1, 0), new Vector2(1, 0), TextAnchor.LowerRight, FontStyle.Bold,
                        new Vector2(-90, 3), new Vector2(-5, 20));
                    tag.text = done ? n.task + " ✓" : n.task;
                }
            }

            // the selected computer, in words
            var s = nodes[selected];
            var reads = Find(getLink(s.id));
            var d = new System.Text.StringBuilder();
            d.Append("<color=#FFC85A><b>").Append(s.name).Append("</b></color>\n").Append(s.place).Append("\n\n");
            d.Append("Holds the table <b>").Append(s.table).Append("</b>.\n");
            if (reads == null) d.Append("Reads from: <b>nothing</b>.\n\nQueries on it can only use <b>").Append(s.table).Append("</b>.");
            else d.Append("Reads from: <b>").Append(reads.name).Append("</b>  (table ").Append(reads.table).Append(").\n\nQueries on it can use <b>")
                  .Append(s.table).Append("</b> and <b>").Append(reads.table).Append("</b>, so they can JOIN them.");
            if (!string.IsNullOrEmpty(s.task)) d.Append("\n\n").Append(s.task).Append(" is on this computer (KODEX shows it when you use it).");
            detail.text = d.ToString();

            if (haveKeys)
            {
                var k = new System.Text.StringBuilder("<b>KEY MAP</b>  (printed)\n");
                foreach (var kv in keys) k.Append(kv.Key).Append("  →  ").Append(kv.Value).Append("\n");
                keymapText.text = k.ToString();
            }
            else keymapText.text = "<b>KEY MAP</b>\n<i>Not printed. Which column points at which table? Unknown.</i>";
            statusText.text = decryptStatus != null ? decryptStatus() : "";
        }

        /// <summary>A line from a to b (map px) with a square "arrowhead" where it arrives at b's box.</summary>
        private void Arrow(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 dir = (b - a).normalized;
            // stop at the edge of the target box (176 x 104)
            float tx = Mathf.Abs(dir.x) > 0.001f ? 92f / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.001f ? 64f / Mathf.Abs(dir.y) : float.MaxValue;
            Vector2 end = b - dir * Mathf.Min(tx, ty);
            float len = Vector2.Distance(a, end);
            var line = UiKit.Box("Link", map, color, Vector2.zero, Vector2.zero);
            var rt = line.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(len, width);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            drawn.Add(line.gameObject);
            var head = UiKit.Box("Head", map, color, Vector2.zero, Vector2.zero);
            head.rectTransform.sizeDelta = new Vector2(width * 3.5f, width * 3.5f);
            head.rectTransform.anchoredPosition = end;
            head.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            drawn.Add(head.gameObject);
            var lbl = UiKit.Label("Reads", map, UiKit.UiFont, 9, color, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
            lbl.rectTransform.sizeDelta = new Vector2(70, 14);
            lbl.rectTransform.anchoredPosition = (a + end) / 2f + new Vector2(0, 8);
            lbl.text = "reads";
            drawn.Add(lbl.gameObject);
        }

        private Button MakeButton(Transform parent, string label, Vector2 min, Vector2 max, Action click)
        {
            var img = UiKit.Framed("Button_" + label, parent, NodeFill, Green, new Vector2(0, 0), new Vector2(0, 0), min, max);
            var go = img.transform.parent.gameObject;
            go.GetComponent<Image>().raycastTarget = true;
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(() => click());
            UiKit.Label("Label", img.transform, UiKit.UiFont, 13, Green, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold).text = label;
            return b;
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "LinkStationCanvas", 120);
            var bg = UiKit.Box("LinkStation", canvas.transform, new Color(0, 0, 0, 0.8f), Vector2.zero, Vector2.one);
            root = bg.gameObject;
            var screen = UiKit.Framed("Screen", bg.transform, Screen, Dim, Vector2.zero, Vector2.one, new Vector2(16, 14), new Vector2(-16, -14), 3f);
            UiKit.Scanlines(screen.transform, 0.08f);

            var title = UiKit.Label("Title", screen.transform, UiKit.Mono, 16, Green, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(14, -30), new Vector2(-14, -6));
            title.text = "LINK STATION  ::  EXECUTIVE NETWORK";

            // the map: computers laid out roughly where they are on the floor
            var mapImg = UiKit.Framed("Map", screen.transform, new Color32(0x0A, 0x10, 0x0D, 0xFF), Dim, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(12, 40), new Vector2(612, 470));
            map = mapImg.rectTransform;
            var hint = UiKit.Label("MapHint", mapImg.transform, UiKit.UiFont, 10, Dim, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.LowerCenter, FontStyle.Italic,
                new Vector2(4, 2), new Vector2(-4, 16));
            hint.text = "Each computer holds ONE table. An arrow = \"reads from\": that computer can also use the other one's table.";

            // the side panel
            var side = UiKit.Framed("Side", screen.transform, new Color32(0x0A, 0x10, 0x0D, 0xFF), Dim, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(622, 40), new Vector2(-12, -40));
            detail = UiKit.Label("Detail", side.transform, UiKit.UiFont, 12, Green, new Vector2(0, 0.52f), new Vector2(1, 1), TextAnchor.UpperLeft, FontStyle.Normal,
                new Vector2(8, 0), new Vector2(-8, -8));
            MakeButton(side.transform, "‹", new Vector2(8, 196), new Vector2(48, 222), () => Cycle(-1));
            MakeButton(side.transform, "change link", new Vector2(52, 196), new Vector2(232, 222), () => Cycle(1));
            MakeButton(side.transform, "›", new Vector2(236, 196), new Vector2(276, 222), () => Cycle(1));
            keymapText = UiKit.Label("KeyMap", side.transform, UiKit.Mono, 10, Green, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.UpperLeft, FontStyle.Normal,
                new Vector2(8, 66), new Vector2(-8, 190));
            statusText = UiKit.Label("Decrypt", side.transform, UiKit.UiFont, 11, Sel, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.UpperLeft, FontStyle.Italic,
                new Vector2(8, 40), new Vector2(-8, 64));
            MakeButton(side.transform, "DECRYPT", new Vector2(8, 8), new Vector2(150, 36), Decrypt);
            MakeButton(side.transform, "CLOSE", new Vector2(156, 8), new Vector2(276, 36), Close);

            var keysHelp = UiKit.Label("Keys", screen.transform, UiKit.UiFont, 11, Dim, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleLeft, FontStyle.Normal,
                new Vector2(14, 8), new Vector2(-14, 34));
            keysHelp.text = "↑↓  choose computer      ←→  change what it reads from      Space  decrypt      Esc  log off      (or click)";
        }
    }
}

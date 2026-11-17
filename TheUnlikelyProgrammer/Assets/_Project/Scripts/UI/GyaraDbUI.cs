using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unlikely.SQL;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// The server room console: GyaraDB master control. Every ending runs through it.
    ///   Upload mode: one button (Enter): push the phone's malware into the servers, watch the racks cook.
    ///   Query mode: a SQL prompt over the master database. The story decides which statements Donnie will type
    ///   (refuse), what the status line says after each one (after), and when he's finished (finished).
    /// Enter runs (Shift+Enter: new line), Esc closes (when allowed).
    /// </summary>
    public class GyaraDbUI : MonoBehaviour
    {
        public enum Mode { Upload, Query }

        public static GyaraDbUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        public bool Uploading { get; private set; }
        public Database Db { get; private set; }
        public string Status => statusText != null ? statusText.text : "";

        private static readonly Color Screen = new Color32(0x0B, 0x0D, 0x12, 0xFF);
        private static readonly Color Panel = new Color32(0x12, 0x16, 0x1E, 0xFF);
        private static readonly Color Red = new Color32(0xE0, 0x4A, 0x3A, 0xFF);
        private static readonly Color Teal = new Color32(0x6F, 0xD6, 0xC8, 0xFF);
        private static readonly Color Dim = new Color32(0x5A, 0x63, 0x70, 0xFF);
        private static readonly Color Text0 = new Color32(0xDC, 0xE3, 0xEA, 0xFF);

        private GameObject root, queryLayer, uploadLayer;
        private Text headerText, tablesText, resultText, statusText, helpText, logText, rackText;
        private Image barFill;
        private InputField input;
        private bool reactivate;

        private Mode mode;
        private bool canClose;
        private Func<string, string> refuse, after;
        private Func<bool> finished;
        private Action onDone, onClosed;
        private int openedFrame;

        private void Awake()
        {
            Instance = this;
            Build();
            root.SetActive(false);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>The malware upload. onDone once the servers are cooked.</summary>
        public void OpenUpload(string user, Database db, Action done)
        {
            Begin(Mode.Upload, user, db, false, done, null);
            queryLayer.SetActive(false);
            uploadLayer.SetActive(true);
            logText.text = "> PHONE CONNECTED (USB-C)\n> FILE FOUND: thermal_patch.bin  (1.2 MB, unsigned)\n> Press ENTER to upload it to every rack.";
            rackText.text = Racks(0f);
            barFill.rectTransform.anchorMax = new Vector2(0, 1);
            SetStatus("Waiting. One key and this is over.", Teal);
            helpText.text = "Enter  upload";
        }

        /// <summary>
        /// A SQL prompt over db. refuse(query): a line Donnie says instead of running it (null = run it).
        /// after(query): the status line once it ran. finished(): checked after each statement; true closes and calls done.
        /// </summary>
        public void OpenQuery(string user, Database db, Func<string, string> refuseQuery, Func<string, string> afterQuery, Func<bool> isFinished,
            bool closable, string firstStatus, Action done, Action closed = null)
        {
            refuse = refuseQuery;
            after = afterQuery;
            finished = isFinished;
            Begin(Mode.Query, user, db, closable, done, closed);
            queryLayer.SetActive(true);
            uploadLayer.SetActive(false);
            resultText.text = "";
            input.text = "";
            SetStatus(firstStatus ?? "", Teal);
            helpText.text = closable ? "Enter  run      Shift+Enter  new line      Esc  step away" : "Enter  run      Shift+Enter  new line";
            reactivate = true;
        }

        private void Begin(Mode m, string user, Database db, bool closable, Action done, Action closed)
        {
            mode = m;
            Db = db;
            canClose = closable;
            onDone = done;
            onClosed = closed;
            Uploading = false;
            openedFrame = Time.frameCount;
            headerText.text = "GYARADB  ·  MASTER CONTROL        <color=#5A6370>user: " + user + "   ·   node: SRV-ROOM-01</color>";
            RefreshTables();
            UiKit.EnsureEventSystem();
            root.SetActive(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            if (input != null) input.DeactivateInputField();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var c = onClosed;
            onClosed = null;
            c?.Invoke();
        }

        private void Finish()
        {
            root.SetActive(false);
            if (input != null) input.DeactivateInputField();
            var d = onDone;
            onDone = null;
            onClosed = null;
            d?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            if (kb == null || Time.frameCount == openedFrame) return;
            if (kb.escapeKey.wasPressedThisFrame && canClose && !Uploading) { Close(); return; }
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            if (mode == Mode.Upload)
            {
                if (enter) StartUpload();
                return;
            }
            if (reactivate) { reactivate = false; input.Select(); input.ActivateInputField(); }
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (enter && !shift && input.isFocused)
            {
                Submit(input.text);
                reactivate = true;
            }
        }

        // ------------------------------------------------------------------ query mode

        /// <summary>Run a statement as if typed (public for tests). Returns the status line.</summary>
        public string Submit(string sql)
        {
            if (!IsOpen || mode != Mode.Query) return null;
            string q = (sql ?? "").Trim().TrimEnd(';').Trim();
            if (q.Length == 0) return null;
            string no = refuse?.Invoke(q);
            if (no != null)
            {
                SetStatus(no, Red);
                return no;
            }
            try
            {
                var result = SqlEngine.Execute(Db, q);
                resultText.text = Format(result);
                RefreshTables();
                string line = after?.Invoke(q) ?? $"{result.Rows.Count} row{(result.Rows.Count == 1 ? "" : "s")}.";
                SetStatus(line, Teal);
                input.text = "";
                if (finished != null && finished()) StartCoroutine(FinishSoon());
                return line;
            }
            catch (SqlException e)
            {
                SetStatus("ERROR  " + e.Message, Red);
                return e.Message;
            }
        }

        private IEnumerator FinishSoon()
        {
            input.DeactivateInputField();
            yield return new WaitForSecondsRealtime(1.2f);
            if (IsOpen) Finish();
        }

        private void RefreshTables()
        {
            var sb = new StringBuilder();
            if (Db != null)
                foreach (var t in Db.Tables)
                {
                    sb.Append(t.Rows.Count == 0 ? "<color=#5A6370>" : "<color=#DCE3EA>").Append(t.Name).Append("</color>\n");
                    sb.Append("<color=#5A6370>  ").Append(t.Rows.Count).Append(" rows</color>\n");
                    sb.Append("<size=10><color=#5A6370>  ").Append(string.Join(", ", t.Columns)).Append("</color></size>\n\n");
                }
            tablesText.text = sb.ToString();
        }

        private static string Format(QueryResult r)
        {
            const int maxRows = 14, maxWidth = 46;
            var widths = new int[r.Columns.Length];
            for (int c = 0; c < widths.Length; c++) widths[c] = r.Columns[c].Length;
            int shown = Mathf.Min(r.Rows.Count, maxRows);
            for (int i = 0; i < shown; i++)
                for (int c = 0; c < widths.Length; c++)
                    widths[c] = Mathf.Min(maxWidth, Mathf.Max(widths[c], QueryResult.Format(r.Rows[i][c]).Length));
            var sb = new StringBuilder();
            string Cell(string s, int w) => (s.Length > w ? s.Substring(0, w - 1) + "…" : s).PadRight(w + 2);
            for (int c = 0; c < widths.Length; c++) sb.Append(Cell(r.Columns[c], widths[c]));
            sb.Append('\n');
            for (int c = 0; c < widths.Length; c++) sb.Append(new string('-', widths[c])).Append("  ");
            sb.Append('\n');
            for (int i = 0; i < shown; i++)
            {
                for (int c = 0; c < widths.Length; c++) sb.Append(Cell(QueryResult.Format(r.Rows[i][c]), widths[c]));
                sb.Append('\n');
            }
            if (r.Rows.Count == 0) sb.Append("(empty)\n");
            if (r.Rows.Count > shown) sb.Append($"... {r.Rows.Count - shown} more\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ upload mode

        /// <summary>Start the malware upload (Enter; public for tests).</summary>
        public void StartUpload()
        {
            if (!IsOpen || mode != Mode.Upload || Uploading) return;
            StartCoroutine(Upload());
        }

        private IEnumerator Upload()
        {
            Uploading = true;
            helpText.text = "";
            var sb = new StringBuilder(logText.text).Append('\n');
            string[] steps =
            {
                "> UPLOADING thermal_patch.bin ...", "> rack 01: fan curve overwritten", "> rack 02: fan curve overwritten",
                "> rack 03: fan curve overwritten", "> cooling: OFFLINE (by request)", "> WARNING: rack temperature rising",
                "> WARNING: WARNING: WARNING:", "> GYARADB: node SRV-ROOM-01 not responding",
            };
            const float total = 6f;
            int next = 0;
            for (float t = 0; t < total; t += Time.unscaledDeltaTime)
            {
                float k = t / total;
                barFill.rectTransform.anchorMax = new Vector2(k, 1);
                while (next < steps.Length && k >= next / (float)steps.Length)
                {
                    sb.Append(steps[next++]).Append('\n');
                    logText.text = sb.ToString();
                }
                rackText.text = Racks(k);
                SetStatus(k < 0.6f ? $"Uploading... {Mathf.RoundToInt(k * 100)}%" : "THERMAL ALARM", k < 0.6f ? Teal : Red);
                yield return null;
            }
            barFill.rectTransform.anchorMax = Vector2.one;
            rackText.text = Racks(1f);
            sb.Append("> CRITICAL. CRITICAL. CRIT");
            logText.text = sb.ToString();
            SetStatus("Every rack in the room is screaming.", Red);
            yield return new WaitForSecondsRealtime(1.4f);
            Uploading = false;
            Finish();
        }

        private static string Racks(float k)
        {
            var sb = new StringBuilder();
            float[] start = { 21f, 23f, 22f };
            for (int i = 0; i < 3; i++)
            {
                float temp = Mathf.Lerp(start[i], 96f + i * 7f, k * k);
                string col = temp > 75 ? "#E04A3A" : temp > 45 ? "#FFC85A" : "#6FD6C8";
                sb.Append($"RACK 0{i + 1}   <color={col}>{temp:0}°C</color>\n");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ building

        private void SetStatus(string text, Color c)
        {
            statusText.color = c;
            statusText.text = text;
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "GyaraDbCanvas", 240);
            var bg = UiKit.Box("GyaraDB", canvas.transform, new Color(0, 0, 0, 0.9f), Vector2.zero, Vector2.one);
            root = bg.gameObject;
            var screen = UiKit.Framed("Screen", bg.transform, Screen, Red, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero, 2f);
            var s = screen.transform;
            UiKit.Box("HeaderBar", s, Panel, new Vector2(0, 1), Vector2.one, new Vector2(0, -34), Vector2.zero);
            headerText = UiKit.Label("Header", s, UiKit.UiFont, 15, Red, new Vector2(0, 1), Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(14, -34), new Vector2(-14, 0));

            // tables, live row counts
            var side = UiKit.Box("Tables", s, Panel, new Vector2(0, 0.1f), new Vector2(0.24f, 1), new Vector2(10, 0), new Vector2(0, -44));
            UiKit.Label("Title", side.transform, UiKit.UiFont, 11, Dim, new Vector2(0, 1), Vector2.one, TextAnchor.UpperLeft, FontStyle.Bold,
                new Vector2(10, -22), new Vector2(-6, -6)).text = "TABLES";
            tablesText = UiKit.Label("List", side.transform, UiKit.Mono, 13, Text0, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal,
                new Vector2(10, 6), new Vector2(-6, -28));

            // query layer
            var q = UiKit.Box("Query", s, Color.clear, new Vector2(0.25f, 0.1f), new Vector2(1, 1), new Vector2(0, 0), new Vector2(-10, -44));
            queryLayer = q.gameObject;
            var field = UiKit.Framed("QueryBox", q.transform, Color.black, Dim, new Vector2(0, 0.74f), Vector2.one);
            field.raycastTarget = true;
            var inputText = UiKit.Label("Text", field.transform, UiKit.Mono, 16, Text0, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal,
                new Vector2(10, 6), new Vector2(-10, -6));
            inputText.supportRichText = false;
            var ph = UiKit.Label("Placeholder", field.transform, UiKit.Mono, 16, Dim, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Italic,
                new Vector2(10, 6), new Vector2(-10, -6));
            ph.text = "gyaradb> _";
            input = field.gameObject.AddComponent<InputField>();
            input.textComponent = inputText;
            input.placeholder = ph;
            input.targetGraphic = field;
            input.lineType = InputField.LineType.MultiLineNewline;
            input.onValidateInput = (text, index, c) =>
            {
                if (c != '\n' && c != '\r') return c == '\t' ? '\0' : c;
                var k = Keyboard.current;
                return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed) ? '\n' : '\0';
            };
            resultText = UiKit.Label("Results", q.transform, UiKit.Mono, 13, Text0, Vector2.zero, new Vector2(1, 0.72f), TextAnchor.UpperLeft, FontStyle.Normal,
                new Vector2(4, 0), new Vector2(0, -4));
            resultText.horizontalOverflow = HorizontalWrapMode.Overflow;
            resultText.supportRichText = false;

            // upload layer
            var u = UiKit.Box("Upload", s, Color.clear, new Vector2(0.25f, 0.1f), new Vector2(1, 1), new Vector2(0, 0), new Vector2(-10, -44));
            uploadLayer = u.gameObject;
            logText = UiKit.Label("Log", u.transform, UiKit.Mono, 15, Teal, new Vector2(0, 0.3f), new Vector2(0.68f, 1), TextAnchor.UpperLeft);
            rackText = UiKit.Label("Racks", u.transform, UiKit.Mono, 18, Text0, new Vector2(0.7f, 0.5f), new Vector2(1, 1), TextAnchor.UpperLeft, FontStyle.Bold);
            var bar = UiKit.Framed("Bar", u.transform, Color.black, Dim, new Vector2(0, 0.12f), new Vector2(1, 0.2f));
            barFill = UiKit.Box("Fill", bar.transform, Red, Vector2.zero, new Vector2(0, 1));
            uploadLayer.SetActive(false);

            statusText = UiKit.Label("Status", s, UiKit.UiFont, 15, Teal, new Vector2(0, 0.035f), new Vector2(1, 0.1f), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(14, 0), new Vector2(-14, 0));
            helpText = UiKit.Label("Help", s, UiKit.UiFont, 11, Dim, Vector2.zero, new Vector2(1, 0.035f), TextAnchor.MiddleLeft, FontStyle.Normal,
                new Vector2(14, 2), new Vector2(-14, 0));
            UiKit.Scanlines(s, 0.12f);
        }
    }
}

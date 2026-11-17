using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unlikely.Core;
using Unlikely.Puzzles;
using Unlikely.SQL;
using Unlikely.Story;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    public enum ComputerSkin
    {
        /// <summary>Black employee PC: a modern desktop with a database app (mission puzzles).</summary>
        EmployeePc,
        /// <summary>Beige employee terminal: green-screen CRT; the phone plugs in, hacks clearance, then teaches.</summary>
        EmployeeTerminal,
    }

    /// <summary>
    /// Full-screen computer. The screen itself is only what the computer would show: its tables, a query box
    /// and results. What Donnie is supposed to find lives on his phone (Manual app, docked on the right):
    /// the TASK tab holds the question and Manual's comments, the KNOWLEDGE tab what he's learned (Unlikely only).
    /// Before Manual exists, a sticky note on the monitor can carry hints (Dan's notes).
    /// </summary>
    public class TerminalUI : MonoBehaviour
    {
        private const int MaxShownRows = 12;

        private sealed class Theme
        {
            public Color text, dim, accent, ok, error, field, button, buttonText;
        }

        private static readonly Theme PcTheme = new Theme
        {
            text = new Color32(0xE6, 0xEA, 0xF2, 0xFF), dim = new Color32(0x8A, 0x95, 0xAB, 0xFF), accent = new Color32(0x5A, 0xA9, 0xFF, 0xFF),
            ok = new Color32(0x66, 0xD1, 0x7A, 0xFF), error = new Color32(0xFF, 0x7A, 0x6A, 0xFF), field = new Color32(0x10, 0x16, 0x23, 0xFF),
            button = new Color32(0x2F, 0x6F, 0xD6, 0xFF), buttonText = Color.white,
        };

        private static readonly Theme CrtTheme = new Theme
        {
            text = new Color32(0x8C, 0xFF, 0x9A, 0xFF), dim = new Color32(0x4F, 0xA3, 0x5A, 0xFF), accent = new Color32(0xC8, 0xFF, 0x7A, 0xFF),
            ok = new Color32(0xE8, 0xFF, 0xB0, 0xFF), error = new Color32(0xFF, 0x9A, 0x6A, 0xFF), field = new Color32(0x06, 0x0C, 0x07, 0xFF),
            button = new Color32(0x2C, 0x5E, 0x33, 0xFF), buttonText = new Color32(0xC8, 0xFF, 0xD0, 0xFF),
        };

        public static TerminalUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>The floor terminal's KODEX lessons are on screen (no time limit on those, even on Likely / Leet).</summary>
        public bool InLessons => IsOpen && sequence != null;

        // ---- layout pieces
        private GameObject root, pcLayer, crtLayer, phone, note, bootLayer, knowledgeTab;
        private RectTransform pcWindow, crtArea, crtScreen, content;
        private RawImage scanlines;
        private Text pcClock, crtPort, headerText, tablesText, resultText, statusText, helpText, inputText, placeholder, runLabel, noteText;
        private Text taskTabLabel, knowledgeTabLabel, phoneBody, bootText;
        private Image fieldBg, runBg, taskTabBg, knowledgeTabBg, bootFill;
        private InputField input;
        private ScrollRect phoneScroll;

        // ---- state
        private Theme theme;
        private ComputerSkin skin;
        private PuzzleData puzzle;
        private Database db;
        private bool reactivateInput, booting, showKnowledge;
        private string manualComment;
        private bool solved;
        private readonly Dictionary<string, string> drafts = new Dictionary<string, string>();   // half-typed queries, per computer
        private GameObject watchWarning;
        private Text watchText;

        private PuzzleData[] sequence;
        private int seqIndex;
        private bool awaitingNext, sequenceDone;
        private Action onSequenceDone, onClosed;

        private void Awake()
        {
            Instance = this;
            UiKit.EnsureEventSystem();
            Build();
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================== opening

        /// <summary>An employee PC with one thing to find. onClose runs when Donnie leaves the computer.</summary>
        public void Open(PuzzleData data, ComputerSkin look, Action onClose = null)
        {
            sequence = null;
            onClosed = onClose;
            Show(data, look);
        }

        /// <summary>
        /// The employee terminal: Donnie's phone hacks it (clearance upgrade), then Manual runs the lessons for the
        /// next floor. Re-opening resumes where he left off. onHacked runs once the upgrade lands; onDone after the last lesson.
        /// </summary>
        public void OpenTerminal(PuzzleData[] lessons, bool alreadyHacked, string clearanceName, Action onHacked, Action onDone)
        {
            if (sequence != lessons) { sequence = lessons; seqIndex = 0; sequenceDone = false; }
            onSequenceDone = onDone;
            onClosed = null;
            awaitingNext = false;
            if (!DifficultyInfo.KodexHelps())
            {
                // Leet: KODEX teaches nothing. The lessons count as done; Donnie just takes the clearance and goes.
                foreach (var l in lessons) GameState.Set(l.rewardFlag);
                seqIndex = lessons.Length - 1;
                sequenceDone = true;
            }
            ShowStep();
            if (!DifficultyInfo.KodexHelps()) SetStatus(skin == ComputerSkin.EmployeeTerminal ? "> KODEX: NO LESSONS. YOU'RE ON YOUR OWN." : "No lessons.", theme.dim);
            if (!alreadyHacked) StartCoroutine(Hack(clearanceName, onHacked));
        }

        private void ShowStep()
        {
            while (seqIndex < sequence.Length - 1 && GameState.Has(sequence[seqIndex].rewardFlag)) seqIndex++;
            if (GameState.Has(sequence[seqIndex].rewardFlag)) sequenceDone = true;
            Show(sequence[seqIndex], ComputerSkin.EmployeeTerminal);
        }

        private void Show(PuzzleData data, ComputerSkin look)
        {
            puzzle = data;
            db = data.BuildDatabase();
            skin = look;
            solved = GameState.Has(data.rewardFlag);
            manualComment = null;
            ApplySkin();

            tablesText.text = BuildTablesText(db);
            resultText.text = "";
            input.text = drafts.TryGetValue(data.id ?? "", out var draft) ? draft : "";
            headerText.text = skin == ComputerSkin.EmployeeTerminal
                ? "GYARACORP EMPLOYEE TERMINAL v2.3   ::   SESSION " + (string.IsNullOrEmpty(data.user) ? "maint-0009" : data.user)
                : "GyaraDB   ▸   connected   ▸   user: " + (string.IsNullOrEmpty(data.user) ? "guest" : data.user);

            if (solved) SetStatus(data.successMessage, theme.ok);
            else SetStatus(skin == ComputerSkin.EmployeeTerminal ? "> READY" : "Ready.", theme.dim);

            note.SetActive(!string.IsNullOrEmpty(data.note) && !phone.activeSelf);
            noteText.text = data.note ?? "";
            RefreshPhone();

            root.SetActive(true);
            bootLayer.SetActive(false);
            FocusInput();
        }

        public void Close()
        {
            if (booting) return;
            if (puzzle != null) drafts[puzzle.id ?? ""] = input.text;
            root.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (sequence != null && sequenceDone)
            {
                var done = onSequenceDone;
                onSequenceDone = null;
                done?.Invoke();
            }
            var closed = onClosed;
            onClosed = null;
            closed?.Invoke();
        }

        // ================================================================== the hack

        private IEnumerator Hack(string clearanceName, Action onHacked)
        {
            booting = true;
            bootLayer.SetActive(true);
            input.DeactivateInputField();
            var sb = new StringBuilder();
            bootFill.rectTransform.anchorMax = new Vector2(0, 1);
            bootFill.transform.parent.parent.gameObject.SetActive(false);

            IEnumerator Line(string s, float pause)
            {
                sb.Append("> ").Append(s).Append('\n');
                bootText.text = sb.ToString() + "_";
                yield return new WaitForSecondsRealtime(pause);
            }

            yield return Line("USB-C DEVICE DETECTED", 0.6f);
            yield return Line("DEVICE: PHONE (UNREGISTERED)", 0.5f);
            yield return Line("KODEX v1.0 REQUESTS ACCESS...", 0.7f);
            yield return Line("ACCESS DENIED", 0.5f);
            yield return Line("KODEX: bypassing. Please hold.", 0.6f);

            sb.Append("> HACKING");
            bootFill.transform.parent.parent.gameObject.SetActive(true);
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 3.2f)
            {
                bootFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(t), 1);
                bootText.text = sb.ToString() + new string('.', 1 + (int)(t * 12) % 4) + "\n";
                yield return null;
            }
            bootFill.rectTransform.anchorMax = Vector2.one;
            sb.Append(" DONE\n");
            yield return Line("BADGE J-0009  (RAYMOND, DONALD  /  FACILITIES)", 0.5f);
            yield return Line("CLEARANCE UPGRADED: " + clearanceName.ToUpperInvariant(), 1.0f);
            onHacked?.Invoke();
            yield return Line(DifficultyInfo.KodexHelps() ? "KODEX: Loading lessons for the next floor." : "KODEX: No lessons. You're on your own.", 1.2f);

            booting = false;
            bootLayer.SetActive(false);
            FocusInput();
        }

        // ================================================================== running queries

        private void Update()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            pcClock.text = GameClock.Text;
            UpdateWatchWarning();
            if (booting) return;

            if (reactivateInput)
            {
                reactivateInput = false;
                FocusInput();
            }

            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }

            if (phone.activeSelf)
            {
                if (kb.tabKey.wasPressedThisFrame && knowledgeTab.activeSelf)
                {
                    showKnowledge = !showKnowledge;
                    RefreshPhone();
                    input.text = input.text.Replace("\t", "");
                    reactivateInput = true;
                }
                if (kb.pageDownKey.wasPressedThisFrame) phoneScroll.verticalNormalizedPosition = Mathf.Max(0, phoneScroll.verticalNormalizedPosition - 0.25f);
                if (kb.pageUpKey.wasPressedThisFrame) phoneScroll.verticalNormalizedPosition = Mathf.Min(1, phoneScroll.verticalNormalizedPosition + 0.25f);
            }

            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (enter && awaitingNext)
            {
                awaitingNext = false;
                seqIndex++;
                ShowStep();
                return;
            }

            if (enter && !shift && input.isFocused)
            {
                RunQuery();
                reactivateInput = true; // Enter makes the field lose focus; take it back next frame
            }
        }

        /// <summary>Someone is noticing Donnie at this computer: red edge and a prompt to get out (Esc) while there's time.</summary>
        private void UpdateWatchWarning()
        {
            float alarm = NPC.Guard.Alarm;
            bool on = NPC.Guard.CatchAtComputers && alarm > 0.01f;   // only matters when computers aren't safe
            watchWarning.SetActive(on);
            if (!on) return;
            float pulse = 0.35f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f));
            foreach (var img in watchWarning.GetComponentsInChildren<Image>()) img.color = new Color(1f, 0.2f, 0.15f, pulse * Mathf.Clamp01(alarm * 2.5f));
            watchText.color = new Color(1f, 0.85f, 0.8f, 1f);
        }

        private void FocusInput()
        {
            if (booting) return;
            input.Select();
            input.ActivateInputField();
        }

        private void SetStatus(string text, Color color)
        {
            statusText.color = color;
            statusText.text = text;
        }

        private void RunQuery()
        {
            string q = input.text.Trim();
            if (q.Length == 0) return;
            try
            {
                QueryResult result = SqlEngine.Execute(db, q);
                resultText.text = FormatResult(result);
                string rows = $"{result.Rows.Count} row{(result.Rows.Count == 1 ? "" : "s")}";

                if (PuzzleChecker.Check(puzzle, result, out string feedback))
                {
                    solved = true;
                    Sfx.Play(Sfx.Id.Success);
                    GameState.Set(puzzle.rewardFlag);
                    SetStatus(rows + ".   " + puzzle.successMessage, theme.ok);
                    if (!string.IsNullOrEmpty(puzzle.unlocksLessons))
                    {
                        foreach (var l in puzzle.unlocksLessons.Split(',')) GameState.Set("lesson_" + l.Trim());
                        HudUI.Instance?.Toast("KODEX: new page in Knowledge");
                    }
                    bool more = sequence != null && seqIndex < sequence.Length - 1;
                    if (sequence != null && !more) sequenceDone = true;
                    awaitingNext = more;
                    manualComment = more ? "That's it. Press Enter for the next lesson." : "That's it.";
                }
                else
                {
                    Sfx.Play(Sfx.Id.Tick, 0.7f);
                    SetStatus(rows + ".", theme.text);
                    manualComment = DifficultyInfo.KodexHelps() ? feedback : null;   // Leet: no hints
                    if (SqlEngine.Changes(q))
                    {
                        db = puzzle.BuildDatabase();   // undo it: practice data goes back to how it was
                        tablesText.text = BuildTablesText(db);
                        manualComment = (manualComment != null ? feedback + " " : "") + "(The table has been put back the way it was. Try again.)";
                    }
                }
            }
            catch (SqlException e)
            {
                resultText.text = "";
                Sfx.Play(Sfx.Id.Error, 0.7f);
                SetStatus("Error: " + e.Message, theme.error);
                manualComment = null;
            }
            RefreshPhone();
        }

        // ================================================================== skins + phone

        private void ApplySkin()
        {
            bool crt = skin == ComputerSkin.EmployeeTerminal;
            theme = crt ? CrtTheme : PcTheme;
            pcLayer.SetActive(!crt);
            crtLayer.SetActive(crt);

            bool showPhone = GameState.HasManual;
            phone.SetActive(showPhone);
            knowledgeTab.SetActive(GameState.ManualPanelAllowed);
            if (!GameState.ManualPanelAllowed) showKnowledge = false;

            pcWindow.anchorMax = new Vector2(showPhone ? 0.74f : 1f, 1f);
            crtArea.anchorMax = new Vector2(showPhone ? 0.74f : 1f, 1f);
            content.SetParent(crt ? crtScreen : (RectTransform)pcWindow.Find("Body"), false);
            content.offsetMin = crt ? new Vector2(18, 10) : new Vector2(14, 8);
            content.offsetMax = crt ? new Vector2(-18, -12) : new Vector2(-14, -8);
            scanlines.transform.SetAsLastSibling();
            bootLayer.transform.SetAsLastSibling();
            bootLayer.transform.SetParent(crtScreen, false);
            scanlines.transform.SetAsLastSibling();

            crtPort.text = showPhone ? "USB-C  ●  PHONE CONNECTED" : "USB-C  ○";

            Font body = crt ? UiKit.Mono : UiKit.UiFont;
            headerText.font = crt ? UiKit.Mono : UiKit.UiFont;
            statusText.font = body;
            helpText.font = body;

            headerText.color = theme.dim;
            tablesText.color = theme.accent;
            resultText.color = theme.text;
            helpText.color = theme.dim;
            inputText.color = theme.text;
            placeholder.color = new Color(theme.text.r, theme.text.g, theme.text.b, 0.35f);
            fieldBg.color = theme.field;
            runBg.color = theme.button;
            runLabel.color = theme.buttonText;
            input.caretColor = theme.text;
            input.customCaretColor = true;
            input.selectionColor = new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.35f);

            string tabHint = showPhone && GameState.ManualPanelAllowed ? (crt ? "   TAB KODEX PAGE" : "    Tab: Task / Knowledge") : "";
            helpText.text = crt
                ? "ENTER RUN   SHIFT+ENTER NEW LINE   ESC DISCONNECT" + tabHint
                : "Enter: run    Shift+Enter: new line    Esc: leave computer" + tabHint;
        }

        private void RefreshPhone()
        {
            if (phone == null || !phone.activeSelf) return;
            taskTabBg.color = showKnowledge ? new Color32(0x3A, 0x3F, 0x4A, 0xFF) : new Color32(0xB0, 0x8D, 0x57, 0xFF);
            knowledgeTabBg.color = showKnowledge ? new Color32(0xB0, 0x8D, 0x57, 0xFF) : new Color32(0x3A, 0x3F, 0x4A, 0xFF);

            var sb = new StringBuilder();
            if (sequence != null && !DifficultyInfo.KodexHelps())
                sb.Append("<b>KODEX</b>\n\nNo lessons on Leet. Clearance is yours; the SQL is up to you.\n\nEsc to leave the terminal.");
            else if (!showKnowledge)
            {
                sb.Append("<b>").Append(puzzle.title).Append("</b>\n\n");
                sb.Append(puzzle.CurrentBrief(solved, DifficultyInfo.KodexHelps())).Append("\n\n");
                if (solved) sb.Append("<color=#2E7D3A><b>✓ Done.</b></color> ");
                if (!string.IsNullOrEmpty(manualComment))
                    sb.Append("<color=#7A5A1E><i>").Append(manualComment).Append("</i></color>");
            }
            else
            {
                var lessons = ManualLessons.Unlocked();
                if (lessons.Count == 0) sb.Append("<i>Nothing learned yet.</i>");
                foreach (var l in lessons)
                {
                    sb.Append("<b>").Append(l.title).Append("</b>\n");
                    sb.Append(l.body).Append("\n");
                    sb.Append("<color=#2F5E9E>").Append(l.example).Append("</color>\n\n");
                }
            }
            phoneBody.text = sb.ToString();
            phoneScroll.verticalNormalizedPosition = 1f;
        }

        // ================================================================== text helpers

        private static string BuildTablesText(Database database)
        {
            var sb = new StringBuilder();
            foreach (var table in database.Tables)
            {
                sb.Append("<b>").Append(table.Name).Append("</b>\n");
                foreach (var c in table.Columns) sb.Append("   ").Append(c).Append('\n');
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string FormatResult(QueryResult r)
        {
            int cols = r.Columns.Length;
            int shown = Mathf.Min(r.Rows.Count, MaxShownRows);
            var widths = new int[cols];
            for (int c = 0; c < cols; c++) widths[c] = r.Columns[c].Length;
            for (int i = 0; i < shown; i++)
                for (int c = 0; c < cols; c++)
                    widths[c] = Mathf.Max(widths[c], Mathf.Min(QueryResult.Format(r.Rows[i][c]).Length, 24));

            var sb = new StringBuilder();
            AppendRow(sb, r.Columns, widths);
            var sep = new string[cols];
            for (int c = 0; c < cols; c++) sep[c] = new string('-', widths[c]);
            AppendRow(sb, sep, widths);
            for (int i = 0; i < shown; i++)
            {
                var cells = new string[cols];
                for (int c = 0; c < cols; c++) cells[c] = QueryResult.Format(r.Rows[i][c]);
                AppendRow(sb, cells, widths);
            }
            if (r.Rows.Count > shown) sb.AppendLine($"... and {r.Rows.Count - shown} more row(s)");
            return sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, IList<string> cells, int[] widths)
        {
            for (int c = 0; c < widths.Length; c++)
            {
                string s = cells[c];
                if (s.Length > 24) s = s.Substring(0, 23) + "…";
                sb.Append(s.PadRight(widths[c])).Append(c < widths.Length - 1 ? " | " : "");
            }
            sb.AppendLine();
        }

        // ================================================================== construction

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "ComputerCanvas", 100);
            root = UiKit.Box("Computer", canvas.transform, Color.black, Vector2.zero, Vector2.one).gameObject;
            root.GetComponent<Image>().raycastTarget = true;

            BuildPcLayer();
            BuildCrtLayer();
            BuildContent();
            BuildBoot();
            BuildNote();
            BuildWatchWarning();
            BuildPhone();
        }

        private void BuildPcLayer()
        {
            pcLayer = UiKit.Box("PC", root.transform, new Color32(0x12, 0x18, 0x26, 0xFF), Vector2.zero, Vector2.one).gameObject;
            UiKit.Label("Logo", pcLayer.transform, UiKit.UiFont, 120, new Color(1, 1, 1, 0.04f), Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold).text = "G";

            var bar = UiKit.Box("Taskbar", pcLayer.transform, new Color32(0x0B, 0x0F, 0x18, 0xFF), Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 26));
            var badge = UiKit.Box("Start", bar.transform, PcTheme.accent, Vector2.zero, new Vector2(0, 1), new Vector2(6, 4), new Vector2(24, -4));
            UiKit.Label("G", badge.transform, UiKit.UiFont, 12, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold).text = "G";
            UiKit.Label("Os", bar.transform, UiKit.UiFont, 12, PcTheme.dim, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(32, 0), Vector2.zero).text = "GyaraOS";
            var app = UiKit.Box("AppTab", bar.transform, new Color32(0x1F, 0x2A, 0x40, 0xFF), new Vector2(0, 0), new Vector2(0, 1), new Vector2(96, 3), new Vector2(230, -3));
            UiKit.Label("Name", app.transform, UiKit.UiFont, 11, PcTheme.text, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter).text = "GyaraDB";
            var mail = UiKit.Box("MailTab", bar.transform, new Color32(0x16, 0x1C, 0x2B, 0xFF), new Vector2(0, 0), new Vector2(0, 1), new Vector2(234, 3), new Vector2(330, -3));
            UiKit.Label("Name", mail.transform, UiKit.UiFont, 11, PcTheme.dim, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter).text = "Mail (37)";
            pcClock = UiKit.Label("Clock", bar.transform, UiKit.UiFont, 12, PcTheme.text, Vector2.zero, Vector2.one, TextAnchor.MiddleRight, FontStyle.Normal, Vector2.zero, new Vector2(-10, 0));

            var frame = UiKit.Box("Window", pcLayer.transform, new Color32(0x3A, 0x4A, 0x6A, 0xFF), Vector2.zero, Vector2.one, new Vector2(20, 40), new Vector2(-20, -16));
            pcWindow = frame.rectTransform;
            var titleBar = UiKit.Box("TitleBar", frame.transform, new Color32(0x25, 0x32, 0x4D, 0xFF), new Vector2(0, 1), Vector2.one, new Vector2(1, -24), new Vector2(-1, -1));
            UiKit.Label("Title", titleBar.transform, UiKit.UiFont, 12, PcTheme.text, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(10, 0), Vector2.zero).text = "GyaraDB Query Studio";
            UiKit.Label("Buttons", titleBar.transform, UiKit.UiFont, 12, PcTheme.dim, Vector2.zero, Vector2.one, TextAnchor.MiddleRight, FontStyle.Normal, Vector2.zero, new Vector2(-10, 0)).text = "—    □    ✕";
            UiKit.Box("Body", frame.transform, new Color32(0x1A, 0x22, 0x33, 0xFF), Vector2.zero, Vector2.one, new Vector2(1, 1), new Vector2(-1, -24));
        }

        private void BuildCrtLayer()
        {
            crtLayer = UiKit.Box("Terminal", root.transform, new Color32(0xC8, 0xB8, 0x8E, 0xFF), Vector2.zero, Vector2.one).gameObject;
            var screenArea = UiKit.Box("ScreenArea", crtLayer.transform, Color.clear, Vector2.zero, Vector2.one, new Vector2(26, 58), new Vector2(-26, -24));
            crtArea = screenArea.rectTransform;
            var ring = UiKit.Box("Ring", screenArea.transform, new Color32(0x8F, 0x80, 0x5C, 0xFF), Vector2.zero, Vector2.one);
            var glass = UiKit.Box("Glass", ring.transform, new Color32(0x0A, 0x11, 0x0B, 0xFF), Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
            crtScreen = glass.rectTransform;
            scanlines = UiKit.Scanlines(glass.transform, 0.22f);

            var chin = UiKit.Box("Chin", crtLayer.transform, Color.clear, Vector2.zero, new Vector2(1, 0), new Vector2(26, 8), new Vector2(-26, 52));
            UiKit.Label("Brand", chin.transform, UiKit.UiFont, 16, new Color32(0x6B, 0x5E, 0x40, 0xFF), Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(8, 0), Vector2.zero).text = "GYARACORP  EMPLOYEE TERMINAL";
            crtPort = UiKit.Label("Port", chin.transform, UiKit.Mono, 12, new Color32(0x5A, 0x4F, 0x36, 0xFF), Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
            UiKit.Box("Led", chin.transform, new Color32(0x6C, 0xFF, 0x7A, 0xFF), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, -4), new Vector2(-16, 4));
            UiKit.Box("Vents", chin.transform, new Color32(0xA9, 0x99, 0x70, 0xFF), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-150, -2), new Vector2(-40, 2));
        }

        private void BuildContent()
        {
            var go = new GameObject("Content", typeof(RectTransform));
            content = UiKit.Rect(go, Vector2.zero, Vector2.one);
            content.SetParent(root.transform, false);

            headerText = UiKit.Label("Header", content, UiKit.UiFont, 11, Color.white, new Vector2(0, 0.93f), Vector2.one, TextAnchor.MiddleLeft);

            // tables sidebar
            var side = UiKit.Box("Tables", content, new Color(0, 0, 0, 0.18f), new Vector2(0, 0.12f), new Vector2(0.24f, 0.91f));
            UiKit.Label("Title", side.transform, UiKit.UiFont, 10, new Color(1, 1, 1, 0.45f), new Vector2(0, 1), Vector2.one, TextAnchor.UpperLeft, FontStyle.Bold, new Vector2(8, -18), new Vector2(-4, -4)).text = "TABLES";
            tablesText = UiKit.Label("List", side.transform, UiKit.Mono, 12, Color.white, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal, new Vector2(8, 4), new Vector2(-4, -22));

            // query box + run
            fieldBg = UiKit.Box("QueryBox", content, Color.black, new Vector2(0.26f, 0.70f), new Vector2(0.88f, 0.91f));
            fieldBg.raycastTarget = true;
            inputText = UiKit.Label("Text", fieldBg.transform, UiKit.Mono, 15, Color.white, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal, new Vector2(8, 6), new Vector2(-8, -6));
            inputText.supportRichText = false;
            placeholder = UiKit.Label("Placeholder", fieldBg.transform, UiKit.Mono, 15, Color.gray, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Italic, new Vector2(8, 6), new Vector2(-8, -6));
            placeholder.text = "SELECT ...";
            input = fieldBg.gameObject.AddComponent<InputField>();
            input.onValueChanged.AddListener(_ => { if (IsOpen) Sfx.Play(Sfx.Id.Type, 0.5f, UnityEngine.Random.Range(0.9f, 1.1f)); });
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.targetGraphic = fieldBg;
            // Enter runs the query and never adds a line; Shift+Enter adds one on purpose
            input.lineType = InputField.LineType.MultiLineNewline;
            input.onValidateInput = (text, index, c) =>
            {
                if (c != '\n' && c != '\r') return c == '\t' ? '\0' : c;
                var k = Keyboard.current;
                bool shiftHeld = k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
                return shiftHeld ? '\n' : '\0';
            };

            runBg = UiKit.Box("Run", content, Color.gray, new Vector2(0.895f, 0.70f), new Vector2(1, 0.91f));
            runBg.raycastTarget = true;
            runLabel = UiKit.Label("Label", runBg.transform, UiKit.UiFont, 16, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            runLabel.text = "RUN";
            var button = runBg.gameObject.AddComponent<Button>();
            button.targetGraphic = runBg;
            button.onClick.AddListener(() => { if (!booting) { RunQuery(); FocusInput(); } });

            resultText = UiKit.Label("Results", content, UiKit.Mono, 14, Color.white, new Vector2(0.26f, 0.12f), new Vector2(1, 0.68f));
            resultText.horizontalOverflow = HorizontalWrapMode.Overflow;
            resultText.supportRichText = false;

            statusText = UiKit.Label("Status", content, UiKit.UiFont, 13, Color.white, new Vector2(0, 0.045f), new Vector2(1, 0.11f), TextAnchor.MiddleLeft, FontStyle.Bold);
            helpText = UiKit.Label("Help", content, UiKit.UiFont, 10, Color.gray, Vector2.zero, new Vector2(1, 0.04f), TextAnchor.LowerLeft);
        }

        private void BuildBoot()
        {
            var b = UiKit.Box("Boot", root.transform, new Color32(0x0A, 0x11, 0x0B, 0xFF), Vector2.zero, Vector2.one);
            bootLayer = b.gameObject;
            bootText = UiKit.Label("Text", b.transform, UiKit.Mono, 16, CrtTheme.text, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal, new Vector2(24, 60), new Vector2(-24, -20));
            var bar = UiKit.Framed("Progress", b.transform, new Color32(0x06, 0x0C, 0x07, 0xFF), CrtTheme.dim, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 24), new Vector2(-24, 40));
            bootFill = UiKit.Box("Fill", bar.transform, CrtTheme.text, Vector2.zero, new Vector2(0, 1));
            bootLayer.SetActive(false);
        }

        private void BuildWatchWarning()
        {
            var go = new GameObject("WatchWarning", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            UiKit.Rect(go, Vector2.zero, Vector2.one);
            watchWarning = go;
            UiKit.Box("Top", go.transform, Color.red, new Vector2(0, 1), Vector2.one, new Vector2(0, -8), Vector2.zero);
            UiKit.Box("Bottom", go.transform, Color.red, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 8));
            UiKit.Box("Left", go.transform, Color.red, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(8, 0));
            UiKit.Box("Right", go.transform, Color.red, new Vector2(1, 0), Vector2.one, new Vector2(-8, 0), Vector2.zero);
            var band = UiKit.Box("Band", go.transform, Color.red, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-170, -40), new Vector2(170, -12));
            watchText = UiKit.Label("Text", band.transform, UiKit.UiFont, 14, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            watchText.text = "Someone's looking this way!   Esc";
            go.SetActive(false);
        }

        private void BuildNote()
        {
            // a yellow sticky note on the monitor (only shown when a puzzle has one)
            // stuck at the bottom right of the screen (clear of the query box, RUN and the "Esc: leave computer" line)
            var n = UiKit.Box("StickyNote", root.transform, new Color32(0xF2, 0xDC, 0x6B, 0xFF), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-214, 56), new Vector2(-28, 262));
            n.rectTransform.localRotation = Quaternion.Euler(0, 0, -2.5f);
            UiKit.Box("Tape", n.transform, new Color(1, 1, 1, 0.45f), new Vector2(0.35f, 1), new Vector2(0.65f, 1), new Vector2(0, -6), new Vector2(0, 6));
            noteText = UiKit.Label("Text", n.transform, UiKit.UiFont, 12, new Color32(0x2B, 0x2A, 0x4A, 0xFF), Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Italic, new Vector2(10, 8), new Vector2(-8, -12));
            note = n.gameObject;
            note.SetActive(false);
        }

        private void BuildPhone()
        {
            var body = UiKit.Framed("Phone", root.transform, new Color32(0x1C, 0x1D, 0x22, 0xFF), new Color32(0x3A, 0x3B, 0x42, 0xFF),
                new Vector2(0.755f, 0), new Vector2(0.985f, 1), new Vector2(0, 40), new Vector2(0, -14), 3f);
            phone = body.transform.parent.gameObject;
            UiKit.Box("Speaker", body.transform, new Color32(0x0C, 0x0C, 0x0F, 0xFF), new Vector2(0.4f, 1), new Vector2(0.6f, 1), new Vector2(0, -10), new Vector2(0, -6));
            var screen = UiKit.Box("Screen", body.transform, new Color32(0xF2, 0xEF, 0xE8, 0xFF), Vector2.zero, Vector2.one, new Vector2(8, 18), new Vector2(-8, -18));

            var header = UiKit.Box("Header", screen.transform, new Color32(0x2A, 0x2F, 0x3A, 0xFF), new Vector2(0, 1), Vector2.one, new Vector2(0, -26), Vector2.zero);
            UiKit.Label("AppName", header.transform, UiKit.UiFont, 13, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(10, 0), Vector2.zero).text = "KODEX";

            // tabs
            taskTabBg = UiKit.Box("TaskTab", screen.transform, Color.gray, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, -48), new Vector2(-1, -27));
            taskTabBg.raycastTarget = true;
            taskTabLabel = UiKit.Label("Label", taskTabBg.transform, UiKit.UiFont, 11, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            taskTabLabel.text = "TASK";
            taskTabBg.gameObject.AddComponent<Button>().onClick.AddListener(() => { showKnowledge = false; RefreshPhone(); FocusInput(); });

            knowledgeTabBg = UiKit.Box("KnowledgeTab", screen.transform, Color.gray, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(1, -48), new Vector2(0, -27));
            knowledgeTabBg.raycastTarget = true;
            knowledgeTab = knowledgeTabBg.gameObject;
            knowledgeTabLabel = UiKit.Label("Label", knowledgeTabBg.transform, UiKit.UiFont, 11, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            knowledgeTabLabel.text = "KNOWLEDGE";
            knowledgeTabBg.gameObject.AddComponent<Button>().onClick.AddListener(() => { showKnowledge = true; RefreshPhone(); FocusInput(); });

            var viewport = UiKit.Box("Viewport", screen.transform, Color.clear, Vector2.zero, Vector2.one, new Vector2(8, 6), new Vector2(-6, -54));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.raycastTarget = true;
            phoneBody = UiKit.Label("Body", viewport.transform, UiKit.UiFont, 12, new Color32(0x22, 0x24, 0x2B, 0xFF), new Vector2(0, 1), Vector2.one);
            var mrt = phoneBody.rectTransform;
            mrt.pivot = new Vector2(0.5f, 1);
            mrt.sizeDelta = new Vector2(0, 100);
            phoneBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            phoneScroll = viewport.gameObject.AddComponent<ScrollRect>();
            phoneScroll.content = mrt;
            phoneScroll.horizontal = false;
            phoneScroll.movementType = ScrollRect.MovementType.Clamped;
            phoneScroll.scrollSensitivity = 20f;
        }
    }
}

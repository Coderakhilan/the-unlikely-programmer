using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Dialogue box with portrait, name plate and typewriter text, plus the "[E] ..." interaction prompt.
    ///
    /// A line can switch speaker and expression with a prefix:  "Dan[tired]: You again?"
    /// The prefix is only recognised when that speaker has a portrait, so ordinary colons are safe.
    /// Lines with no speaker are Donnie's thoughts / narration (no portrait, italic).
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        private const float CharsPerSecond = 40f;
        private static readonly Regex SpeakerPrefix = new Regex(@"^([A-Za-z][A-Za-z ]{0,20}?)(?:\[([a-z_]+)\])?:\s+(.*)$", RegexOptions.Singleline);

        private static readonly Regex ThoughtExpression = new Regex(@"^\[([a-z_]+)\]\s*(.*)$", RegexOptions.Singleline);

        /// <summary>People who talk but have no portrait (their prefix is still recognised).</summary>
        private static readonly System.Collections.Generic.HashSet<string> FacelessSpeakers =
            new System.Collections.Generic.HashSet<string> { "Receptionist", "Employee", "Security", "Unknown", "Voice", "Linda", "Ken", "Joey", "Omar", "Tomas", "Priya", "Grace",
                "Candidate", "Video", "Interviewer", "Head of HR", "HR Officer",
                "Assistant", "Executive", "Police" };

        public static DialogueUI Instance { get; private set; }
        public bool IsOpen => panel != null && panel.activeSelf;

        private GameObject panel, portraitFrame, namePlate, promptRoot;
        private Image portrait, plateFill, plateBorder;
        private Text speakerText, bodyText, promptText, promptKey, moreMark;
        private RectTransform bodyRect, panelRect, moreRect, promptRect;
        private CanvasGroup panelGroup;
        private float openedAt;

        private string[] lines;
        private int index;
        private string defaultSpeaker, currentSpeaker;
        private string fullText;
        private float shown;
        private Action onClosed;

        // ---- choices (e.g. tell the truth or lie)
        private string[] choices;
        private int choice;
        private Action<int> onChoice;
        public bool IsChoosing => IsOpen && choices != null;

        /// <summary>The text on screen right now (tests).</summary>
        public string VisibleText => bodyText != null ? bodyText.text : "";

        /// <summary>
        /// Ask a question: the prompt line types out, then the options appear under it.
        /// W/S (or arrows) move, E/Enter picks; onPick gets the option's index.
        /// </summary>
        public void ShowChoice(string promptLine, string[] options, Action<int> onPick)
        {
            choices = options;
            choice = 0;
            onChoice = onPick;
            Show("", new[] { promptLine }, null);
        }

        /// <summary>Pick an option directly (tests).</summary>
        public void Choose(int option)
        {
            if (!IsChoosing) return;
            shown = fullText.Length;
            choice = Mathf.Clamp(option, 0, choices.Length - 1);
            Advance();
        }

        private void RenderChoices()
        {
            var sb = new System.Text.StringBuilder(fullText).Append('\n');
            for (int i = 0; i < choices.Length; i++)
                sb.Append(i == choice ? "\n<color=#D4F03C><b>>_  " + choices[i] + "</b></color>" : "\n     <color=#9A968C>" + choices[i] + "</color>");
            bodyText.text = sb.ToString();
        }

        private void Awake()
        {
            Instance = this;
            Build();
            panel.SetActive(false);
            SetPrompt(null);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Show(string speaker, string[] dialogueLines) => Show(speaker, dialogueLines, null);

        /// <summary>Show lines one at a time; onDone runs after the last one is dismissed.</summary>
        public void Show(string speaker, string[] dialogueLines, Action onDone)
        {
            if (dialogueLines == null || dialogueLines.Length == 0) { onDone?.Invoke(); return; }
            lines = dialogueLines;
            index = 0;
            defaultSpeaker = speaker ?? "";
            currentSpeaker = defaultSpeaker;
            onClosed = onDone;
            if (!panel.activeSelf) openedAt = Time.unscaledTime;   // slide up only when the box wasn't already showing
            panel.SetActive(true);
            SetPrompt(null);
            ShowLine();
        }

        /// <summary>Finish the typewriter, or go to the next line, or close.</summary>
        public void Advance()
        {
            if (!IsOpen) return;
            if (shown < fullText.Length)
            {
                // first press: the whole line at once (the typewriter only redraws while it's still typing)
                shown = fullText.Length;
                bodyText.text = fullText;
                if (choices != null) RenderChoices();
                return;
            }
            if (choices != null)
            {
                int picked = choice;
                var pick = onChoice;
                choices = null;
                onChoice = null;
                Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Confirm);
                panel.SetActive(false);
                pick?.Invoke(picked);
                return;
            }
            Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move, 0.5f);
            index++;
            if (index >= lines.Length)
            {
                panel.SetActive(false);
                var done = onClosed;
                onClosed = null;
                done?.Invoke();
                return;
            }
            ShowLine();
        }

        /// <summary>Pass null/empty to hide the hint.</summary>
        public void SetPrompt(string text, string key = "E")
        {
            bool show = !string.IsNullOrEmpty(text) && !IsOpen && !UiKit.AnyModalOpen;
            promptRoot.SetActive(show);
            if (!show || (promptText.text == text && promptKey.text == key)) return;   // called every frame: only re-measure when it changes
            promptText.text = text;
            promptKey.text = key;
            // the pill fits its text: key cap on the left, then the words
            float w = Mathf.Clamp(UiKit.TextWidth(promptText, text) + 58f, 110f, 520f);
            promptRect.offsetMin = new Vector2(-w / 2f, promptRect.offsetMin.y);
            promptRect.offsetMax = new Vector2(w / 2f, promptRect.offsetMax.y);
        }

        private void ShowLine()
        {
            string line = Unlikely.Core.Music.ApplyCues(lines[index]);   // {music:...} cues start tracks; the player never sees them
            string expression = null;
            var m = SpeakerPrefix.Match(line);
            if (m.Success && (ArtLibrary.Portrait(m.Groups[1].Value, null) != null || FacelessSpeakers.Contains(m.Groups[1].Value.Trim())))
            {
                currentSpeaker = m.Groups[1].Value.Trim();
                expression = m.Groups[2].Success ? m.Groups[2].Value : null;
                line = m.Groups[3].Value;
            }
            else
            {
                // no name: back to the box's default speaker (usually Donnie thinking), not whoever spoke last
                currentSpeaker = defaultSpeaker;
                // a thought can still set Donnie's expression: "[worried] Where is she?"
                var t = ThoughtExpression.Match(line);
                if (t.Success) { expression = t.Groups[1].Value; line = t.Groups[2].Value; }
            }

            // no speaker = Donnie thinking: his face, italic text
            bool narration = string.IsNullOrEmpty(currentSpeaker);
            var face = ArtLibrary.Portrait(narration ? "Donnie" : currentSpeaker, expression);
            bool hasFace = face != null;
            portraitFrame.SetActive(hasFace);
            if (hasFace) portrait.sprite = face;
            bodyRect.offsetMin = new Vector2(hasFace ? 196 : 24, 16);

            namePlate.SetActive(true);
            speakerText.text = narration ? "Donnie" : currentSpeaker;
            // speaking: a lime name tab (like the logo's label); thinking: a quiet outlined one
            plateFill.color = narration ? UiKit.GlassLight : UiKit.Lime;
            plateBorder.color = narration ? UiKit.Edge : UiKit.Lime;
            speakerText.color = narration ? UiKit.Muted : UiKit.OnLime;
            speakerText.fontStyle = narration ? FontStyle.Italic : FontStyle.Bold;
            bodyText.fontStyle = narration ? FontStyle.Italic : FontStyle.Normal;
            bodyText.color = narration ? new Color(0.85f, 0.83f, 0.78f) : UiKit.Paper;

            fullText = line;
            shown = 0f;
            bodyText.text = "";
        }

        private void Update()
        {
            if (!IsOpen) return;
            float open = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.2f);
            open = 1f - (1f - open) * (1f - open);
            panelGroup.alpha = open;
            if (Time.unscaledTime - openedAt < 0.4f)   // only during the slide-up
            {
                float drop = 12f * (1f - open);
                panelRect.offsetMin = new Vector2(40, 16 - drop);
                panelRect.offsetMax = new Vector2(-40, 166 - drop);
            }
            moreRect.anchoredPosition = new Vector2(-28f, 18f + 2f * Mathf.Sin(Time.unscaledTime * 6f));
            if (shown < fullText.Length)
            {
                int before = (int)shown;
                shown = Mathf.Min(fullText.Length, shown + Time.unscaledDeltaTime * CharsPerSecond);
                bodyText.text = fullText.Substring(0, (int)shown);
                // the "voice": a blip every other letter, pitched by the speaker
                int now = (int)shown;
                if (now > before && now % 2 == 0 && now <= fullText.Length && char.IsLetterOrDigit(fullText[now - 1])) Unlikely.Core.Sfx.Voice(currentSpeaker);
            }
            moreRect.gameObject.SetActive(choices == null && shown >= fullText.Length);
            if (choices != null && shown >= fullText.Length)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                var pad = UnityEngine.InputSystem.Gamepad.current;
                int move = 0;
                if (kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) move = -1;
                if (kb != null && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)) move = 1;
                if (pad != null && pad.dpad.up.wasPressedThisFrame) move = -1;
                if (pad != null && pad.dpad.down.wasPressedThisFrame) move = 1;
                if (move != 0) { choice = (choice + move + choices.Length) % choices.Length; Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move); }
                RenderChoices();
            }
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "DialogueCanvas", 150);

            // ---- dialogue box: bottom of the screen, dark glass with a lime edge
            var root = new GameObject("Dialogue", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            panelRect = UiKit.Rect(root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 16), new Vector2(-40, 166));
            panel = root;
            panelGroup = root.AddComponent<CanvasGroup>();
            var fill = UiKit.Panel("Box", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color32(0x0C, 0x10, 0x12, 0xF8), UiKit.LimeDim);
            UiKit.Box("TopLine", fill.transform, new Color(1, 1, 1, 0.05f), new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -4), new Vector2(-10, -3));

            // portrait on the left, in a rounded frame
            var pf = UiKit.Panel("Portrait", fill.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(10, 10), new Vector2(180, -10),
                UiKit.GlassLight, UiKit.Edge, false);
            portraitFrame = pf.transform.parent.gameObject;
            pf.gameObject.AddComponent<RectMask2D>();
            // the bust fills the frame's height (whatever its size), so it never spills over or gets clipped
            var pimg = UiKit.Box("Face", pf.transform, Color.white, Vector2.zero, Vector2.one, new Vector2(2, 0), new Vector2(-2, -2));
            portrait = pimg;
            pimg.preserveAspect = true;

            // name tab sitting on the top edge
            var plate = UiKit.Panel("NamePlate", root.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(192, -10), new Vector2(352, 16),
                UiKit.Lime, UiKit.Lime, true, 3f);
            namePlate = plate.transform.parent.gameObject;
            plateFill = plate;
            plateBorder = namePlate.transform.Find("Border").GetComponent<Image>();
            speakerText = UiKit.Label("Speaker", plate.transform, UiKit.UiFont, 14, UiKit.OnLime, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);

            bodyText = UiKit.Label("Body", fill.transform, UiKit.UiFont, 17, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Normal,
                new Vector2(196, 16), new Vector2(-28, -26));
            bodyText.lineSpacing = 1.12f;
            bodyRect = bodyText.rectTransform;

            // "more" marker: the E key and an arrow, bobbing in the bottom right corner
            var more = new GameObject("More", typeof(RectTransform));
            more.transform.SetParent(fill.transform, false);
            moreRect = (RectTransform)more.transform;
            moreRect.anchorMin = moreRect.anchorMax = new Vector2(1, 0);
            moreRect.sizeDelta = new Vector2(36, 20);
            moreRect.anchoredPosition = new Vector2(-28, 18);
            UiKit.KeyCap(more.transform, "E", new Vector2(0, 0.5f), new Vector2(0, -8), new Vector2(16, 8), 9);
            moreMark = UiKit.Label("Arrow", more.transform, UiKit.UiFont, 11, UiKit.Lime, Vector2.zero, Vector2.one, TextAnchor.MiddleRight, FontStyle.Bold);
            moreMark.text = "▼";

            // ---- interaction prompt: a pill above the item cards, key cap + what it does
            var pill = UiKit.Panel("Prompt", canvas.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-120, 66), new Vector2(120, 92),
                UiKit.Glass, UiKit.LimeDim, true, 1.5f);
            promptRoot = pill.transform.parent.gameObject;
            promptRect = (RectTransform)pill.transform.parent;
            var cap = UiKit.KeyCap(pill.transform, "E", new Vector2(0, 0.5f), new Vector2(8, -9), new Vector2(36, 9), 10);
            promptKey = cap.GetComponentInChildren<Text>();
            promptText = UiKit.Label("Text", pill.transform, UiKit.UiFont, 14, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold,
                new Vector2(40, 0), new Vector2(-10, 0));
        }
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// A door keypad: type the 4-digit code with the number keys, Enter to try it, Backspace to fix, Esc to leave.
    /// </summary>
    public class KeypadUI : MonoBehaviour
    {
        private static readonly Key[] Digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        private static readonly Key[] Pad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };

        public static KeypadUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        private GameObject root;
        private Text label, display, help;
        private Image screen;
        private string code, typed = "";
        private Action onOpen;
        private bool busy;
        private int openedFrame;

        private void Awake()
        {
            Instance = this;
            Build();
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Open(string doorName, string correctCode, Action onSuccess)
        {
            code = correctCode;
            onOpen = onSuccess;
            typed = "";
            busy = false;
            openedFrame = Time.frameCount;
            label.text = doorName.ToUpperInvariant();
            Show("", new Color32(0x1D, 0x2B, 0x1E, 0xFF));
            root.SetActive(true);
            UiKit.EnsureEventSystem();
        }

        /// <summary>One key on the pad: a digit, "<" (delete) or "OK".</summary>
        public void Press(string key)
        {
            if (!IsOpen || busy) return;
            if (key == "OK") { if (typed.Length > 0) Try(); return; }
            if (key == "<") { if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1); }
            else if (typed.Length < 4) typed += key;
            Show(typed, new Color32(0x1D, 0x2B, 0x1E, 0xFF));
            if (typed.Length == 4 && key != "<") Try();   // four digits: try it straight away
        }

        /// <summary>Type a code as if entered on the keys (tests and tutorials).</summary>
        public void Submit(string digits)
        {
            typed = digits;
            Try();
        }

        public void Close() { if (!busy) root.SetActive(false); }

        private void Update()
        {
            if (!IsOpen || busy || Time.frameCount == openedFrame) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            for (int i = 0; i < 10; i++)
                if ((kb[Digits[i]].wasPressedThisFrame || kb[Pad[i]].wasPressedThisFrame) && typed.Length < 4)
                {
                    typed += i;
                    Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Key, 0.8f, 1f + i * 0.03f);
                    Show(typed, new Color32(0x1D, 0x2B, 0x1E, 0xFF));
                    if (typed.Length == 4) { Try(); return; }   // four digits: try it straight away
                }
            if (kb.backspaceKey.wasPressedThisFrame && typed.Length > 0) { typed = typed.Substring(0, typed.Length - 1); Show(typed, new Color32(0x1D, 0x2B, 0x1E, 0xFF)); }
            if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && typed.Length > 0) Try();
        }

        private void Try()
        {
            if (typed == code) StartCoroutine(Accept());
            else StartCoroutine(Deny());
        }

        private IEnumerator Accept()
        {
            busy = true;
            Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Success);
            Show("OPEN", new Color32(0x1F, 0x5A, 0x2A, 0xFF));
            yield return new WaitForSecondsRealtime(0.5f);
            busy = false;
            root.SetActive(false);
            onOpen?.Invoke();
        }

        private IEnumerator Deny()
        {
            busy = true;
            Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Error);
            Show("DENIED", new Color32(0x6A, 0x1E, 0x1A, 0xFF));
            yield return new WaitForSecondsRealtime(0.7f);
            typed = "";
            Show("", new Color32(0x1D, 0x2B, 0x1E, 0xFF));
            busy = false;
        }

        private void Show(string text, Color bg)
        {
            screen.color = bg;
            display.text = text.Length == 0 ? "_ _ _ _" : text == "OPEN" || text == "DENIED" ? text : string.Join(" ", text.PadRight(4, '_').ToCharArray());
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "KeypadCanvas", 125);
            root = UiKit.Box("Keypad", canvas.transform, new Color(0, 0, 0, 0.5f), Vector2.zero, Vector2.one).gameObject;
            root.GetComponent<Image>().raycastTarget = true;
            var body = UiKit.Framed("Body", root.transform, new Color32(0x3A, 0x3D, 0x44, 0xFF), new Color32(0x1A, 0x1B, 0x1F, 0xFF),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-110, -150), new Vector2(110, 150), 4f);
            label = UiKit.Label("Door", body.transform, UiKit.UiFont, 12, new Color32(0xC9, 0xC6, 0xBE, 0xFF), new Vector2(0, 1), Vector2.one,
                TextAnchor.MiddleCenter, FontStyle.Bold, new Vector2(0, -30), new Vector2(0, -8));
            screen = UiKit.Box("Screen", body.transform, Color.black, new Vector2(0, 1), Vector2.one, new Vector2(18, -78), new Vector2(-18, -36));
            display = UiKit.Label("Digits", screen.transform, UiKit.Mono, 26, new Color32(0x9C, 0xFF, 0xA8, 0xFF), Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            // a 3x4 grid of (decorative) keys
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "<", "0", "OK" };
            for (int i = 0; i < 12; i++)
            {
                int r = i / 3, c = i % 3;
                var k = UiKit.Framed("Key" + keys[i], body.transform, new Color32(0x5A, 0x5E, 0x66, 0xFF), new Color32(0x22, 0x23, 0x27, 0xFF),
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(26 + c * 58, -134 - r * 40), new Vector2(26 + c * 58 + 50, -100 - r * 40), 2f);
                UiKit.Label("L", k.transform, UiKit.UiFont, 14, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold).text = keys[i];
                // the keys can be clicked too
                var keyGo = k.transform.parent.gameObject;
                keyGo.GetComponent<Image>().raycastTarget = true;
                string key = keys[i];
                keyGo.AddComponent<Button>().onClick.AddListener(() => Press(key));
            }
            help = UiKit.Label("Help", root.transform, UiKit.UiFont, 12, UiKit.Paper, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                TextAnchor.MiddleCenter, FontStyle.Normal, new Vector2(-200, -185), new Vector2(200, -160));
            help.text = "Number keys: type    Enter: try    Backspace: fix    Esc: step back";
        }
    }
}

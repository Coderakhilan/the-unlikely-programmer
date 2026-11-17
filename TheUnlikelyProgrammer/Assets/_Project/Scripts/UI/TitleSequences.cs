using System;
using System.Collections;
using Unlikely.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// The title scene's full-screen moments (text in Resources/credits.json):
    ///   start-up:  "A game by INFI", then (until accepted) the terms of use and privacy policy, then infi's note;
    ///   after an ending: "A game by INFI", "THE UNLIKELY PROGRAMMER  ·  n / 4 endings completed", then the credits roll.
    /// Extras can show the credits, the terms and the note again.
    /// Enter / E / Space / click move on; Esc skips.
    /// </summary>
    public class TitleSequences : MonoBehaviour
    {
        [Serializable]
        private class Data
        {
            public string studio = "INFI";
            public int termsVersion = 1;
            public string[] terms = new string[0], note = new string[0], credits = new string[0];
        }

        /// <summary>The start-up screens have played this launch.</summary>
        public static bool BootDone;
        /// <summary>An ending just finished: the title opens with the credits.</summary>
        public static bool PendingCredits;
        /// <summary>Tests: straight to the menu.</summary>
        public static bool SkipForTests;

        public bool Running { get; private set; }
        /// <summary>The big line on screen right now (tests).</summary>
        public string Headline => big != null ? big.text + " " + small.text : "";

        private Data data;
        private GameObject root, card, panel, roll;
        private CanvasGroup group, cardGroup;
        private Text big, small, panelTitle, panelBody, panelHint, rollText;
        private ScrollRect panelScroll;
        private bool skipAll;

        private string TermsKey => "terms_accepted_v" + data.termsVersion;
        public bool TermsAccepted => PlayerPrefs.GetInt(TermsKey, 0) == 1;

        /// <summary>Hard reset: the terms and the note show again, as on a first launch.</summary>
        public void ForgetTerms()
        {
            PlayerPrefs.DeleteKey(TermsKey);
            PlayerPrefs.Save();
        }

        private void Awake()
        {
            var json = Resources.Load<TextAsset>("credits");
            try { data = json != null ? JsonUtility.FromJson<Data>(json.text) : null; } catch (Exception e) { Debug.LogWarning("credits.json: " + e.Message); }
            data ??= new Data();
            Build();
        }

        // ------------------------------------------------------------------ the sequences

        /// <summary>Start-up: the splash; the terms and the note until the terms are accepted.</summary>
        public IEnumerator Boot()
        {
            yield return Begin();
            yield return Splash();
            if (!TermsAccepted)
            {
                yield return Page("TERMS OF USE  ·  PRIVACY", data.terms, "Enter  accept and continue", false);
                PlayerPrefs.SetInt(TermsKey, 1);
                PlayerPrefs.Save();
                yield return Page("A NOTE FROM INFI", data.note, "Enter  continue", true);
            }
            yield return End();
        }

        /// <summary>After an ending (or from Extras): who made it, how many endings so far, the roll.</summary>
        public IEnumerator Credits(bool showEndings)
        {
            yield return Begin();
            yield return Splash();
            if (showEndings && !skipAll)
            {
                int done = 0, all = 0;
                foreach (var s in Profile.Stars)
                    if (s.id.StartsWith("ending_")) { all++; if (Profile.HasStar(s.id)) done++; }
                yield return Card("THE UNLIKELY PROGRAMMER", $"{done} / {all} endings completed", 2.8f, true);
            }
            if (!skipAll) yield return Roll();
            yield return End();
        }

        /// <summary>Extras: the terms or the note again (Esc / Enter back).</summary>
        public IEnumerator Show(bool terms)
        {
            yield return Begin();
            yield return Page(terms ? "TERMS OF USE  ·  PRIVACY" : "A NOTE FROM INFI", terms ? data.terms : data.note, "Enter / Esc  back", true);
            yield return End();
        }

        private IEnumerator Begin()
        {
            Running = true;
            skipAll = false;
            root.SetActive(true);
            card.SetActive(false);
            panel.SetActive(false);
            roll.SetActive(false);
            yield return Fade(group, 0f, 1f, 0.25f);
        }

        private IEnumerator End()
        {
            yield return Fade(group, 1f, 0f, 0.4f);
            root.SetActive(false);
            Running = false;
        }

        private IEnumerator Splash() => Card("A game by", data.studio, 2.0f, false);

        /// <summary>A centred title card: fades in, holds (a key moves on), fades out.</summary>
        private IEnumerator Card(string top, string bottom, float hold, bool bigFirst)
        {
            if (skipAll) yield break;
            card.SetActive(true);
            // "A game by / INFI": small over big; "THE UNLIKELY PROGRAMMER / 1 / 4 endings": big over small
            big.text = bigFirst ? top : bottom;
            small.text = bigFirst ? bottom : top;
            big.rectTransform.anchoredPosition = new Vector2(0, bigFirst ? 18 : -10);
            small.rectTransform.anchoredPosition = new Vector2(0, bigFirst ? -34 : 42);
            big.fontSize = bigFirst ? 34 : 64;
            yield return Fade(cardGroup, 0f, 1f, 0.7f);
            for (float t = 0; t < hold; t += Time.unscaledDeltaTime)
            {
                if (Skip()) { skipAll = true; break; }
                if (Next()) break;
                yield return null;
            }
            yield return Fade(cardGroup, 1f, 0f, 0.5f);
            card.SetActive(false);
        }

        /// <summary>A page of text in a panel; waits for Enter (or Esc when escapable).</summary>
        private IEnumerator Page(string title, string[] lines, string hint, bool escapable)
        {
            panel.SetActive(true);
            panelTitle.text = title;
            panelBody.text = string.Join("\n", lines ?? new string[0]);
            panelHint.text = hint;
            panelScroll.verticalNormalizedPosition = 1f;
            yield return null;
            while (true)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.pageDownKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) MenuKit.ScrollBy(panelScroll, -0.25f);
                    if (kb.pageUpKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) MenuKit.ScrollBy(panelScroll, 0.25f);
                    var mouse = Mouse.current;
                    bool go = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame
                              || (mouse != null && mouse.leftButton.wasPressedThisFrame);
                    if (go || (escapable && kb.escapeKey.wasPressedThisFrame)) { Sfx.Play(Sfx.Id.Confirm); break; }
                }
                yield return null;
            }
            panel.SetActive(false);
        }

        /// <summary>The credits roll up the screen; Enter speeds it up, Esc ends it.</summary>
        private IEnumerator Roll()
        {
            // place the text below the screen before it's ever drawn (it used to show centred for a frame, then jump)
            rollText.text = string.Join("\n", data.credits ?? new string[0]);
            float h = UiKit.TextHeight(rollText, rollText.text, 600f) + 20f;
            var rt = rollText.rectTransform;
            rt.sizeDelta = new Vector2(600f, h);
            float half = Mathf.Max(300f, ((RectTransform)root.transform).rect.height / 2f);
            float from = -half - h / 2f, to = half + h / 2f, y = from;
            rt.anchoredPosition = new Vector2(0, y);
            roll.SetActive(true);
            yield return null;
            while (y < to)
            {
                if (Skip()) break;
                var kb = Keyboard.current;
                bool fast = kb != null && (kb.enterKey.isPressed || kb.spaceKey.isPressed || kb.eKey.isPressed);
                y += Time.unscaledDeltaTime * (fast ? 160f : 42f);
                rt.anchoredPosition = new Vector2(0, y);
                if (y > h / 2f - 20f) break;   // the last line (thanks) has reached the middle: it stays there a moment
                yield return null;
            }
            for (float t = 0; t < 2.2f && !Skip() && !Next(); t += Time.unscaledDeltaTime) yield return null;
            roll.SetActive(false);
        }

        // ------------------------------------------------------------------ input and fading

        private static bool Skip()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        private static bool Next()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        }

        private static IEnumerator Fade(CanvasGroup g, float from, float to, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                g.alpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            g.alpha = to;
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "SequenceCanvas", 60);
            var bg = UiKit.Box("Sequence", canvas.transform, new Color(0.02f, 0.025f, 0.03f, 1f), Vector2.zero, Vector2.one);
            bg.raycastTarget = true;
            root = bg.gameObject;
            group = root.AddComponent<CanvasGroup>();

            // title cards
            var c = new GameObject("Card", typeof(RectTransform));
            c.transform.SetParent(root.transform, false);
            UiKit.Rect(c, Vector2.zero, Vector2.one);
            card = c;
            cardGroup = c.AddComponent<CanvasGroup>();
            big = UiKit.Label("Big", c.transform, UiKit.UiFont, 64, UiKit.Lime, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter,
                FontStyle.Bold, new Vector2(-460, -40), new Vector2(460, 40));
            small = UiKit.Label("Small", c.transform, UiKit.UiFont, 16, UiKit.Paper, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter,
                FontStyle.Normal, new Vector2(-460, -14), new Vector2(460, 14));

            // a page of text (terms, the note)
            var fill = UiKit.Panel("Page", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, -220), new Vector2(300, 220),
                MenuKit.Panel, UiKit.LimeDim);
            panel = fill.transform.parent.gameObject;
            panelTitle = UiKit.Label("Title", fill.transform, UiKit.UiFont, 18, UiKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft,
                FontStyle.Bold, new Vector2(22, -46), new Vector2(-22, -12));
            panelBody = MenuKit.ScrollText("Body", fill.transform, UiKit.UiFont, 13, UiKit.Paper, Vector2.zero, Vector2.one,
                new Vector2(22, 44), new Vector2(-18, -54), out panelScroll);
            panelBody.lineSpacing = 1.1f;
            panelHint = UiKit.Label("Hint", fill.transform, UiKit.UiFont, 11, MenuKit.Dim, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleRight,
                FontStyle.Bold, new Vector2(22, 10), new Vector2(-22, 36));

            // the credits roll
            var r = new GameObject("Roll", typeof(RectTransform), typeof(RectMask2D));
            r.transform.SetParent(root.transform, false);
            UiKit.Rect(r, Vector2.zero, Vector2.one);
            roll = r;
            rollText = UiKit.Label("Credits", r.transform, UiKit.UiFont, 15, UiKit.Paper, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                TextAnchor.MiddleCenter, FontStyle.Normal, new Vector2(-300, -400), new Vector2(300, 400));
            rollText.lineSpacing = 1.25f;

            root.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            BootDone = false;
            PendingCredits = false;
            SkipForTests = false;
        }
    }
}

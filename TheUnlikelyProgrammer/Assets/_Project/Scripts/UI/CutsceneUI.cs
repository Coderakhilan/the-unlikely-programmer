using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>One illustration and the lines spoken over it. No image = a black title card ("Elsewhere...").</summary>
    [Serializable]
    public class Shot
    {
        public string image;      // ArtLibrary name, e.g. "capitalist_ending"; empty for a black card
        public string[] lines;    // "Betty[sad]: text" shows Betty's portrait; a plain line is narration
        public Shot(string image, params string[] lines) { this.image = image; this.lines = lines; }
    }

    /// <summary>A whole sequence (the intro, an ending), loaded from Data/Dialogue/cutscenes.json.</summary>
    [Serializable]
    public class Sequence
    {
        public string id;
        public string title;      // shown at the end, e.g. "THE UNLIKELY PROGRAMMER"; may be empty
        public Shot[] shots;
    }

    /// <summary>
    /// Full-screen story scenes: the opening, mid-game scenes and the endings. Each line is shown in a band at the
    /// bottom with the speaker's portrait and name (or as narration). E / Space / Enter / click advances, Esc skips.
    /// </summary>
    public class CutsceneUI : MonoBehaviour
    {
        private const float Fade = 0.6f;
        private const float CharsPerSecond = 42f;
        private static readonly Regex SpeakerPrefix = new Regex(@"^([A-Za-z][A-Za-z ]{0,20}?)(?:\[([a-z_]+)\])?:\s+(.*)$", RegexOptions.Singleline);

        public static CutsceneUI Instance { get; private set; }
        public bool IsPlaying { get; private set; }

        private CanvasGroup group, imageGroup;
        private Image picture, face;
        private GameObject band, faceFrame, namePlate;
        private Text caption, speaker, hint, title, card;
        private RectTransform captionRect;
        private bool advance, skip;

        private void Awake()
        {
            Instance = this;
            Build();
            group.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Play(Sequence seq, Action onDone = null)
        {
            if (seq == null) { onDone?.Invoke(); return; }
            Play(seq.shots, onDone, seq.title);
        }

        public void Play(Shot[] shots, Action onDone = null, string endTitle = null)
        {
            if (IsPlaying) return;
            StartCoroutine(Run(shots, onDone, endTitle));
        }

        /// <summary>
        /// The opening: black from the very first frame (no glimpse of the room behind it), then the pictures.
        /// </summary>
        public void PlayFromBlack(Sequence seq, Action onDone = null)
        {
            if (seq == null || IsPlaying) { onDone?.Invoke(); return; }
            group.gameObject.SetActive(true);
            group.alpha = 1f;
            fromBlack = true;
            Play(seq.shots, onDone, seq.title);
        }

        private bool fromBlack;

        private void Update()
        {
            if (!IsPlaying) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            if ((kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                || (mouse != null && mouse.leftButton.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame))
                advance = true;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame))
                skip = true;
        }

        private IEnumerator Run(Shot[] shots, Action onDone, string endTitle)
        {
            IsPlaying = true;
            skip = false;
            group.gameObject.SetActive(true);
            if (fromBlack) { fromBlack = false; group.alpha = 1f; }
            else yield return FadeGroup(group, 0f, 1f);

            foreach (var shot in shots)
            {
                if (skip) break;
                bool isCard = string.IsNullOrEmpty(shot.image);
                picture.sprite = isCard ? null : ArtLibrary.Get(shot.image);
                picture.enabled = picture.sprite != null;
                if (!isCard) Unlikely.Core.Profile.SeeArt(shot.image);   // Extras shows it from now on
                band.SetActive(false);
                card.text = "";
                yield return FadeGroup(imageGroup, 0f, 1f);

                string who = "";
                foreach (var raw in shot.lines)
                {
                    if (skip) break;
                    string clean = Unlikely.Core.Music.ApplyCues(raw);   // {music:...} cues start tracks; never shown
                    string line = clean, expression = null;
                    var m = SpeakerPrefix.Match(clean);
                    // a line without a "Name:" keeps the previous speaker (within this shot)
                    if (m.Success) { who = m.Groups[1].Value.Trim(); expression = m.Groups[2].Success ? m.Groups[2].Value : null; line = m.Groups[3].Value; }

                    Text target;
                    if (isCard) { target = card; }
                    else
                    {
                        band.SetActive(true);
                        ShowSpeaker(who, expression);
                        target = caption;
                    }

                    advance = false;
                    float shown = 0f;
                    while (shown < line.Length && !skip)
                    {
                        if (advance) { advance = false; shown = line.Length; }
                        int before = (int)shown;
                        shown = Mathf.Min(line.Length, shown + Time.unscaledDeltaTime * CharsPerSecond);
                        target.text = line.Substring(0, (int)shown);
                        if ((int)shown > before && (int)shown % 2 == 0 && char.IsLetterOrDigit(line[(int)shown - 1]))
                            Unlikely.Core.Sfx.Voice(target == caption ? speaker.text : null);
                        yield return null;
                    }
                    target.text = line;
                    hint.enabled = true;
                    while (!advance && !skip) yield return null;
                    hint.enabled = false;
                    advance = false;
                }
                if (!skip) yield return FadeGroup(imageGroup, 1f, 0f);
            }

            band.SetActive(false);
            if (!string.IsNullOrEmpty(endTitle) && !skip)
            {
                imageGroup.alpha = 0f;
                title.text = endTitle;
                title.enabled = true;
                advance = false;
                float t = 0f;
                while (!advance && !skip && t < 6f) { t += Time.unscaledDeltaTime; yield return null; }
                title.enabled = false;
            }

            yield return FadeGroup(group, 1f, 0f);
            group.gameObject.SetActive(false);
            imageGroup.alpha = 0f;
            IsPlaying = false;
            onDone?.Invoke();
        }

        private void ShowSpeaker(string who, string expression)
        {
            var sprite = string.IsNullOrEmpty(who) ? null : ArtLibrary.Portrait(who, expression);
            faceFrame.SetActive(sprite != null);
            if (sprite != null) face.sprite = sprite;
            namePlate.SetActive(!string.IsNullOrEmpty(who));
            speaker.text = who;
            bool narration = string.IsNullOrEmpty(who);
            caption.fontStyle = narration ? FontStyle.Italic : FontStyle.Normal;
            caption.alignment = narration ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            captionRect.offsetMin = new Vector2(sprite != null ? 170 : 90, 10);
        }

        private static IEnumerator FadeGroup(CanvasGroup g, float from, float to)
        {
            for (float t = 0f; t < Fade; t += Time.unscaledDeltaTime)
            {
                g.alpha = Mathf.Lerp(from, to, t / Fade);
                yield return null;
            }
            g.alpha = to;
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "CutsceneCanvas", 200);
            canvas.pixelPerfect = false;
            var bg = UiKit.Box("Cutscene", canvas.transform, Color.black, Vector2.zero, Vector2.one);
            bg.raycastTarget = true;
            group = bg.gameObject.AddComponent<CanvasGroup>();

            var imgRoot = new GameObject("Image", typeof(RectTransform));
            imgRoot.transform.SetParent(bg.transform, false);
            UiKit.Rect(imgRoot, Vector2.zero, Vector2.one);
            imageGroup = imgRoot.AddComponent<CanvasGroup>();
            imageGroup.alpha = 0f;
            picture = UiKit.Box("Picture", imgRoot.transform, Color.white, Vector2.zero, Vector2.one);
            picture.preserveAspect = true;
            card = UiKit.Label("Card", imgRoot.transform, UiKit.UiFont, 26, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Italic);

            // speech band at the bottom: portrait, name plate, line
            var b = UiKit.Box("Band", bg.transform, new Color(0, 0, 0, 0.78f), Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 118));
            band = b.gameObject;
            UiKit.Box("Line", b.transform, UiKit.LimeDim, new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero);

            var pf = UiKit.Framed("Portrait", b.transform, UiKit.GlassLight, UiKit.Edge, Vector2.zero, Vector2.zero, new Vector2(40, 8), new Vector2(158, 116));
            faceFrame = pf.transform.parent.gameObject;
            face = UiKit.Box("Face", pf.transform, Color.white, Vector2.zero, Vector2.one);
            face.preserveAspect = true;

            var plate = UiKit.Framed("Name", b.transform, UiKit.Lime, UiKit.Lime, new Vector2(0, 1), new Vector2(0, 1), new Vector2(170, -8), new Vector2(320, 16));
            namePlate = plate.transform.parent.gameObject;
            speaker = UiKit.Label("Speaker", plate.transform, UiKit.UiFont, 14, UiKit.Amber, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            speaker.color = UiKit.OnLime;

            caption = UiKit.Label("Caption", b.transform, UiKit.UiFont, 18, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Normal,
                new Vector2(170, 10), new Vector2(-90, -16));
            caption.lineSpacing = 1.15f;
            captionRect = caption.rectTransform;

            hint = UiKit.Label("Hint", bg.transform, UiKit.UiFont, 11, UiKit.Muted, Vector2.zero, new Vector2(1, 0), TextAnchor.LowerRight, FontStyle.Normal,
                new Vector2(0, 6), new Vector2(-14, 24));
            hint.text = "<color=#D4F03C>E</color>  continue      <color=#D4F03C>Esc</color>  skip";
            hint.enabled = false;

            title = UiKit.Label("Title", bg.transform, UiKit.UiFont, 34, UiKit.Lime, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.enabled = false;
            band.SetActive(false);
        }
    }
}

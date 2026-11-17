using System;
using Unlikely.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Extras > Soundtrack: every track heard in the game so far (the rest are "???" until they play somewhere).
    /// ↑↓ choose, Enter / click play (again: pause), ←→ skip 5 s, click or drag the bar to scrub, Esc back
    /// (the title music comes back).
    /// </summary>
    public class SoundtrackPage
    {
        public GameObject Root { get; }
        public bool IsOpen => Root.activeSelf;
        /// <summary>The track picked here (tests).</summary>
        public string Playing => playing;

        private readonly MenuList list;
        private readonly Action onBack;
        private readonly Text count, nowTitle, nowWhere, time, pauseLabel;
        private readonly RectTransform bar;
        private readonly Image fill, knob;
        private string playing;

        public SoundtrackPage(Transform canvas, Action back)
        {
            onBack = back;
            var frame = UiKit.Framed("Soundtrack", canvas, MenuKit.Panel, MenuKit.Lime, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-440, -240), new Vector2(440, 240));
            Root = frame.transform.parent.gameObject;
            UiKit.Label("Title", frame.transform, UiKit.UiFont, 22, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(20, -44), new Vector2(-20, -8)).text = "SOUNDTRACK";
            count = UiKit.Label("Count", frame.transform, UiKit.UiFont, 12, MenuKit.Dim, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleRight,
                FontStyle.Normal, new Vector2(20, -44), new Vector2(-20, -8));
            UiKit.Label("Keys", frame.transform, UiKit.UiFont, 11, MenuKit.Dim, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleLeft, FontStyle.Normal,
                new Vector2(20, 6), new Vector2(-20, 28)).text = "↑↓ choose    Enter play / pause    ← → skip 5 s    drag the bar to scrub    Esc back";

            // the track list, left
            list = new MenuList(frame.transform, new Vector2(0, 0), new Vector2(0.5f, 1), 26f, 14);
            list.Root.offsetMin = new Vector2(16, 34);
            list.Root.offsetMax = new Vector2(-8, -52);

            // now playing, right
            var box = UiKit.Panel("NowPlaying", frame.transform, new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(8, 34), new Vector2(-16, -52),
                new Color(0, 0, 0, 0.35f), UiKit.Edge, false);
            UiKit.Label("Label", box.transform, UiKit.UiFont, 10, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(18, -34), new Vector2(-18, -12)).text = ">_  NOW PLAYING";
            nowTitle = UiKit.Label("Track", box.transform, UiKit.UiFont, 22, UiKit.Paper, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.UpperLeft, FontStyle.Bold,
                new Vector2(18, -100), new Vector2(-18, -40));
            nowWhere = UiKit.Label("Where", box.transform, UiKit.UiFont, 12, MenuKit.Dim, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.UpperLeft, FontStyle.Italic,
                new Vector2(18, -130), new Vector2(-18, -100));
            // what the highlighted row is (a found track's place, or that it's still hidden)
            list.Note = UiKit.Label("Note", box.transform, UiKit.UiFont, 11, MenuKit.Dim, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleLeft,
                FontStyle.Italic, new Vector2(18, 16), new Vector2(-18, 40));

            // the scrub bar
            var track = UiKit.RoundBox("Bar", box.transform, new Color(1, 1, 1, 0.12f), new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(18, 96), new Vector2(-18, 108), 4f);
            track.raycastTarget = true;
            bar = track.rectTransform;
            fill = UiKit.RoundBox("Fill", track.transform, MenuKit.Lime, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero, 4f);
            fill.raycastTarget = false;
            knob = UiKit.RoundBox("Knob", track.transform, UiKit.Paper, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-7, -9), new Vector2(7, 9), 3f);
            knob.raycastTarget = false;
            var trigger = track.gameObject.AddComponent<EventTrigger>();
            foreach (var kind in new[] { EventTriggerType.PointerDown, EventTriggerType.Drag })
            {
                var e = new EventTrigger.Entry { eventID = kind };
                e.callback.AddListener(d => { if (d is PointerEventData p) ScrubTo(p); });
                trigger.triggers.Add(e);
            }
            time = UiKit.Label("Time", box.transform, UiKit.Mono, 12, UiKit.Paper, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleRight, FontStyle.Normal,
                new Vector2(18, 66), new Vector2(-18, 88));
            pauseLabel = UiKit.Label("State", box.transform, UiKit.UiFont, 12, MenuKit.Lime, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(18, 66), new Vector2(-18, 88));
            Root.SetActive(false);
        }

        public void Open()
        {
            list.Clear();
            int found = 0;
            for (int i = 0; i < Soundtrack.All.Length; i++)
            {
                var t = Soundtrack.All[i];
                bool heard = Profile.Heard(t.id);
                if (heard) found++;
                int idx = i;
                list.Add($"{i + 1:00}   {(heard ? t.title : "???")}", () => Play(idx), heard,
                    heard ? "Heard: " + t.where : "Not found yet. It plays somewhere in the game.");
            }
            count.text = $"{found} / {Soundtrack.All.Length} found";
            playing = null;
            Root.SetActive(true);
            list.SelectFirstEnabled();
            ShowNow();
        }

        /// <summary>Back to Extras: the title music takes over again.</summary>
        public void Close()
        {
            Music.SetPreview(null);
            playing = null;
            Root.SetActive(false);
        }

        public void Play(int i)
        {
            var t = Soundtrack.All[i];
            if (!Profile.Heard(t.id)) return;
            if (playing == t.id) Music.PreviewPaused = !Music.PreviewPaused;   // the same track again: pause / carry on
            else { playing = t.id; Music.SetPreview(t.id); }
            ShowNow();
        }

        private void ShowNow()
        {
            var t = Array.Find(Soundtrack.All, x => x.id == playing);
            nowTitle.text = t != null ? t.title : "Pick a track";
            nowWhere.text = t != null ? "Heard: " + t.where : "";
        }

        private void ScrubTo(PointerEventData p)
        {
            if (playing == null || !Music.PreviewReady) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(bar, p.position, p.pressEventCamera, out var local)) return;
            float f = Mathf.InverseLerp(bar.rect.xMin, bar.rect.xMax, local.x);
            Music.Seek(f * Music.Length);
        }

        private static string Clock(float s) => $"{(int)(s / 60f)}:{(int)(s % 60f):00}";

        /// <summary>Call from the owner's Update while open.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (MenuKit.Back(kb)) { Sfx.Play(Sfx.Id.Back); Close(); onBack?.Invoke(); return; }
                if (playing != null && Music.PreviewReady)
                {
                    if (MenuKit.Left(kb)) Music.Seek(Music.Position - 5f);
                    if (MenuKit.Right(kb)) Music.Seek(Music.Position + 5f);
                }
                list.Tick();
            }
            bool live = playing != null && Music.PreviewReady;
            float len = live ? Music.Length : 0f, pos = live ? Music.Position : 0f;
            float f = len > 0f ? Mathf.Clamp01(pos / len) : 0f;
            fill.rectTransform.anchorMax = new Vector2(f, 1);
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(f, 0.5f);
            knob.enabled = live;
            time.text = live ? Clock(pos) + "  /  " + Clock(len) : "";
            pauseLabel.text = playing == null ? "" : Music.PreviewPaused ? "||  PAUSED" : "▶  PLAYING";
        }
    }
}

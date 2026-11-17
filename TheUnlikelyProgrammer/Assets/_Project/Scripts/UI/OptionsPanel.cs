using System;
using System.Collections.Generic;
using Unlikely.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// The options screen (title and pause menu): resolution, display mode, V-sync, frame limit, SFX and music volume.
    /// The title also adds links under them ("Terms of use · Privacy", "A note from infi").
    /// </summary>
    public class OptionsPanel
    {
        private const float RowHeight = 32f;

        public GameObject Root { get; }
        public bool IsOpen => Root.activeSelf;

        private readonly MenuList list;
        private readonly Action onBack;
        private readonly RectTransform frame;
        private readonly List<(string label, Action open, string note)> links = new List<(string, Action, string)>();
        private int resIndex;
        private List<Vector2Int> resolutions;

        public OptionsPanel(Transform parent, Action back)
        {
            onBack = back;
            var fill = UiKit.Framed("Options", parent, MenuKit.Panel, MenuKit.Lime, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-250, -186), new Vector2(250, 186));
            Root = fill.transform.parent.gameObject;
            frame = (RectTransform)Root.transform;
            UiKit.Label("Title", fill.transform, UiKit.UiFont, 24, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(20, -46), new Vector2(-20, -10)).text = "OPTIONS";
            list = new MenuList(fill.transform, new Vector2(0, 0), new Vector2(1, 1), RowHeight, 15);
            list.Root.offsetMin = new Vector2(16, 50);
            list.Root.offsetMax = new Vector2(-16, -56);
            list.Note = UiKit.Label("Note", fill.transform, UiKit.UiFont, 11, MenuKit.Dim, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleLeft,
                FontStyle.Italic, new Vector2(24, 12), new Vector2(-24, 44));
            BuildRows();
            Root.SetActive(false);
        }

        /// <summary>A link row under the settings (opens something, then comes back here).</summary>
        public void AddLink(string label, Action open, string note)
        {
            links.Add((label, open, note));
            list.Clear();
            BuildRows();
            // taller by one row per link, still centred where it was
            float h = 372f + links.Count * RowHeight;
            Vector2 centre = (frame.offsetMin + frame.offsetMax) / 2f;
            frame.offsetMin = centre - new Vector2(250, h / 2f);
            frame.offsetMax = centre + new Vector2(250, h / 2f);
        }

        private void BuildRows()
        {
            list.AddSetting("Resolution", () => resolutions == null || resolutions.Count == 0 ? "-" : $"{resolutions[resIndex].x} × {resolutions[resIndex].y}",
                d => { resIndex = Wrap(resIndex + d, resolutions.Count); var r = resolutions[resIndex]; GameSettings.Width = r.x; GameSettings.Height = r.y; },
                "Applies when you press Apply.");
            list.AddSetting("Display", () => GameSettings.ModeNames[GameSettings.Mode],
                d => GameSettings.Mode = Wrap(GameSettings.Mode + d, GameSettings.ModeNames.Length), "Fullscreen, borderless window or a window.");
            list.AddSetting("V-sync", () => GameSettings.VSync ? "On" : "Off", d => GameSettings.VSync = !GameSettings.VSync,
                "On: no flickering lines across the screen, about 60 fps. Off: the frame limit below applies instead.");
            list.AddSetting("Frame limit", () => GameSettings.FrameLimit + " fps",
                d => GameSettings.FrameLimit = GameSettings.FrameLimits[Wrap(Array.IndexOf(GameSettings.FrameLimits, GameSettings.FrameLimit) + d, GameSettings.FrameLimits.Length)],
                "Used when V-sync is off. 60 is plenty for this game; lower it to keep a laptop cooler.");
            list.AddSetting("SFX volume", () => Mathf.RoundToInt(GameSettings.Volume * 100) + "%",
                d => GameSettings.Volume = Mathf.Clamp01(Mathf.Round(GameSettings.Volume * 10 + d) / 10f), "Sound effects, footsteps and voices. Click ◀ to lower, ▶ to raise.");
            list.AddSetting("Music volume", () => Mathf.RoundToInt(GameSettings.MusicVolume * 100) + "%",
                d => GameSettings.MusicVolume = Mathf.Clamp01(Mathf.Round(GameSettings.MusicVolume * 10 + d) / 10f), "The soundtrack (changes as you adjust it).");
            foreach (var l in links) list.Add(l.label, l.open, true, l.note);
            list.Add("Apply", () => { GameSettings.Apply(); GameSettings.Save(); HudUI.Instance?.Toast("Options saved"); }, true, "Save and apply these settings.");
            list.Add("Back", Back, true, "Esc also goes back (unsaved changes are dropped).");
        }

        private static int Wrap(int i, int n) => n <= 0 ? 0 : ((i % n) + n) % n;

        public void Open()
        {
            GameSettings.Load();
            resolutions = GameSettings.Resolutions();
            resIndex = Mathf.Max(0, resolutions.FindIndex(r => r.x == GameSettings.Width && r.y == GameSettings.Height));
            if (resolutions.Count > 0) { GameSettings.Width = resolutions[resIndex].x; GameSettings.Height = resolutions[resIndex].y; }
            Root.SetActive(true);
            list.Select(0);
        }

        /// <summary>Back to whatever opened it, keeping applied settings only.</summary>
        public void Back()
        {
            GameSettings.Load();   // drop anything not applied
            Root.SetActive(false);
            onBack?.Invoke();
        }

        /// <summary>Call from the owner's Update while open.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Back(); return; }
            list.Tick();
        }
    }
}

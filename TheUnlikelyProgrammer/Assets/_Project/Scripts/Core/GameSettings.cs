using System.Collections.Generic;
using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// Options: resolution, display mode, frame limit and volume. Stored in PlayerPrefs and applied at start-up.
    /// V-sync on by default (no tearing), paced to about 60 fps; with it off, a fixed frame limit. Either keeps the PC cool.
    /// </summary>
    public static class GameSettings
    {
        public static readonly string[] ModeNames = { "Fullscreen", "Borderless window", "Windowed" };
        private static readonly FullScreenMode[] Modes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        public static readonly int[] FrameLimits = { 30, 60, 120 };

        public static int Width { get; set; }
        public static int Height { get; set; }
        public static int Mode { get; set; } = 1;
        public static int FrameLimit { get; set; } = 60;
        public static float Volume { get; set; } = 0.8f;
        public static float MusicVolume { get; set; } = 0.6f;
        /// <summary>
        /// V-sync: frames wait for the screen, so no tearing (the flickering lines across the screen). On a fast screen
        /// it waits every 2nd/3rd refresh, keeping the game near 60 fps (cool laptop). Off: the frame limit applies instead.
        /// </summary>
        public static bool VSync { get; set; } = true;

        /// <summary>Distinct resolutions the display offers (largest last), always including the current one.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 640 && !list.Contains(v)) list.Add(v);
            }
            var cur = new Vector2Int(Width > 0 ? Width : Screen.width, Height > 0 ? Height : Screen.height);
            if (!list.Contains(cur)) list.Add(cur);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public static void Load()
        {
            Width = PlayerPrefs.GetInt("opt_w", Screen.currentResolution.width);
            Height = PlayerPrefs.GetInt("opt_h", Screen.currentResolution.height);
            Mode = Mathf.Clamp(PlayerPrefs.GetInt("opt_mode", 1), 0, Modes.Length - 1);
            FrameLimit = PlayerPrefs.GetInt("opt_fps", 60);
            if (System.Array.IndexOf(FrameLimits, FrameLimit) < 0) FrameLimit = 60;   // older saves had "unlimited"
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat("opt_volume", 0.8f));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("opt_music", 0.6f));
            VSync = PlayerPrefs.GetInt("opt_vsync", 1) == 1;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("opt_w", Width);
            PlayerPrefs.SetInt("opt_h", Height);
            PlayerPrefs.SetInt("opt_mode", Mode);
            PlayerPrefs.SetInt("opt_fps", FrameLimit);
            PlayerPrefs.SetFloat("opt_volume", Volume);
            PlayerPrefs.SetFloat("opt_music", MusicVolume);
            PlayerPrefs.SetInt("opt_vsync", VSync ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void Apply()
        {
            ApplyFrameRate();
            AudioListener.volume = 1f;   // SFX and music each have their own volume
#if !UNITY_EDITOR
            if (Width > 0 && Height > 0) Screen.SetResolution(Width, Height, Modes[Mode]);
#endif
        }

        /// <summary>
        /// Unity ignores the frame limit while V-sync is on, and the editor ignores V-sync (so it would run uncapped and
        /// hot): in the editor and test runs only the frame limit is used.
        /// </summary>
        private static void ApplyFrameRate()
        {
            bool vsync = VSync && !Application.isEditor && !Application.isBatchMode;
            QualitySettings.vSyncCount = vsync ? VSyncInterval() : 0;
            Application.targetFrameRate = Application.isBatchMode ? -1 : vsync ? -1 : FrameLimit;   // test runs play sped up: no cap
        }

        /// <summary>Wait for every Nth refresh so the game runs close to 60 fps: 1 on a 60 Hz screen, 2 on 120/144 Hz, 4 on 240 Hz.</summary>
        private static int VSyncInterval()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (hz <= 0 || double.IsNaN(hz)) return 1;
            return Mathf.Clamp(Mathf.RoundToInt((float)(hz / 60.0)), 1, 4);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAndApply()
        {
            Load();
            Apply();
            // alt-tabbed away: the game idles at 10 frames a second instead of rendering at full speed for nobody
            Application.focusChanged -= OnFocus;
            Application.focusChanged += OnFocus;
        }

        private static void OnFocus(bool focused)
        {
            if (Application.isBatchMode) return;
            if (!focused) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 10; }
            else ApplyFrameRate();
        }
    }
}

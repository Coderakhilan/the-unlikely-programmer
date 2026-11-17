using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// What carries over between playthroughs: the 8 stars (an ending reached on each difficulty, and each of the four
    /// endings) and which pictures have been seen (Extras shows only those).
    /// </summary>
    public static class Profile
    {
        public sealed class Star
        {
            public readonly string id, title, hint;
            public Star(string id, string title, string hint) { this.id = id; this.title = title; this.hint = hint; }
        }

        public const string EndAccomplice = "ending_accomplice";
        public const string EndLastQuery = "ending_last_query";
        public const string EndCapitalist = "ending_capitalist";
        public const string EndSamaritan = "ending_good_samaritan";

        public static readonly Star[] Stars =
        {
            new Star("diff_vibe", "Vibe Programmer", "Finish the game on Vibe Programmer."),
            new Star("diff_unlikely", "Unlikely Programmer", "Finish the game on Unlikely Programmer."),
            new Star("diff_likely", "Likely Programmer", "Finish the game on Likely Programmer."),
            new Star("diff_leet", "Leet Programmer", "Finish the game on Leet Programmer."),
            new Star(EndAccomplice, "The Accomplice", "Reach an ending."),
            new Star(EndLastQuery, "The Last Query", "Reach an ending."),
            new Star(EndCapitalist, "The Capitalist", "Reach an ending."),
            new Star(EndSamaritan, "The Good Samaritan", "Reach an ending."),
        };

        [Serializable]
        private class Data
        {
            public List<string> stars = new List<string>();
            public List<string> art = new List<string>();
            public List<string> tracks = new List<string>();   // soundtrack tracks heard (Extras > Soundtrack)
        }

        private static Data data;

        /// <summary>Where the file goes (tests point this at a temp folder).</summary>
        public static string Folder { get; set; }
        private static string FilePath => Path.Combine(string.IsNullOrEmpty(Folder) ? SaveSystem.RealFolder : Folder, "profile.json");

        private static Data D
        {
            get
            {
                if (data != null) return data;
                try { data = File.Exists(FilePath) ? JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) : null; }
                catch (Exception e) { Debug.LogWarning("Profile: couldn't read: " + e.Message); }
                return data ??= new Data();
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(D, true));
            }
            catch (Exception e) { Debug.LogWarning("Profile: couldn't write: " + e.Message); }
        }

        /// <summary>Forget the cached copy (tests, or after changing Folder).</summary>
        public static void Reload() => data = null;

        /// <summary>Hard reset: every star and every picture seen, gone.</summary>
        public static void Erase()
        {
            data = new Data();
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception e) { Debug.LogWarning("Profile: couldn't delete: " + e.Message); }
        }

        /// <summary>A track played for the first time: it shows up in Extras > Soundtrack.</summary>
        public static void HearTrack(string id)
        {
            if (string.IsNullOrEmpty(id) || D.tracks.Contains(id)) return;
            D.tracks.Add(id);
            Save();
        }

        public static bool Heard(string id) => D.tracks.Contains(id);

        public static bool HasStar(string id) => D.stars.Contains(id);
        public static int StarCount { get { int n = 0; foreach (var s in Stars) if (HasStar(s.id)) n++; return n; } }

        public static string DifficultyStar(Difficulty d) => "diff_" + d.ToString().ToLowerInvariant();

        /// <summary>An ending's pictures finished: its star, and the star for finishing on this difficulty.</summary>
        public static void EndingReached(string endingId, Difficulty difficulty)
        {
            bool changed = false;
            foreach (var id in new[] { endingId, DifficultyStar(difficulty) })
                if (Array.Exists(Stars, s => s.id == id) && !D.stars.Contains(id)) { D.stars.Add(id); changed = true; }
            if (changed) Save();
        }

        public static bool Seen(string image) => !string.IsNullOrEmpty(image) && D.art.Contains(image.ToLowerInvariant());

        public static void SeeArt(string image)
        {
            if (string.IsNullOrEmpty(image) || Seen(image)) return;
            D.art.Add(image.ToLowerInvariant());
            Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            data = null;
            Folder = null;
        }
    }
}

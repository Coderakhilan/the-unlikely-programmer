using System;
using System.Collections.Generic;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.Story
{
    /// <summary>
    /// The illustrated sequences (opening, endings...), loaded from Data/Dialogue/cutscenes.json so the
    /// script can be rewritten without touching code. Image names are files in Art/Processed/cutscenes.
    /// </summary>
    public static class StoryScenes
    {
        public const string Intro = "intro";
        public const string EndingAccomplice = "ending_accomplice";
        public const string EndingLastQuery = "ending_last_query";
        public const string EndingCapitalist = "ending_capitalist";
        public const string EndingGoodSamaritan = "ending_good_samaritan";

        [Serializable] private class File { public Sequence[] sequences; }

        private static readonly Dictionary<string, Sequence> sequences = new Dictionary<string, Sequence>();

        public static void Load(TextAsset json)
        {
            if (json == null) return;
            var file = JsonUtility.FromJson<File>(json.text);
            if (file?.sequences == null) return;
            foreach (var s in file.sequences) sequences[s.id] = s;
        }

        public static Sequence Get(string id)
        {
            if (sequences.TryGetValue(id, out var s)) return s;
            Debug.LogWarning("Cutscene missing: " + id);
            return null;
        }

        public static IEnumerable<Sequence> All => sequences.Values;
    }
}

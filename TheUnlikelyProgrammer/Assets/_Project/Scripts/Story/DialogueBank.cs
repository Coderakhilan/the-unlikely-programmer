using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unlikely.Story
{
    /// <summary>
    /// All story text for a floor, loaded from a JSON file in Data/Dialogue so it can be rewritten without code.
    /// Each entry is an id and its lines. A line can start with "Speaker[expression]: " to set who talks.
    /// </summary>
    public static class DialogueBank
    {
        [Serializable] private class Entry { public string id; public string[] lines; }
        [Serializable] private class File { public Entry[] entries; }

        private static readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>();

        public static void Load(TextAsset json)
        {
            if (json == null) return;
            var file = JsonUtility.FromJson<File>(json.text);
            if (file?.entries == null) return;
            foreach (var e in file.entries) entries[e.id] = e.lines ?? new string[0];
        }

        public static string[] Get(string id)
        {
            if (entries.TryGetValue(id, out var lines)) return lines;
            Debug.LogWarning("Dialogue missing: " + id);
            return new[] { "[missing dialogue: " + id + "]" };
        }

        /// <summary>First line of an entry (for phone messages and single barks).</summary>
        public static string One(string id) => Get(id)[0];

        /// <summary>Splits "Dan: text" into ("Dan", "text"); no prefix gives ("", line).</summary>
        public static (string speaker, string text) Split(string line)
        {
            int c = line.IndexOf(": ", StringComparison.Ordinal);
            if (c > 0 && c < 24 && char.IsLetter(line[0]))
            {
                string who = line.Substring(0, c);
                int b = who.IndexOf('[');
                if (b > 0) who = who.Substring(0, b);
                return (who.Trim(), line.Substring(c + 2));
            }
            return ("", line);
        }
    }
}

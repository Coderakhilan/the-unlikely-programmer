using System.Collections;
using Unlikely.Core;

namespace Unlikely.Story
{
    /// <summary>
    /// Makes the floor scripts resumable. A "beat" is a scripted bit (lines, a call, people walking off) that plays once
    /// per run: it's remembered as the flag "beat_{id}" when it finishes, so a resumed game skips it.
    /// While any beat is playing the game isn't at rest, and nothing autosaves (a save mid-beat would replay half of it).
    /// </summary>
    public static class StoryBeats
    {
        /// <summary>How many beats are playing right now.</summary>
        public static int Playing { get; private set; }

        public static bool Done(string id) => GameState.Has("beat_" + id);
        public static void Mark(string id) => GameState.Set("beat_" + id);

        /// <summary>Plays the beat if this run hasn't already, then remembers it.</summary>
        public static IEnumerator Beat(string id, IEnumerator body)
        {
            if (Done(id)) yield break;
            Playing++;
            try { yield return body; }
            finally { Playing--; }
            Mark(id);
        }

        /// <summary>A new scene: nothing is playing (a beat cut short by a reload must not block saving for ever).</summary>
        public static void Reset() => Playing = 0;
    }
}

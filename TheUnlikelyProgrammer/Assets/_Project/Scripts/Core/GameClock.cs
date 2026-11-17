using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// The in-story time shown on phones and PCs (lore only; nothing in the game waits on it).
    /// It's all one Tuesday morning: 08:02 at the opening card. Each clearance upgrade nudges the clock on a little,
    /// and inside a stage it creeps forward with play time (capped, so it never runs into the night).
    /// </summary>
    public static class GameClock
    {
        // minutes after midnight at the start of each clearance stage
        private static readonly int[] StageStart = { 8 * 60 + 2, 8 * 60 + 47, 9 * 60 + 38, 10 * 60 + 31, 11 * 60 + 26 };
        private const float RealSecondsPerMinute = 15f;
        private const int MaxDrift = 40;

        private static int stage = -1;
        private static float stageSince;

        public static int Minutes
        {
            get
            {
                int c = Mathf.Clamp(GameState.Clearance, 0, StageStart.Length - 1);
                if (c != stage) { stage = c; stageSince = Time.time; }
                int drift = Mathf.Min(MaxDrift, (int)((Time.time - stageSince) / RealSecondsPerMinute));
                return (StageStart[c] + drift) % (24 * 60);
            }
        }

        public static string Text => $"{Minutes / 60:00}:{Minutes % 60:00}";

        /// <summary>Donnie's phone battery: it's an old phone.</summary>
        public static int Battery => Mathf.Max(6, 81 - (int)(Time.time / 150f));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => stage = -1;
    }
}

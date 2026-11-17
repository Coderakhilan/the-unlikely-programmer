using System.Collections.Generic;

namespace Unlikely.Core
{
    /// <summary>
    /// Story and progress flags (e.g. "ryker_discovered", puzzle rewards). Kept deliberately simple:
    /// a set of named flags plus the clearance level. Saving/loading comes later.
    /// </summary>
    public enum Difficulty { Vibe, Unlikely, Likely, Leet }

    public static class GameState
    {
        private static readonly HashSet<string> flags = new HashSet<string>();

        public static int Clearance { get; set; }
        public static Difficulty Difficulty { get; set; } = Difficulty.Unlikely;

        /// <summary>Development switch: lets the elevator reach every floor. Turn off for the real game flow.</summary>
        public static bool DevUnlockAllFloors = false;

        /// <summary>Development switch: phone starts with Manual and the Floor 0 lessons. Turn off once the Floor 0 story is in.</summary>
        public static bool DevGiveManual = false;

        /// <summary>KODEX's Knowledge tab at computers: every difficulty but Leet.</summary>
        public static bool ManualPanelAllowed => Difficulty != Difficulty.Leet;

        public static bool HasManual => DevGiveManual || Has("has_manual");

        public static bool CanUseFloor(int floor) => DevUnlockAllFloors || floor <= Clearance;

        /// <summary>Every flag set right now (for saving).</summary>
        public static IEnumerable<string> AllFlags => flags;

        public static bool Has(string flag) => !string.IsNullOrEmpty(flag) && flags.Contains(flag);

        public static void Set(string flag)
        {
            if (!string.IsNullOrEmpty(flag)) flags.Add(flag);
        }

        public static void Unset(string flag)
        {
            if (!string.IsNullOrEmpty(flag)) flags.Remove(flag);
        }

        public static void ResetAll()
        {
            flags.Clear();
            Clearance = 0;
        }

        /// <summary>
        /// A fresh game every time Play is pressed. The editor may keep static data between plays
        /// (fast "enter play mode"), which left the USB cable, messages and flags from the last run.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            ResetAll();
            Difficulty = Difficulty.Unlikely;
            Inventory.ResetStatic();
            PhoneData.ResetStatic();
        }
    }
}

namespace Unlikely.Core
{
    /// <summary>
    /// What each difficulty changes The story, puzzles and endings are the same on all four.
    ///   Vibe:     Unlikely + The Theory of Everything, future Donnie's full guide (B to read; never leaves his pockets).
    ///   Unlikely: the standard game.
    ///   Likely:   computers aren't safe. Stay on one too long and somebody notices (caught, back to the floor's elevator).
    ///   Leet:     Likely, and KODEX never helps: no lessons, no Knowledge pages, no clue lists, no hints on wrong answers.
    /// </summary>
    public static class DifficultyInfo
    {
        /// <summary>Seconds Donnie can spend on a computer (Likely / Leet) before somebody notices.</summary>
        public const float ComputerSeconds = 60f;

        public static readonly Difficulty[] All = { Difficulty.Vibe, Difficulty.Unlikely, Difficulty.Likely, Difficulty.Leet };

        public static string Name(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Vibe: return "Vibe Programmer";
                case Difficulty.Likely: return "Likely Programmer";
                case Difficulty.Leet: return "Leet Programmer";
                default: return "Unlikely Programmer";
            }
        }

        public static string Blurb(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Vibe:
                    return "The standard game, plus a book in your pocket: The Theory of Everything, written by future Donnie. "
                         + "Every solution, every ending, step by step. Press B to read it.";
                case Difficulty.Likely:
                    return "Computers aren't safe any more. Stay on one too long and somebody notices you. "
                         + "Find what you need and get off it.";
                case Difficulty.Leet:
                    return "Computers aren't safe, and KODEX doesn't help. No lessons, no Knowledge pages, no clues, no hints. "
                         + "Just you and the SQL.";
                default:
                    return "The standard game. KODEX teaches you, computers are safe once you're on them.";
            }
        }

        public static bool ComputersWatched(Difficulty d) => d == Difficulty.Likely || d == Difficulty.Leet;
        public static bool KodexHelps(Difficulty d) => d != Difficulty.Leet;
        public static bool HasTheory(Difficulty d) => d == Difficulty.Vibe;

        public static bool ComputersWatched() => ComputersWatched(GameState.Difficulty);
        public static bool KodexHelps() => KodexHelps(GameState.Difficulty);
        public static bool HasTheory() => HasTheory(GameState.Difficulty);
    }
}

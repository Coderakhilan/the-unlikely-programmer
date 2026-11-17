namespace Unlikely.Core
{
    /// <summary>
    /// Donnie's badge and when it lets him onto each floor. The times are lore only (the game has no clock):
    /// what actually gates a floor is the clearance level. Gyaracorp has several janitors on rotating slots,
    /// which is why Donnie's access comes in time windows.
    /// </summary>
    public static class Access
    {
        public const string BadgeId = "J-0009";
        public const string JanitorNumber = "Janitor #9";

        public static readonly string[] FloorNames = { "General", "Administration", "Human Resources", "Executive Offices", "Server Room" };

        /// <summary>Can Donnie use this floor now (clearance check).</summary>
        public static bool Allowed(int floor) => GameState.CanUseFloor(floor);

        /// <summary>His access window for a floor, as the GyaraCorp app shows it.</summary>
        public static string Schedule(int floor)
        {
            int c = GameState.Clearance;
            switch (floor)
            {
                case 0: return "All day";
                case 1: return c >= 1 ? "All day" : "After hours only (19:00 - 23:00)";
                case 2: return c >= 2 ? "All day" : "After hours only (19:00 - 23:00)";
                case 3: return c >= 3 ? "Early morning (05:00 - 08:00) and evenings through midnight" : "After hours only (23:00 - 02:00)";
                default: return c >= 4 ? "Authorised" : "No access";
            }
        }

        /// <summary>What the elevator says when the badge is refused.</summary>
        public static string DeniedLine(int floor) =>
            floor >= 4
                ? $"Access denied. Badge {BadgeId} has no server room access."
                : $"Access denied. Badge {BadgeId} is cleared for {FloorNames[floor]}: {Schedule(floor).ToLowerInvariant()}.";
    }
}

namespace Unlikely.Core
{
    /// <summary>
    /// The OST in album order, for Extras > Soundtrack: a track shows up there once it has played in the game
    /// (Profile.Heard); until then it's "???", and the hint says roughly where it turns up.
    /// </summary>
    public static class Soundtrack
    {
        public sealed class Track
        {
            public string id, title, where;
            public Track(string id, string title, string where) { this.id = id; this.title = title; this.where = where; }
        }

        public static readonly Track[] All =
        {
            new Track("the_unlikely_programmer", "The Unlikely Programmer", "The title screen"),
            new Track("betty_donnie", "Betty & Donnie", "The opening"),
            new Track("another_day_at_work", "Another Day At Work", "Floor 0"),
            new Track("the_usual_suspects", "The Usual Suspects", "A call from an unknown number"),
            new Track("the_paper_trail", "The Paper Trail", "Floor 1 · Administration"),
            new Track("corporate_intrigue", "Corporate Intrigue", "Floor 2 · Human Resources"),
            new Track("threes_company", "Three's Company", "Another call from that number"),
            new Track("framed", "Framed", "When the lights go out"),
            new Track("the_intern", "The Intern", "Somebody isn't who they say they are"),
            new Track("the_people_upstairs", "The People Upstairs", "Floor 3 · Executive Offices"),
            new Track("the_man_behind_the_mop", "The Man Behind The Mop", "An ending"),
            new Track("just_donnie", "Just Donnie", "An ending"),
            new Track("the_final_query", "The Final Query", "An ending"),
            new Track("honest_days_work", "Honest Day's Work", "The server room"),
        };
    }
}

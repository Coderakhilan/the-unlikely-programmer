using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// Three save slots. Two kinds of save in each:
    ///   floor checkpoints: the first time a run steps onto a floor (0 to 3), exactly as the elevator doors opened.
    ///     Load Game can start from any of them.
    ///   the resume save: written on every room change while Donnie is free (no dialogue, no chase, no scripted scene),
    ///     with the room, his position, the objective, the cart, the spills and where everyone stands.
    ///     Continue loads it: the game picks up right where it was left.
    /// A run started straight from the Floors scene (editor) or moved by a dev jump doesn't save.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 3;

        [Serializable] public class Message { public string text; public bool fromMe; public string attachment; }
        [Serializable] public class Thread { public string contact; public bool unread; public List<Message> messages = new List<Message>(); }
        [Serializable] public class Item { public string id, name; }

        [Serializable]
        public class Checkpoint
        {
            public int floor;
            public int clearance;
            public List<string> flags = new List<string>();
            public List<Item> items = new List<Item>();
            public int equipped = -1;
            public List<Thread> threads = new List<Thread>();
            public string savedAt;

            // ---- the exact spot (resume saves; empty room = a floor checkpoint, start at its elevator)
            public string room;
            public float x, y;
            public string objective, hint;
            public World world = new World();

            public bool IsResume => !string.IsNullOrEmpty(room);
        }

        /// <summary>The bits of the world that aren't flags: the cart, mopped spills, every character's state.</summary>
        [Serializable]
        public class World
        {
            public bool cartOut;
            public string cartRoom;
            public float cartX, cartY;
            public List<string> cleanSpills = new List<string>();
            public List<Person> people = new List<Person>();
        }

        [Serializable] public class Person { public string id; public bool active, talkable; public float x, y, fx, fy; }

        [Serializable]
        public class Slot
        {
            public int difficulty = (int)Difficulty.Unlikely;
            public long lastPlayed;          // DateTime ticks, for "Continue"
            public int current;              // the checkpoint Continue loads (the latest one reached or loaded)
            public bool finished;            // an ending was reached in this run
            public List<Checkpoint> checkpoints = new List<Checkpoint>();
            public Checkpoint resume;        // where the run was left (JsonUtility fills it in empty when there is none)

            public Difficulty Difficulty => (Difficulty)difficulty;
            public bool HasResume => resume != null && resume.IsResume;
            /// <summary>The floor Continue puts Donnie on.</summary>
            public int ContinueFloor => HasResume ? resume.floor : (Current != null ? Current.floor : 0);
            public Checkpoint Get(int floor) => checkpoints.Find(c => c.floor == floor);
            public Checkpoint Current => Get(current) ?? (checkpoints.Count > 0 ? checkpoints[checkpoints.Count - 1] : null);
        }

        /// <summary>Where the files go (tests point this at a temp folder).</summary>
        public static string Folder { get; set; }
        private static string Dir => !string.IsNullOrEmpty(Folder) ? Folder : RealFolder;

        /// <summary>
        /// Where saves live. Automated test runs (batch mode) never touch the player's real saves: they get a scratch folder.
        /// </summary>
        public static string RealFolder => Application.isBatchMode
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UnlikelyProgrammerTests")
            : Application.persistentDataPath;
        private static string PathOf(int slot) => System.IO.Path.Combine(Dir, $"slot{slot + 1}.json");

        /// <summary>The slot this run saves into (-1: not saving).</summary>
        public static int ActiveSlot { get; private set; } = -1;

        /// <summary>The highest floor this run has saved or loaded; reaching a higher one saves.</summary>
        public static int SessionFloor { get; private set; }

        /// <summary>A checkpoint to restore when the Floors scene starts (null = a fresh game).</summary>
        public static Checkpoint Pending { get; private set; }

        /// <summary>The server room's checkpoint number (after Floor 3): the endings start there.</summary>
        public const int ServerRoom = 4;

        public static string FloorLabel(int floor) =>
            floor == ServerRoom ? "The Server Room"
                : "Floor " + floor + "  ·  " + (floor >= 0 && floor < Access.FloorNames.Length ? Access.FloorNames[floor] : "?");

        // ------------------------------------------------------------------ files

        public static Slot Load(int slot)
        {
            try
            {
                string p = PathOf(slot);
                if (!File.Exists(p)) return null;
                var s = JsonUtility.FromJson<Slot>(File.ReadAllText(p));
                return s != null && s.checkpoints.Count > 0 ? s : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("SaveSystem: slot " + (slot + 1) + " couldn't be read: " + e.Message);
                return null;
            }
        }

        public static void Write(int slot, Slot data)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(PathOf(slot), JsonUtility.ToJson(data, true));
            }
            catch (Exception e) { Debug.LogWarning("SaveSystem: couldn't write slot " + (slot + 1) + ": " + e.Message); }
        }

        public static void Delete(int slot)
        {
            try { if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot)); } catch (Exception) { }
        }

        public static bool AnySave
        {
            get { for (int i = 0; i < SlotCount; i++) if (Load(i) != null) return true; return false; }
        }

        /// <summary>The slot played most recently, or -1.</summary>
        public static int LatestSlot
        {
            get
            {
                int best = -1;
                long when = long.MinValue;
                for (int i = 0; i < SlotCount; i++)
                {
                    var s = Load(i);
                    if (s != null && s.lastPlayed > when) { when = s.lastPlayed; best = i; }
                }
                return best;
            }
        }

        // ------------------------------------------------------------------ starting a run

        /// <summary>A new game in a slot (overwrites it): saves the Floor 0 checkpoint now; the opening plays.</summary>
        public static void NewGame(int slot, Difficulty difficulty)
        {
            GameState.ResetAll();
            Inventory.Clear();
            PhoneData.Clear();
            GameState.Difficulty = difficulty;
            ActiveSlot = slot;
            SessionFloor = 0;
            Pending = null;
            var cp = Capture(0);
            cp.flags.Add("intro_seen");   // loading Floor 0 later skips the opening
            var data = new Slot { difficulty = (int)difficulty, lastPlayed = DateTime.Now.Ticks, current = 0 };
            data.checkpoints.Add(cp);
            Write(slot, data);
        }

        /// <summary>Continue / Load Game: restore a checkpoint when the Floors scene starts.</summary>
        public static bool Begin(int slot, int floor)
        {
            var data = Load(slot);
            var cp = data?.Get(floor);
            if (cp == null) return false;
            ActiveSlot = slot;
            SessionFloor = floor;
            Pending = cp;
            GameState.Difficulty = data.Difficulty;
            data.current = floor;
            data.lastPlayed = DateTime.Now.Ticks;
            Write(slot, data);
            return true;
        }

        /// <summary>Continue: pick up exactly where this slot was left (falls back to its latest floor checkpoint).</summary>
        public static bool BeginResume(int slot)
        {
            var data = Load(slot);
            if (data == null) return false;
            if (!data.HasResume) return Begin(slot, data.Current.floor);
            ActiveSlot = slot;
            SessionFloor = data.resume.floor;
            Pending = data.resume;
            GameState.Difficulty = data.Difficulty;
            data.lastPlayed = DateTime.Now.Ticks;
            Write(slot, data);
            return true;
        }

        /// <summary>Back to the title, or a run that isn't saved (dev jumps, the editor's Floors scene).</summary>
        public static void Stop()
        {
            ActiveSlot = -1;
            Pending = null;
        }

        /// <summary>Floors scene start: puts the pending checkpoint's state back (flags, clearance, pockets, phone).</summary>
        public static Checkpoint TakePending()
        {
            var cp = Pending;
            Pending = null;
            if (cp == null) return null;
            Restore(cp);
            return cp;
        }

        // ------------------------------------------------------------------ during play

        /// <summary>Donnie stepped onto a floor: the first time this run reaches it, save a checkpoint.</summary>
        public static void ReachedFloor(int floor)
        {
            if (ActiveSlot < 0 || floor <= SessionFloor) return;
            var data = Load(ActiveSlot) ?? new Slot { difficulty = (int)GameState.Difficulty };
            data.checkpoints.RemoveAll(c => c.floor >= floor);   // a replay replaces the later checkpoints
            var cp = Capture(floor);
            data.checkpoints.Add(cp);
            if (cp.IsResume) data.resume = cp;   // arriving is also where Continue picks up
            data.checkpoints.Sort((a, b) => a.floor.CompareTo(b.floor));
            data.current = floor;
            data.lastPlayed = DateTime.Now.Ticks;
            Write(ActiveSlot, data);
            SessionFloor = floor;
        }

        /// <summary>Autosave: the resume save (Continue), right where Donnie is now.</summary>
        public static void SaveResume(int floor)
        {
            if (ActiveSlot < 0) return;
            var data = Load(ActiveSlot);
            if (data == null) return;
            var cp = Capture(floor);
            if (!cp.IsResume) return;
            data.resume = cp;
            data.lastPlayed = DateTime.Now.Ticks;
            Write(ActiveSlot, data);
            LastAutosave = UnityEngine.Time.unscaledTime;
        }

        /// <summary>When the last autosave happened (unscaled time), for the HUD's little "saved" note.</summary>
        public static float LastAutosave { get; private set; } = -100f;

        /// <summary>Fills a checkpoint's room, position and world (set by the story at start-up; null in EditMode tests).</summary>
        public static Action<Checkpoint> WorldCapture;

        /// <summary>An ending was reached: the run is marked finished (its checkpoints stay loadable).</summary>
        public static void Finished()
        {
            if (ActiveSlot < 0) return;
            var data = Load(ActiveSlot);
            if (data == null) return;
            data.finished = true;
            data.lastPlayed = DateTime.Now.Ticks;
            Write(ActiveSlot, data);
        }

        public static Checkpoint Capture(int floor)
        {
            var cp = new Checkpoint { floor = floor, clearance = GameState.Clearance, savedAt = DateTime.Now.ToString("d MMM yyyy, HH:mm") };
            cp.flags.AddRange(GameState.AllFlags);
            foreach (var i in Inventory.Items) cp.items.Add(new Item { id = i.id, name = i.name });
            cp.equipped = Inventory.EquippedIndex;
            for (int t = PhoneData.Threads.Count - 1; t >= 0; t--)   // oldest first, so re-adding keeps the order
            {
                var th = PhoneData.Threads[t];
                var copy = new Thread { contact = th.contact, unread = th.unread };
                foreach (var m in th.messages) copy.messages.Add(new Message { text = m.text, fromMe = m.fromMe, attachment = m.attachment });
                cp.threads.Add(copy);
            }
            WorldCapture?.Invoke(cp);
            return cp;
        }

        public static void Restore(Checkpoint cp)
        {
            GameState.ResetAll();
            foreach (var f in cp.flags) GameState.Set(f);
            GameState.Clearance = cp.clearance;
            Inventory.Clear();
            foreach (var i in cp.items) Inventory.Add(i.id, i.name, false);
            if (cp.equipped >= 0) Inventory.Equip(cp.equipped);
            PhoneData.Clear();
            foreach (var th in cp.threads)
            {
                foreach (var m in th.messages) PhoneData.AddMessage(th.contact, m.text, m.attachment, m.fromMe);
                var restored = PhoneData.Threads.Find(x => x.contact == th.contact);
                if (restored != null) restored.unread = th.unread;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            ActiveSlot = -1;
            SessionFloor = 0;
            Pending = null;
            Folder = null;
            WorldCapture = null;
            LastAutosave = -100f;
        }
    }
}

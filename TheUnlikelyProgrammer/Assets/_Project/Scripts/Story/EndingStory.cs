using System;
using System.Collections;
using System.Collections.Generic;
using Unlikely.Core;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.SQL;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;

namespace Unlikely.Story
{
    /// <summary>
    /// The server room and the four endings. The GyaraDB console (desk, top left) checks the truth-or-lie calls:
    ///   Floor 2 truth + Floor 3 truth  -> ACCOMPLICE: upload the kidnappers' malware; Donnie takes the fall.
    ///   Floor 2 truth + Floor 3 lie    -> THE LAST QUERY: Ryker was their eyes; Betty is killed; Donnie can only DELETE.
    ///   Floor 2 lie   + Floor 3 truth  -> CAPITALIST: needs Ryker's MagiCorp login AND the CEO's laptop mail. Donnie cuts a
    ///                                     deal: INSERT GyaraDB into MagiCorp, then DELETE it, on the CEO's card (Liam is blamed).
    ///                                     MagiCorp shares must be bought first. Without the mail: a "missed something" tease,
    ///                                     then ACCOMPLICE.
    ///   Floor 2 lie   + Floor 3 lie    -> GOOD SAMARITAN: out into the dark again, find the CEO (security still hunting) and tell
    ///                                     him everything; he gets the truth out of Liam.
    /// The Floor 1 call only adds a line to the kidnappers' last call (it introduced the mechanic).
    /// After the ending's pictures, the game restarts.
    /// </summary>
    public class EndingStory : MonoBehaviour
    {
        [SerializeField] private TextAsset dialogue;

        public static EndingStory Instance { get; private set; }

        public const string Accomplice = "accomplice", LastQuery = "last_query", Capitalist = "capitalist",
            CapitalistMissed = "capitalist_missed", Samaritan = "samaritan";

        private const string Room = "server";
        private const string Caller = "Unknown number";
        public const string MasterTable = "gyaradb", MagiTable = "magicorp_inbox";

        /// <summary>"" (not there yet), "ready" (use the console), "busy" (a scene), "upload", "delete", "deal", "samaritan", "done".</summary>
        public string Stage { get; private set; } = "";
        /// <summary>The ending sequence now playing (tests).</summary>
        public string EndingShown { get; private set; }

        private RoomManager rooms;
        private Database db;
        private bool consoleBusy;
        private int refusals;

        public void Configure(TextAsset dialogueJson) => dialogue = dialogueJson;

        private void Awake() => Instance = this;

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Begin()
        {
            DialogueBank.Load(dialogue);
            rooms = RoomManager.Instance;
            var pc = Unlikely.Interaction.ScriptedInteractable.Find("srv_gyaradb");
            if (pc != null) pc.OnUse = UseConsole;
            foreach (var id in new[] { "srv_ceo", "srv_liam", "srv_guard_1", "srv_guard_2", "f3_liam_ceo", "f3_sec_ceo" })
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.Talkable = false;
                n.gameObject.SetActive(false);
            }
            var prevUse = Door.InteractHook;
            Door.InteractHook = d => DoorUse(d) || (prevUse != null && prevUse(d));
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ which ending

        /// <summary>Capitalist needs Ryker's MagiCorp login (Floor 2 lie) and the CEO's mail about MagiCorp (his laptop).</summary>
        public static bool CapitalistReady => GameState.Has("f2_magicorp") && GameState.Has("f3_mails_read");

        public static string Pick()
        {
            bool lie2 = GameState.Has("d2_lie"), lie3 = GameState.Has("d3_lie");
            if (!lie2) return lie3 ? LastQuery : Accomplice;
            if (lie3) return Samaritan;
            return CapitalistReady ? Capitalist : CapitalistMissed;
        }

        private IEnumerator Run()
        {
            yield return new WaitUntil(() => GameState.Has("f3_complete") && InRoom(Room));
            var hud = HudUI.Instance;
            hud.SetHint(null);
            hud.SetObjective("Use the GyaraDB console (the desk, top left)");
            yield return StoryBeats.Beat("end_arrive", Say("end_arrive"));
            Stage = "ready";
        }

        /// <summary>Safe to autosave: before the server room, or in it before the console has been touched.</summary>
        public bool CanSave => Stage == "" || Stage == "ready";

        /// <summary>A resumed game: nothing to put back (the run picks the server room up from the flags).</summary>
        public void Resume() { }

        private void UseConsole()
        {
            if (consoleBusy || DialogueUI.Instance.IsOpen) return;
            switch (Stage)
            {
                case "ready": StartCoroutine(Route()); break;
                case "upload": StartCoroutine(UploadConsole()); break;
                case "delete": OpenDeleteConsole(); break;
                case "deal":
                    if (!GameState.Has("f3_stocks")) Show("end_cap_need_stocks");
                    else OpenDealConsole();
                    break;
                case "samaritan": Show("end_sam_pc_again"); break;
                case "done": Show("end_pc_done"); break;
                default: break;   // "" or mid-scene: nothing
            }
        }

        private IEnumerator Route()
        {
            Stage = "busy";
            switch (Pick())
            {
                case Accomplice: yield return AccompliceCall(); break;
                case LastQuery: yield return LastQueryCall(); break;
                case Capitalist: yield return CapitalistCall(); break;
                case CapitalistMissed:
                    yield return Say(GameState.Has("f2_magicorp") ? "end_cap_tease" : "end_cap_tease_ryker");
                    yield return AccompliceCall();
                    break;
                case Samaritan: yield return SamaritanThink(); break;
            }
        }

        // ------------------------------------------------------------------ ACCOMPLICE

        private IEnumerator AccompliceCall()
        {
            yield return Call("end_acc_call");
            Stage = "upload";
            HudUI.Instance.SetObjective("Upload the file from your phone (GyaraDB console)");
            yield return Say("end_acc_pc");
            yield return UploadConsole();
        }

        private IEnumerator UploadConsole()
        {
            if (consoleBusy) yield break;
            consoleBusy = true;
            bool done = false;
            GyaraDbUI.Instance.OpenUpload("PHONE (UNREGISTERED)", BuildGyaraDb(false), () => done = true);
            yield return new WaitUntil(() => done);
            consoleBusy = false;
            Stage = "busy";
            HudUI.Instance.SetObjective("");
            HudUI.Instance.Toast("THERMAL ALARM: SERVER ROOM");
            yield return Say("end_acc_shambles");
            yield return Call("end_acc_fall", false);
            yield return WalkIn(true);
            yield return Say("end_acc_walkin");
            yield return EndWith(StoryScenes.EndingAccomplice);
        }

        // ------------------------------------------------------------------ THE LAST QUERY

        private IEnumerator LastQueryCall()
        {
            yield return Call("end_lq_call");
            yield return Say("end_lq_rage");
            Stage = "delete";
            HudUI.Instance.SetObjective("Destroy it. All of it. (GyaraDB console)");
            db = BuildGyaraDb(false);
            OpenDeleteConsole();
        }

        /// <summary>The last query: Donnie only wants it gone. Anything that isn't a DELETE is refused.</summary>
        public static string DeleteOnlyRefusal(string sql, int n)
        {
            if (SqlParser.FirstWord(sql ?? "") == "DELETE") return null;
            var lines = DialogueBank.Get("end_lq_refuse");
            return lines.Length > 0 ? DialogueBank.Split(lines[n % lines.Length]).text : "DELETE. Everything.";
        }

        private void OpenDeleteConsole()
        {
            if (GyaraDbUI.Instance.IsOpen) return;
            if (db == null) db = BuildGyaraDb(false);
            GyaraDbUI.Instance.OpenQuery("J-0009  RAYMOND, DONALD  (FACILITIES)", db,
                q => DeleteOnlyRefusal(q, refusals++),
                q => db.Find(MasterTable).Rows.Count == 0 ? "gyaradb: 0 rows." : One("end_lq_partial"),
                () => db.Find(MasterTable).Rows.Count == 0,
                true, One("end_lq_status"),
                () => StartCoroutine(LastQueryWiped()));
        }

        private IEnumerator LastQueryWiped()
        {
            Stage = "busy";
            HudUI.Instance.SetObjective("");
            yield return Say("end_lq_wiped");
            yield return WalkIn(false);
            yield return Say("end_lq_walkin");
            yield return EndWith(StoryScenes.EndingLastQuery);
        }

        // ------------------------------------------------------------------ CAPITALIST

        private IEnumerator CapitalistCall()
        {
            yield return Call("end_cap_call");
            Stage = "deal";
            db = BuildGyaraDb(true);
            if (!GameState.Has("f3_stocks"))
            {
                yield return Say("end_cap_buy_first");
                HudUI.Instance.SetObjective("Buy MagiCorp shares first (phone: INFI Bank)");
                yield return new WaitUntil(() => GameState.Has("f3_stocks"));
                yield return new WaitForSeconds(0.5f);   // let "it'd better be worth it" have its moment first
                yield return new WaitUntil(() => !UiKit.AnyModalOpen && !DialogueUI.Instance.IsOpen);
            }
            // the player walks up to the console and opens it (UseConsole, stage "deal")
            HudUI.Instance.SetObjective("GyaraDB console: copy everything into MagiCorp (INSERT INTO), then wipe GyaraDB (DELETE)");
        }

        /// <summary>What Donnie won't do while cutting the deal (null = fine). Public for tests.</summary>
        public static string DealRefusal(Database d, string sql)
        {
            string w = SqlParser.FirstWord(sql ?? "");
            if (w == "INSERT")
            {
                var s = SqlParser.ParseInsert(sql);
                return string.Equals(s.Table, MagiTable, StringComparison.OrdinalIgnoreCase) ? null : One("end_cap_wrong_table");
            }
            if (w == "DELETE")
            {
                var s = SqlParser.ParseDelete(sql);
                if (string.Equals(s.Table, MagiTable, StringComparison.OrdinalIgnoreCase)) return One("end_cap_keep_copy");
                if (Copied(d) < d.Find(MasterTable).Rows.Count) return One("end_cap_copy_first");
            }
            return null;
        }

        /// <summary>How many of GyaraDB's records are in MagiCorp's inbox.</summary>
        public static int Copied(Database d)
        {
            var have = new HashSet<string>();
            foreach (var r in d.Find(MagiTable).Rows) have.Add(QueryResult.Format(r[0]));
            int n = 0;
            foreach (var r in d.Find(MasterTable).Rows) if (have.Contains(QueryResult.Format(r[0]))) n++;
            return n;
        }

        public static bool DealDone(Database d) => d.Find(MasterTable).Rows.Count == 0 && d.Find(MagiTable).Rows.Count >= Records.Length;

        private void OpenDealConsole()
        {
            if (GyaraDbUI.Instance.IsOpen) return;
            if (db == null) db = BuildGyaraDb(true);
            int total = Records.Length;
            GyaraDbUI.Instance.OpenQuery("CEO KEYCARD  ·  link: MAGICORP (ryker.v)", db,
                q => DealRefusal(db, q),
                q =>
                {
                    int left = db.Find(MasterTable).Rows.Count;
                    if (SqlParser.FirstWord(q) == "DELETE")
                        return left == 0 ? "gyaradb: 0 rows.  magicorp_inbox: everything." : $"{left} record(s) still in gyaradb. All of it.";
                    if (SqlParser.FirstWord(q) == "INSERT")
                    {
                        int c = Copied(db);
                        return c >= left ? $"All {total} records copied to MagiCorp. Now wipe GyaraDB." : $"Copied {c} of {left}. They want all of it.";
                    }
                    return null;
                },
                () => DealDone(db),
                true, One("end_cap_status"),
                () => StartCoroutine(CapitalistDone()));
        }

        private IEnumerator CapitalistDone()
        {
            Stage = "busy";
            HudUI.Instance.SetObjective("");
            yield return Say("end_cap_done");
            // cut to the CEO's office
            PlayerController.Frozen = true;
            var ceo = Place("f3_ceo", "f3_ceo", "scene_ceo", Vector2.left);
            var liam = Place("f3_liam_ceo", "f3_ceo", "scene_liam", Vector2.right);
            var sec = Place("f3_sec_ceo", "f3_ceo", "scene_sec", Vector2.left);
            yield return rooms.Peek("f3_ceo", ceo.transform);
            yield return Say("end_cap_office");
            // security walks Liam out
            bool gone = false;
            Vector3 door = rooms.MarkerPosition("f3_ceo", "door0");
            liam.WalkPath(new[] { door }, () => gone = true);
            sec.WalkPath(new[] { door });
            float t = 0f;
            yield return new WaitUntil(() => gone || (t += Time.deltaTime) > 6f);
            liam.gameObject.SetActive(false);
            sec.gameObject.SetActive(false);
            ceo.Face(Vector2.down);
            yield return new WaitForSeconds(0.6f);
            yield return Say("end_cap_office_after");
            yield return rooms.EndPeek();
            yield return EndWith(StoryScenes.EndingCapitalist);
        }

        // ------------------------------------------------------------------ GOOD SAMARITAN

        private IEnumerator SamaritanThink()
        {
            yield return Say("end_sam_think");
            Stage = "samaritan";
            HudUI.Instance.SetObjective("Find the CEO out on the floor and tell him everything (security is still hunting)");
            yield return new WaitUntil(() => InRoom("floor3"));
            var ceo = Floor3Story.Instance.ResumeDarkHunt();
            if (ceo == null) yield break;
            ceo.Talkable = true;
            ceo.SetBarks();
            ceo.OnTalk = () =>
            {
                if (Stage != "samaritan") return;
                StartCoroutine(SamaritanTalk(ceo));
            };
            StartCoroutine(CeoWanders(ceo));
        }

        /// <summary>The CEO walks his beat in the dark. He isn't hunting any more; he's thinking.</summary>
        private IEnumerator CeoWanders(Npc ceo)
        {
            var hunter = ceo.GetComponent<Hunter>();
            var route = hunter != null ? hunter.Route : null;
            if (route == null || route.Length == 0) yield break;
            var barks = Plain("end_sam_ceo_barks");
            int b = 0;
            while (Stage == "samaritan" && ceo != null)
            {
                bool done = false;
                ceo.WalkPath(route, () => done = true);
                float t = 0f;
                while (!done && Stage == "samaritan")
                {
                    t += Time.deltaTime;
                    if (t > 7f && barks.Length > 0 && InRoom("floor3")) { ceo.Bark(barks[b++ % barks.Length], 2.5f); t = 0f; }
                    yield return null;
                }
            }
        }

        private IEnumerator SamaritanTalk(Npc ceo)
        {
            Stage = "busy";
            ceo.StopWalking();
            ceo.Face((PlayerController.Instance.transform.position - ceo.transform.position).normalized);
            Floor3Story.Instance.EndHunt();
            yield return Say("end_sam_talk");
            HudUI.Instance.SetObjective("");
            // cut to the CEO's office: Liam, fetched out of the meeting room
            PlayerController.Frozen = true;
            ceo.gameObject.SetActive(false);
            var boss = Place("f3_ceo", "f3_ceo", "scene_ceo", Vector2.left);
            Place("f3_liam_ceo", "f3_ceo", "scene_liam", Vector2.right);
            yield return rooms.Peek("f3_ceo", boss.transform);
            yield return Say("end_sam_interrogate");
            yield return rooms.EndPeek();
            yield return EndWith(StoryScenes.EndingGoodSamaritan);
        }

        // ------------------------------------------------------------------ the server room door

        private bool DoorUse(Door d)
        {
            if (!InRoom(Room) || d.TargetRoomId != "floor3") return false;
            if (Stage == "samaritan") return false;
            if (Stage == "ready") { Show("end_exit_ready"); return true; }
            if (Stage != "") { Show("end_exit_busy"); return true; }
            return false;
        }

        // ------------------------------------------------------------------ people walking in

        /// <summary>
        /// The CEO, Liam and two officers (or security) come in through the door, round the racks, and stop at fixed spots on
        /// the open floor right of the desk (markers walk_stand_0..3), facing Donnie. Never relative to Donnie: that put
        /// them on top of the racks when he stood elsewhere.
        /// </summary>
        private IEnumerator WalkIn(bool police)
        {
            PlayerController.Frozen = true;
            yield return new WaitUntil(() => !DialogueUI.Instance.IsOpen);
            Vector3 door = rooms.MarkerPosition(Room, "walk_in_0"), w1 = rooms.MarkerPosition(Room, "walk_in_1"), w2 = rooms.MarkerPosition(Room, "walk_in_2");
            string[] ids = { "srv_guard_1", "srv_ceo", "srv_liam", "srv_guard_2" };
            int arrived = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                var n = Npc.Find(ids[i]);
                if (n == null) { arrived++; continue; }
                n.gameObject.SetActive(true);
                n.transform.position = door;
                var target = rooms.MarkerPosition(Room, "walk_stand_" + i);
                n.WalkPath(new[] { w1, w2, new Vector3(w2.x, target.y, door.z), target }, () => { arrived++; n.Face(Vector2.left); });
                yield return new WaitForSeconds(0.45f);
            }
            float t = 0f;
            yield return new WaitUntil(() => arrived >= ids.Length || (t += Time.deltaTime) > 12f);
            foreach (var id in ids) { var n = Npc.Find(id); if (n != null) { n.StopWalking(); n.Face(Vector2.left); } }
        }

        /// <summary>Someone set in a room for a scene, at one of its markers.</summary>
        private Npc Place(string id, string room, string marker, Vector2 facing)
        {
            var n = Npc.Find(id);
            if (n == null) return null;
            n.StopWalking();
            n.gameObject.SetActive(true);
            n.Talkable = false;
            n.transform.position = rooms.MarkerPosition(room, marker);
            n.Face(facing);
            var h = n.GetComponent<Hunter>();
            if (h != null) h.enabled = false;
            return n;
        }

        private IEnumerator EndWith(string sequence)
        {
            PlayerController.Frozen = true;
            Stage = "done";
            yield return new WaitUntil(() => !DialogueUI.Instance.IsOpen && !rooms.IsTransitioning);
            EndingShown = sequence;
            bool done = false;
            CutsceneUI.Instance.Play(StoryScenes.Get(sequence), () => done = true);
            yield return new WaitUntil(() => done);
            // the stars: this ending, and finishing on this difficulty
            Profile.EndingReached(sequence, GameState.Difficulty);
            SaveSystem.Finished();
            StoryDirector.Restart();
        }

        // ------------------------------------------------------------------ GyaraDB

        /// <summary>Everything GyaraCorp is, in one master table.</summary>
        public static readonly string[][] Records =
        {
            new[] { "1", "pensions", "Staff pension fund: 4.1m moved to 'strategic consultancy'. Consultant: the CEO's brother-in-law." },
            new[] { "2", "pensions", "Pension statements printed in 6pt grey on grey. Approved by Legal." },
            new[] { "3", "hr", "Interview programme: 212 candidates. Hires planned: 0. Purpose: 'look busy for investors'." },
            new[] { "4", "hr", "Interns: unpaid, 'temporary', some since 2023." },
            new[] { "5", "products", "GyaraSync v9 is GyaraSync v8 with a new logo. R&D budget: 11m." },
            new[] { "6", "products", "Core engine source code, build keys, the lot. Comment on line 1: 'do not touch'." },
            new[] { "7", "clients", "Brightwater Insurance: 38% of revenue. Thinks it has an exclusive deal." },
            new[] { "8", "clients", "Halden Retail: also thinks it has an exclusive deal." },
            new[] { "9", "clients", "Client list: 1,904 accounts, renewal dates, and what each of them is scared of." },
            new[] { "10", "finance", "Q3 'record profit' includes 9m of money we expect to find." },
            new[] { "11", "finance", "Board bonus pool: 22m. Cleaning budget: 14k (see Facilities)." },
            new[] { "12", "security", "Server room code rota, keycard master list. CEO's password hint: 'Liam's birthday'." },
        };

        public static Database BuildGyaraDb(bool magicorp)
        {
            var d = new Database();
            var t = new Table(MasterTable, new[] { "record_id", "dept", "record" });
            foreach (var r in Records) t.Rows.Add(new object[] { double.Parse(r[0]), r[1], r[2] });
            d.Add(t);
            if (magicorp) d.Add(new Table(MagiTable, new[] { "record_id", "dept", "record" }));
            return d;
        }

        // ------------------------------------------------------------------ dev jumps (F10)

        /// <summary>Jump straight into the server room on one of the four routes (dev menu).</summary>
        public void DevJump(string ending)
        {
            bool lie2 = ending == "sam" || ending == "cap" || ending == "cap_missed";
            bool lie3 = ending == "sam" || ending == "lq";
            Floor3Story.Instance.DevJump("in_server", lie2, lie3);
            if (ending == "cap_missed") GameState.Unset("f3_mails_read");
        }

        // ------------------------------------------------------------------ helpers

        private static string One(string id) => DialogueBank.Split(DialogueBank.One(id)).text;

        private static string[] Plain(string entry)
        {
            var lines = DialogueBank.Get(entry);
            var plain = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++) plain[i] = DialogueBank.Split(lines[i]).text;
            return plain;
        }

        private static void Show(string id) => DialogueUI.Instance.Show("", DialogueBank.Get(id));

        private static IEnumerator Say(string id)
        {
            yield return new WaitUntil(() => !DialogueUI.Instance.IsOpen);
            bool done = false;
            DialogueUI.Instance.Show("", DialogueBank.Get(id), () => done = true);
            yield return new WaitUntil(() => done);
        }

        /// <summary>The kidnappers ring. If Donnie lied to them on Floor 1, they bring it up first.</summary>
        private IEnumerator Call(string id, bool mentionFloor1 = true)
        {
            yield return new WaitUntil(() => !UiKit.AnyModalOpen && !rooms.IsTransitioning && !DialogueUI.Instance.IsOpen);
            bool answered = false;
            PhoneUI.Instance.Ring(Caller, () => answered = true);
            yield return new WaitUntil(() => answered);
            if (mentionFloor1 && GameState.Has("d1_lie")) yield return Say("end_d1_lie");
            yield return Say(id);
        }

        private bool InRoom(string id) => rooms.Current != null && rooms.Current.roomId == id && !rooms.IsTransitioning;
    }
}

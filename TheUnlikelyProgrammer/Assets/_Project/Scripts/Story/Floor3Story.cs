using System;
using System.Collections;
using System.Collections.Generic;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.Puzzles;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;

namespace Unlikely.Story
{
    /// <summary>
    /// Floor 3 (Executive Offices): get into the server room.
    ///
    ///   arrival call: the server room code changes daily, belongs to a different staff member each day, and is encrypted;
    ///     Liam's executive keycard decrypts it.
    ///   Liam (paces by the bottom-left windows) tells Donnie to get lost. Three coins in the plant pots (stairs, Liam's
    ///     corner, the lounge) buy a hot coffee at the lounge vending machine. Coffee in hand, talk to Liam: splash.
    ///     Liam chases Donnie (a bit slower than him). Into the small meeting room, into the locker; Liam walks in, goes to
    ///     the far end ("I know you're in here"); Donnie slips out and locks the door from outside. Liam hands over his keycard.
    ///   Employee terminal (reception desk): Server room clearance + INSERT / INSERT ... SELECT / DELETE lessons.
    ///   Executive office A ("the orange room"): the link station (points each computer at one other machine; decrypts),
    ///     and a pen drive in the desk drawer. Printed on HR's printer (Floor 2) it's the key map (which column points where).
    ///   Three join puzzles on the green computers (see tools/puzzles_floor3.py): today's holder, who covers her, his code parts
    ///     -> V Q 3 L, encrypted.
    ///   Decrypt with Liam's card: revoked, the CEO is alerted ("What is he up to?"). Lights out on Floor 3, security on
    ///     the floor, the CEO hunting too; his office is open. Torch from the emergency cabinet in office A (which is safe).
    ///   CEO's keycard (his desk) -> decrypt again: the phone restarts and updates (Calls works, Bank app appears).
    ///     His laptop's mail is about MagiCorp. Knowing that AND having Ryker's MagiCorp login (lie branch, Floor 2), Donnie
    ///     can sink his savings into MagiCorp shares (f3_stocks): the capitalist ending's setup.
    ///   Decrypt once more: 7302. The server room keypad, then the call: truth or lie about getting in (d3_truth / d3_lie).
    ///   Before the blackout, security and an assistant only question Donnie without his cart; after it, everyone hunts him.
    /// </summary>
    public class Floor3Story : MonoBehaviour
    {
        [SerializeField] private TextAsset dialogue;
        [SerializeField] private TextAsset[] puzzleFiles;
        [SerializeField] private TextAsset[] tutorialSteps;

        public static Floor3Story Instance { get; private set; }

        private const string Floor = "floor3";
        private const string Caller = "Unknown number";
        public const string ServerCode = "7302";

        // the five green computers and the one table each serves to the network
        public static readonly string[] Machines = { "board_long", "board_side", "open_l", "open_r", "exec_b" };
        private static readonly string[] MachineNames =
            { "Boardroom PC (long table)", "Boardroom PC (side table)", "Open office PC (left)", "Open office PC (right)", "Executive office B PC" };
        private static readonly string[] MachineTables = { "staff", "rota", "leave", "badges", "code_parts" };
        private static readonly string[] MachinePlaces =
            { "Boardroom, the long table", "Boardroom, the side table", "Open office, left desk", "Open office, right desk", "Executive office B, the desk" };
        private static readonly string[][] MachineColumns =
        {
            new[] { "staff_id", "name", "title" }, new[] { "rota_day", "holder" }, new[] { "leave_id", "on_leave", "leave_day", "cover" },
            new[] { "badge_no", "owner", "status" }, new[] { "part_id", "card", "position", "cipher" },
        };
        // which machine has which join task (KODEX), and the puzzle behind it
        private static readonly Dictionary<string, (string label, string puzzle)> MachineTasks = new Dictionary<string, (string, string)>
        {
            { "board_side", ("TASK 1", "f3_p1_rota") }, { "open_l", ("TASK 2", "f3_p2_cover") }, { "exec_b", ("TASK 3", "f3_p3_code") },
        };
        /// <summary>The printed key map: which column points at which table's key.</summary>
        public static readonly Dictionary<string, string> KeyMap = new Dictionary<string, string>
        {
            { "rota.holder", "staff.staff_id" }, { "leave.on_leave", "staff.staff_id" }, { "leave.cover", "staff.staff_id" },
            { "badges.owner", "staff.staff_id" }, { "code_parts.card", "badges.badge_no" },
        };
        private static readonly string[] Hunters = { "f3_ceo_hunt", "f3_sec_1", "f3_sec_2", "f3_sec_3" };
        private static readonly string[] People = { "f3_guard_a", "f3_guard_b", "f3_assistant", "f3_lounge" };
        private static readonly string[] CoinPlants = { "f3_plant_coin_1", "f3_plant_coin_2", "f3_plant_coin_3" };

        private RoomManager rooms;
        private readonly Dictionary<string, PuzzleData> puzzles = new Dictionary<string, PuzzleData>();
        private PuzzleData[] tutorial;
        private bool tutorialDone;
        private readonly Dictionary<string, string> links = new Dictionary<string, string>();

        /// <summary>
        /// "" (normal), "liam" (chased), "liam_room" (he followed Donnie into the small meeting room), "liam_inside" (Donnie
        /// slipped out; the door can be locked), "alert" (the CEO finds out), "dark" (blackout), "done" (server room).
        /// </summary>
        public string Phase { get; private set; } = "";
        private bool stationBusy, deciding;

        /// <summary>Liam is at the far end of the small meeting room, looking the wrong way: Donnie can slip out.</summary>
        public bool LiamSearching { get; private set; }
        private float outsideFor;

        public void Configure(TextAsset dialogueJson, TextAsset[] puzzleJson, TextAsset[] tutorialJson)
        {
            dialogue = dialogueJson;
            puzzleFiles = puzzleJson;
            tutorialSteps = tutorialJson;
        }

        private void Awake() => Instance = this;

        public void Begin()
        {
            DialogueBank.Load(dialogue);
            rooms = RoomManager.Instance;
            foreach (var f in puzzleFiles)
            {
                var p = PuzzleData.FromJson(f.text);
                puzzles[p.id] = p;
            }
            tutorial = new PuzzleData[tutorialSteps.Length];
            for (int i = 0; i < tutorialSteps.Length; i++) tutorial[i] = PuzzleData.FromJson(tutorialSteps[i].text);
            // lessons already done (a loaded save, or Leet's skipped ones): the terminal step is over
            tutorialDone = tutorial.Length > 0 && System.Array.TrueForAll(tutorial, t => GameState.Has(t.rewardFlag));

            // the link station starts wired up wrong, the way someone left it
            links["board_long"] = null;
            links["board_side"] = "open_r";
            links["open_l"] = "exec_b";
            links["open_r"] = null;
            links["exec_b"] = "board_long";

            SetupPeople();
            SetupHotspots();
            Hunter.Caught += OnCaught;
            rooms.RoomChanged += OnRoomChanged;

            // Floor 2 already hooks doors and the lift: go first, and hand over to it when Floor 3 has nothing to say
            var prevPrompt = Door.PromptHook;
            Door.PromptHook = d => DoorPrompt(d) ?? prevPrompt?.Invoke(d);
            var prevUse = Door.InteractHook;
            Door.InteractHook = d => DoorUse(d) || (prevUse != null && prevUse(d));
            var prevBlocked = Elevator.Blocked;
            Elevator.Blocked = () => LiftBlocked() ?? prevBlocked?.Invoke();

            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Hunter.Caught -= OnCaught;
            if (rooms != null) rooms.RoomChanged -= OnRoomChanged;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ the floor, in order

        private IEnumerator Run()
        {
            var hud = HudUI.Instance;
            // anywhere on this floor (a resumed game may start in one of its rooms)
            yield return new WaitUntil(() => GameState.Has("f2_complete") && OnThisFloor || GameState.Has("f3_complete"));
            if (GameState.Has("f3_complete")) yield break;   // a dev jump into the server room: the endings run the floor now

            if (!GameState.Has("f3_started"))   // a dev jump past the arrival skips the call
                yield return StoryBeats.Beat("f3_arrive", Arrival());
            if (!GameState.Has("f3_liam_met")) hud.SetObjective("Get Liam's keycard (he's by the windows, bottom left)");

            yield return new WaitUntil(() => GameState.Has("f3_liam_met"));
            // the objective follows what Donnie is up to, until the keycard is his
            while (!GameState.Has("f3_liam_locked"))
            {
                if (Phase == "")
                    hud.SetObjective(Inventory.Has("coffee") ? "Hold the coffee (its number key) and talk to Liam"
                        : CoinsFound >= 3 && !GameState.Has("f3_coffee_bought") ? "Buy a hot coffee (lounge vending machine)"
                        : "Get a hot coffee for Liam: coins first (" + CoinsFound + " of 3)");
                yield return new WaitForSeconds(0.25f);
            }

            hud.SetObjective("Find today's server room code (computers: boardroom, open office, executive offices)");
            hud.SetHint("Executive office A has the link station. KODEX shows each computer's task.");
            yield return new WaitUntil(() => GameState.Has("f3_code_enc"));
            hud.SetHint(null);
            while (Phase != "dark" && Phase != "alert")
            {
                hud.SetObjective(GameState.Has("f3_hacked") ? "Decrypt the code at the link station (Executive office A)"
                    : "Get server room clearance (employee terminal, reception), then decrypt the code (Executive office A)");
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitUntil(() => Phase == "dark");
            yield return TorchThen("Get the CEO's keycard from his office (top left)");
            yield return new WaitUntil(() => Inventory.Has("keycard_ceo"));
            hud.SetObjective("Decrypt the code at the link station (Executive office A)");
            yield return new WaitUntil(() => GameState.Has("f3_code"));
            hud.SetObjective("Get into the server room (right-hand side)");
            yield return new WaitUntil(() => Phase == "done");
            hud.SetHint(null);   // the server room (EndingStory) takes it from here
        }

        private IEnumerator Arrival()
        {
            yield return Say("f3_arrive");
            yield return Call("f3_arrival_call");
            GameState.Set("f3_started");
        }

        /// <summary>Donnie is somewhere on the Executive floor (its corridor or any of its rooms).</summary>
        private bool OnThisFloor => rooms.Current != null && !rooms.IsTransitioning && Darkness.FloorOf(rooms.Current.roomId) == "3";

        /// <summary>
        /// Safe to autosave: the normal day, after the floor (server room), or the blackout while Donnie is in one of the two
        /// rooms the hunters can't follow him into (executive office A, the CEO's office). Never during Liam's chase.
        /// </summary>
        public bool CanSave
        {
            get
            {
                if (stationBusy || deciding) return false;
                if (Phase == "" || Phase == "done") return true;
                string room = rooms != null && rooms.Current != null ? rooms.Current.roomId : null;
                return Phase == "dark" && (room == "f3_exec_a" || room == "f3_ceo");
            }
        }

        /// <summary>
        /// A resumed game (after WorldState put everyone back): the link station's wiring, the lessons, and the blackout if
        /// it's on (security and the CEO start their rounds again from the top).
        /// </summary>
        public void Resume()
        {
            tutorialDone = tutorial != null && tutorial.Length > 0 && Array.TrueForAll(tutorial, t => GameState.Has(t.rewardFlag));
            foreach (var from in Machines)
            {
                if (GameState.Has(LinkFlag(from, null))) links[from] = null;
                foreach (var to in Machines) if (GameState.Has(LinkFlag(from, to))) links[from] = to;
            }
            if (GameState.Has("f3_complete")) { Phase = "done"; return; }
            if (!GameState.Has("power_out_floor3")) { Phase = ""; return; }
            Phase = "dark";
            foreach (var id in People) { var n = Npc.Find(id); if (n != null) n.gameObject.SetActive(false); }
            var ceo = Npc.Find("f3_ceo");
            if (ceo != null) ceo.gameObject.SetActive(false);
            foreach (var id in Hunters)
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.gameObject.SetActive(true);
                var h = n.GetComponent<Hunter>();
                h.ResetToStart();
                h.enabled = true;
            }
            HideSpot.Enabled = true;
            HudUI.Instance.SetHint(DialogueBank.One("f3_hint_dark"));
        }

        /// <summary>The link station's wiring is kept as flags ("f3link_open_l_board_long"), so saves keep it.</summary>
        private static string LinkFlag(string from, string to) => "f3link_" + from + "_" + (to ?? "none");

        private IEnumerator TorchThen(string objective)
        {
            var hud = HudUI.Instance;
            if (!Inventory.Has("flashlight"))
            {
                hud.SetObjective("Grab the torch from the emergency cabinet (this office)");
                yield return new WaitUntil(() => Inventory.Has("flashlight"));
            }
            hud.SetObjective(objective);
        }

        private void Update()
        {
            if (rooms == null) return;
            // KODEX unlocks each join puzzle's task once the one before it is solved (and the key map is printed)
            if (GameState.Has("f3_keymap"))
            {
                if (GameState.Has("f3_p1_rota_solved")) GameState.Set("f3_ready_p2");
                if (GameState.Has("f3_p2_cover_solved")) GameState.Set("f3_ready_p3");
            }
            if (Phase == "liam_inside" && InRoom(Floor) && !UiKit.AnyModalOpen && !DialogueUI.Instance.IsOpen)
            {
                // Donnie walked off without locking it: Liam comes back out after a while
                outsideFor += Time.deltaTime;
                if (outsideFor > 14f) LiamComesOut();
            }
        }

        /// <summary>Tests: the Floor 3 lift with Floors 0-2 done on the lie branch.</summary>
        public void DevSkipToFloor3() => DevJump("start", true);

        /// <summary>
        /// Dev jump (F10 menu). Floors 0-2 done, on the lie branch (Ryker discovered, MagiCorp login in hand) or the truth.
        ///   "start":    at the Floor 3 lift; the arrival call rings.
        ///   "blackout": Liam locked in, terminal done, key map printed, the three joins solved; at the link station with
        ///               Liam's card (decrypting it starts the blackout).
        ///   "ceo_card": the blackout is on, torch and the CEO's keycard in hand; decrypting next = phone update + bank.
        ///   "server":   code 7302 in hand, mail read, phone updated; at the server room door (keypad, then the D3 call).
        /// </summary>
        public void DevJump(string stage, bool lie, bool lie3 = false)
        {
            GameState.Clearance = Mathf.Max(GameState.Clearance, 3);
            foreach (var f in new[] { "has_manual", "f0_complete", "f0_hacked", "f1_complete", "f1_hacked", "f2_complete", "f2_hacked", "f2_deal", "f2_plant_found", "f2_leverage" })
                GameState.Set(f);
            if (!GameState.Has("d1_truth") && !GameState.Has("d1_lie")) GameState.Set("d1_truth");
            foreach (var l in ManualLessons.All) if (l.floor <= 2) GameState.Set("lesson_" + l.id);
            for (int f = 0; f <= 2; f++) GameState.Unset("power_out_floor" + f);
            if (lie)
            {
                foreach (var f in new[] { "d2_lie", "ryker_discovered", "f2_magicorp", "f2_ryker_trapped" }) GameState.Set(f);
                if (!Inventory.Has("magicorp")) Inventory.Add("magicorp", "MagiCorp login (written on hand)", false);
            }
            else GameState.Set("d2_truth");
            Floor2Story.Instance?.BackToNormal();
            if (stage == "start")
            {
                rooms.GoTo(Floor, "elevator");
                return;
            }

            // Liam locked in, the terminal done, the key map printed, the three joins solved, the links set
            GameState.Clearance = 4;
            foreach (var f in new[] { "f3_started", "f3_liam_met", "f3_liam_locked", "f3_coffee_bought", "f3_coin_1", "f3_coin_2", "f3_coin_3",
                         "f3_hacked", "f3_tut_1_done", "f3_tut_2_done", "f3_tut_3_done", "f3_got_drive", "f3_keymap", "f3_link_seen",
                         "f3_p1_rota_solved", "f3_p2_cover_solved", "f3_p3_code_solved", "f3_ready_p2", "f3_ready_p3", "f3_code_enc" })
                GameState.Set(f);
            foreach (var l in ManualLessons.All) if (l.floor == 3) GameState.Set("lesson_" + l.id);
            tutorialDone = true;
            SetLink("board_side", "board_long");
            SetLink("open_l", "board_long");
            SetLink("exec_b", "open_r");
            var liam = Npc.Find("f3_liam");
            if (liam != null) liam.gameObject.SetActive(false);   // he's in the small meeting room
            Inventory.Add("keymap", "Key map (printout)", false);
            Inventory.Add("code_enc", "Encrypted code: VQ3L", false);
            if (stage == "blackout")
            {
                Inventory.Add("keycard_liam", "Liam's keycard", false);
                rooms.GoTo("f3_exec_a", "door0");
                return;
            }

            // the blackout is on
            GameState.Set("power_out_floor3");
            GameState.Set("unlocked_f3_ceo");
            foreach (var id in People) { var n = Npc.Find(id); if (n != null) n.gameObject.SetActive(false); }
            var ceo = Npc.Find("f3_ceo");
            if (ceo != null) ceo.gameObject.SetActive(false);
            foreach (var id in Hunters)
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.gameObject.SetActive(true);
                n.GetComponent<Hunter>().ResetToStart();
                n.GetComponent<Hunter>().enabled = true;
            }
            HideSpot.Enabled = true;
            HudUI.Instance.SetHint(DialogueBank.One("f3_hint_dark"));
            Phase = "dark";
            Inventory.Add("flashlight", "Torch", false);
            Inventory.Add("keycard_ceo", "CEO's keycard", false);
            if (stage == "ceo_card")
            {
                rooms.GoTo("f3_exec_a", "door0");
                return;
            }

            // the code decrypted, the phone updated, the mail read
            foreach (var f in new[] { "f3_mails_read", "phone_updated", "f3_code" }) GameState.Set(f);
            if (PhoneUI.BankAllowed) GameState.Set("bank_app");
            Inventory.Remove("code_enc");
            Inventory.Add("code_server", "Server room code: " + ServerCode, false);
            if (stage != "in_server")
            {
                rooms.GoTo(Floor, "server_door");
                return;
            }

            // "in_server": the keypad and the D3 call done, inside the server room
            GameState.Set("unlocked_server");
            GameState.Set(lie3 ? "d3_lie" : "d3_truth");
            EndHunt();
            HudUI.Instance.SetHint(null);
            Phase = "done";
            GameState.Set("f3_complete");
            rooms.GoTo("server", "from_floor3");
        }

        /// <summary>
        /// Good samaritan: back out into the dark. Security hunts Donnie again; the CEO is out there too, but just walking.
        /// Returns the CEO (EndingStory walks him and lets Donnie talk to him).
        /// </summary>
        public Npc ResumeDarkHunt()
        {
            Phase = "dark";
            HideSpot.Enabled = true;
            HudUI.Instance.SetHint(DialogueBank.One("f3_hint_dark"));
            foreach (var id in Hunters)
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.gameObject.SetActive(true);
                var h = n.GetComponent<Hunter>();
                h.ResetToStart();
                h.enabled = id != "f3_ceo_hunt";
                if (h.enabled) MoveAwayFromDonnie(h);
            }
            return Npc.Find("f3_ceo_hunt");
        }

        /// <summary>
        /// Donnie steps out of the server room: the guard whose round starts by its door would have him before he
        /// could move. Anyone that close starts from the far end of his round instead.
        /// </summary>
        private static void MoveAwayFromDonnie(Hunter h)
        {
            var p = PlayerController.Instance;
            var route = h.Route;
            if (p == null || route == null || route.Length == 0) return;
            Vector3 pp = p.transform.position;
            float view = Camera.main != null ? Camera.main.orthographicSize : 8f;   // half a screen
            if (Vector2.Distance(h.transform.position, pp) > view) return;
            Vector3 far = route[0];
            foreach (var r in route) if (Vector2.Distance(r, pp) > Vector2.Distance(far, pp)) far = r;
            h.ResetTo(far);
        }

        /// <summary>Everyone stops hunting (the D3 call, the endings).</summary>
        public void EndHunt()
        {
            foreach (var id in Hunters) SetHunter(id, false);
            HideSpot.Enabled = false;
            if (Phase == "dark") Phase = "done";
        }

        // ------------------------------------------------------------------ Liam

        private void SetupLiam()
        {
            var liam = Npc.Find("f3_liam");
            if (liam == null) return;
            liam.OnTalk = () =>
            {
                if (Phase != "") return;
                if (Inventory.IsEquipped("coffee"))
                {
                    if (JanitorCart.Holding) { DialogueUI.Instance.Show("", new[] { "Both hands on the cart. Can't throw a coffee like that. (Q to let go.)" }); return; }
                    StartCoroutine(Splash());
                    return;
                }
                if (Inventory.Has("coffee")) { Show("f3_liam_need_equip"); return; }
                if (!GameState.Has("f3_liam_met")) StartCoroutine(LiamRefuses());
                else Show("f3_liam_again");
            };
            StartCoroutine(PaceLiam());
        }

        private IEnumerator LiamRefuses()
        {
            yield return Say("f3_liam_refuses");
            GameState.Set("f3_liam_met");
            yield return Say("f3_liam_after");
        }

        /// <summary>Liam walks up and down by the windows until something happens to him.</summary>
        private IEnumerator PaceLiam()
        {
            var liam = Npc.Find("f3_liam");
            bool toB = true;
            while (true)
            {
                yield return new WaitUntil(() => Phase == "" && !GameState.Has("f3_liam_locked") && liam.gameObject.activeInHierarchy && !liam.IsWalking);
                bool done = false;
                liam.WalkPath(new[] { rooms.MarkerPosition(Floor, toB ? "liam_b" : "liam_a") }, () => done = true);
                yield return new WaitUntil(() => done || Phase != "");
                if (Phase != "") { liam.StopWalking(); continue; }
                liam.Face(Vector2.up);   // looks out of the window
                toB = !toB;
                float t = 0f;
                yield return new WaitUntil(() => (t += Time.deltaTime) > 3f || Phase != "");
            }
        }

        private IEnumerator Splash()
        {
            GameState.Set("f3_liam_met");   // coffee first, talk never: still counts as meeting him
            Inventory.Remove("coffee");
            var liam = Npc.Find("f3_liam");
            liam.StopWalking();
            liam.Face((PlayerController.Instance.transform.position - liam.transform.position).normalized);
            yield return Say("f3_splash");
            StartChase(liam.transform.position);
        }

        private void StartChase(Vector3 from)
        {
            Phase = "liam";
            var liam = Npc.Find("f3_liam");
            liam.gameObject.SetActive(true);
            liam.Talkable = false;
            var h = liam.GetComponent<Hunter>();
            h.ResetTo(from);
            h.enabled = true;
            HideSpot.Enabled = true;
            SetGuards(false);
            HudUI.Instance.SetObjective("Lose Liam: get him into the small meeting room and lock him in");
        }

        /// <summary>The day staff (security, the assistant) watching for a janitor without his cart, or not.</summary>
        private static void SetGuards(bool on)
        {
            foreach (var id in People)
            {
                var n = Npc.Find(id);
                var g = n != null ? n.GetComponent<Guard>() : null;
                if (g != null) g.enabled = on;
            }
        }

        private void OnRoomChanged(Room from, Room to)
        {
            if (to == null) return;
            if (Phase == "liam" && to.roomId == "f3_round") StartCoroutine(LiamFollows());
        }

        /// <summary>Donnie ran into the small meeting room. Liam comes in after him.</summary>
        private IEnumerator LiamFollows()
        {
            Phase = "liam_room";
            HudUI.Instance.SetHint(DialogueBank.One("f3_hint_locker"));
            var hall = Npc.Find("f3_liam");
            hall.GetComponent<Hunter>().enabled = false;
            hall.gameObject.SetActive(false);
            yield return Unpaused(2.6f);
            if (!InRoom("f3_round"))
            {
                // he was out again before Liam got there: Liam's right outside
                ResumeChaseAtDoor();
                yield break;
            }
            var liam = Npc.Find("f3_liam_in");
            liam.gameObject.SetActive(true);
            liam.transform.position = rooms.MarkerPosition("f3_round", "door0");
            liam.Face(Vector2.left);   // looking into the room
            var cone = LiamCone(liam);
            cone.enabled = true;
            liam.Bark(DialogueBank.Split(DialogueBank.One("f3_liam_in")).text, 2.5f);

            // he stands in the doorway for a moment, looking in. He only catches Donnie if he can SEE him.
            float t = 0f;
            while (t < 1f)
            {
                if (!Paused) t += Time.deltaTime;
                DrawCone(cone, liam);
                if (InRoom("f3_round") && LiamSees(liam)) { CaughtInRoom(liam, cone); yield break; }
                yield return null;
            }

            // across to the far end of the room, his back to the locker
            bool arrived = false;
            var grid = NavGrid.For(rooms.Current);
            Vector3 far = rooms.MarkerPosition("f3_round", "liam_far");
            var path = grid != null ? grid.Path(liam.transform.position, far) : new List<Vector3> { far };
            if (path.Count == 0) path.Add(far);
            liam.WalkPath(path, () => arrived = true);
            while (!arrived && InRoom("f3_round"))
            {
                DrawCone(cone, liam);
                if (LiamSees(liam)) { liam.StopWalking(); CaughtInRoom(liam, cone); yield break; }
                yield return null;
            }

            // searching the far corner, facing away. That's the moment to slip out.
            LiamSearching = true;
            string[] mutter = { "Come out, janitor. I'll only break something small.", "I can smell the coffee on you.", "Under the table? No...", "Behind the plant? ...No." };
            Vector2[] glances = { Vector2.left, Vector2.up, Vector2.left, Vector2.down };
            float search = 15f, barkAt = 2.5f, glanceAt = 2f;
            int m = 0, g = 0;
            liam.Face(Vector2.left);
            while (InRoom("f3_round") || rooms.IsTransitioning)
            {
                if (!Paused) { search -= Time.deltaTime; barkAt -= Time.deltaTime; glanceAt -= Time.deltaTime; }
                if (barkAt <= 0f) { liam.Bark(mutter[m++ % mutter.Length], 2.2f); barkAt = 4f; }
                if (glanceAt <= 0f) { liam.Face(glances[g++ % glances.Length]); glanceAt = 2f; }
                DrawCone(cone, liam);
                if (InRoom("f3_round") && LiamSees(liam)) { LiamSearching = false; CaughtInRoom(liam, cone); yield break; }
                if (search <= 0f)
                {
                    // he turns round and goes for the locker
                    LiamSearching = false;
                    liam.Bark("The locker. Of course.", 1.6f);
                    bool there = false;
                    Vector3 lockerSpot = rooms.MarkerPosition("f3_round", "door0");
                    var back = grid != null ? grid.Path(liam.transform.position, lockerSpot) : new List<Vector3> { lockerSpot };
                    if (back.Count == 0) back.Add(lockerSpot);
                    liam.WalkPath(back, () => there = true);
                    while (!there && InRoom("f3_round"))
                    {
                        DrawCone(cone, liam);
                        if (LiamSees(liam)) { liam.StopWalking(); CaughtInRoom(liam, cone); yield break; }
                        yield return null;
                    }
                    if (InRoom("f3_round")) { liam.Bark("Found you.", 1.5f); CaughtInRoom(liam, cone); yield break; }
                    break;
                }
                yield return null;
            }
            // Donnie's out in the corridor; Liam's still in there, looking
            cone.enabled = false;
            LiamSearching = false;
            Phase = "liam_inside";
            outsideFor = 0f;
            HudUI.Instance.SetHint(null);
            HudUI.Instance.SetObjective("Lock the small meeting room door. Now.");
        }

        private SpriteRenderer liamCone;
        private const float LiamRange = 3.6f, LiamHalfAngle = 50f;

        /// <summary>Liam in the meeting room has no Hunter, so he gets his own visible sight cone.</summary>
        private SpriteRenderer LiamCone(Npc liam)
        {
            if (liamCone != null) return liamCone;
            var go = new GameObject("SightCone");
            go.transform.SetParent(liam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            liamCone = go.AddComponent<SpriteRenderer>();
            liamCone.sprite = Hunter.ConeSprite(LiamHalfAngle);
            liamCone.sortingOrder = -9500;
            liamCone.color = new Color(1f, 0.55f, 0.3f, 0.22f);
            return liamCone;
        }

        private static void DrawCone(SpriteRenderer cone, Npc liam)
        {
            cone.transform.localScale = new Vector3(LiamRange, LiamRange, 1f);
            cone.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(liam.Facing.y, liam.Facing.x) * Mathf.Rad2Deg);
        }

        /// <summary>Can Liam see Donnie: not hidden, in range, inside his cone (or right next to him), nothing in the way.</summary>
        private bool LiamSees(Npc liam)
        {
            if (HideSpot.IsHidden || Paused) return false;
            var p = PlayerController.Instance;
            float s = liam.transform.localScale.x;
            Vector2 d = p.transform.position - liam.transform.position;
            if (d.magnitude > LiamRange * s) return false;
            if (d.magnitude > 0.7f * s && Vector2.Angle(liam.Facing, d) > LiamHalfAngle) return false;
            var grid = NavGrid.For(rooms.Current);
            return grid == null || grid.Clear(grid.Nearest(liam.transform.position), grid.Nearest(p.transform.position));
        }

        private void CaughtInRoom(Npc liam, SpriteRenderer cone)
        {
            cone.enabled = false;
            LiamSearching = false;
            Vector2 d = PlayerController.Instance.transform.position - liam.transform.position;
            liam.Face(Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y)));
            LiamCatches();
        }

        private void ResumeChaseAtDoor()
        {
            var hall = Npc.Find("f3_liam");
            var inside = Npc.Find("f3_liam_in");
            inside.StopWalking();
            inside.gameObject.SetActive(false);
            hall.gameObject.SetActive(true);
            Vector3 door = rooms.MarkerPosition(Floor, "f3_round_door0");
            hall.transform.position = door;
            Phase = "liam";
            var h = hall.GetComponent<Hunter>();
            h.ResetTo(door);
            h.enabled = true;
            HudUI.Instance.SetObjective("Lose Liam: get him into the small meeting room and lock him in");
        }

        private void LiamComesOut()
        {
            ResumeChaseAtDoor();
            Npc.Find("f3_liam").Bark("Thought so.", 2f);
        }

        private void LiamCatches()
        {
            var h = Npc.Find("f3_liam").GetComponent<Hunter>();
            OnCaught(h);
        }

        private IEnumerator LockLiam()
        {
            Phase = "";
            GameState.Set("f3_liam_locked");
            var inside = Npc.Find("f3_liam_in");
            inside.StopWalking();
            inside.gameObject.SetActive(false);
            HideSpot.Enabled = false;
            SetGuards(true);
            HudUI.Instance.SetHint(null);
            HudUI.Instance.Toast("Locked: Small meeting room");
            yield return Say("f3_liam_trapped");
            Give("keycard_liam", "Liam's keycard");
        }

        // ------------------------------------------------------------------ the CEO finds out; the blackout

        private IEnumerator Blackout()
        {
            Phase = "alert";
            Inventory.Remove("keycard_liam");   // revoked
            yield return Say("f3_decrypt_fail");
            PlayerController.Frozen = true;
            var ceo = Npc.Find("f3_ceo");
            yield return rooms.Peek("f3_ceo", ceo.transform);
            ceo.StopWalking();
            ceo.Face(Vector2.down);
            yield return Say("f3_ceo_alert");
            yield return new WaitForSeconds(0.4f);
            yield return rooms.EndPeek(() =>
            {
                foreach (var id in People) { var n = Npc.Find(id); if (n != null) n.gameObject.SetActive(false); }
                ceo.gameObject.SetActive(false);   // he's out on the floor now
                GameState.Set("power_out_floor3");
                GameState.Set("unlocked_f3_ceo");
            });
            PlayerController.Frozen = false;
            yield return Say("f3_blackout");
            yield return Say(Inventory.Has("flashlight") ? "f3_blackout_torch" : "f3_blackout_no_torch");
            HideSpot.Enabled = true;
            foreach (var id in Hunters)
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.gameObject.SetActive(true);
                var h = n.GetComponent<Hunter>();
                h.ResetToStart();
                h.enabled = true;
            }
            HudUI.Instance.SetHint(DialogueBank.One("f3_hint_dark"));
            Phase = "dark";
            Music.SetScene(null);   // Framed (cued at "REVOKED") carries on unbroken as the blackout's track
        }

        // ------------------------------------------------------------------ the link station (Executive office A)

        private IEnumerator LinkStation()
        {
            if (stationBusy) yield break;
            stationBusy = true;
            if (!GameState.Has("f3_link_seen"))
            {
                GameState.Set("f3_link_seen");
                yield return Say("f3_link_intro");
            }
            var nodes = new LinkStationUI.Node[Machines.Length];
            Vector2[] where = { new Vector2(105, 335), new Vector2(300, 335), new Vector2(180, 100), new Vector2(400, 100), new Vector2(505, 228) };
            for (int i = 0; i < Machines.Length; i++)
                nodes[i] = new LinkStationUI.Node
                {
                    id = Machines[i], name = MachineNames[i], place = MachinePlaces[i], table = MachineTables[i], columns = MachineColumns[i],
                    task = MachineTasks.TryGetValue(Machines[i], out var t) ? t.label : null, pos = where[i],
                };
            bool closed = false, decrypt = false;
            LinkStationUI.Instance.Open(nodes, LinkOf, SetLink,
                () => GameState.Has("f3_keymap") ? KeyMap : null,
                m => MachineTasks.TryGetValue(m, out var t) && GameState.Has(t.puzzle + "_solved"),
                DecryptStatus, () => decrypt = true, () => closed = true);
            yield return new WaitUntil(() => closed);
            if (decrypt) yield return Decrypt();
            stationBusy = false;
        }

        private string DecryptStatus()
        {
            if (GameState.Has("f3_code")) return "Decrypted: " + ServerCode + ". Today's server room code.";
            if (!GameState.Has("f3_code_enc")) return "Decrypt: nothing to decrypt yet. TASK 3 gives the encrypted code.";
            if (Inventory.Has("keycard_ceo")) return "Encrypted code VQ3L ready. Keycard: the CEO's.";
            if (Inventory.Has("keycard_liam")) return "Encrypted code VQ3L ready. Keycard: Liam's.";
            return "Encrypted code VQ3L ready. Needs an executive keycard.";
        }

        private string NameOf(string machine) => machine == null ? "nothing" : MachineNames[Array.IndexOf(Machines, machine)];

        /// <summary>Point one computer at another (null = nothing). Public for tests.</summary>
        public void SetLink(string from, string to)
        {
            links[from] = to;
            GameState.Unset(LinkFlag(from, null));
            foreach (var m in Machines) GameState.Unset(LinkFlag(from, m));
            GameState.Set(LinkFlag(from, to));
        }

        public string LinkOf(string machine) => links.TryGetValue(machine, out var t) ? t : null;

        private IEnumerator Decrypt()
        {
            if (!GameState.Has("f3_code_enc")) { yield return Say("f3_decrypt_no_code"); yield break; }
            if (GameState.Has("f3_code")) { yield return Say("f3_decrypt_again"); yield break; }
            if (!GameState.Has("f3_hacked")) { yield return Say("f3_decrypt_need_terminal"); yield break; }
            if (Phase == "")
            {
                if (!Inventory.Has("keycard_liam")) { yield return Say("f3_decrypt_no_card"); yield break; }
                yield return Blackout();
                yield break;
            }
            if (Phase != "dark") yield break;
            if (!Inventory.Has("keycard_ceo")) { yield return Say("f3_decrypt_need_ceo"); yield break; }
            if (!GameState.Has("phone_updated"))
            {
                yield return Say("f3_decrypt_update");
                bool done = false;
                PhoneUI.Instance.SoftwareUpdate(() => done = true);
                yield return new WaitUntil(() => done);
                yield return Say("f3_after_update");
                if (GameState.Has("bank_app")) yield return Say("f3_after_update_bank");
                if (PhoneUI.KnowsAboutMagiCorp) yield return Say("f3_after_update_aware");
                yield break;   // the decryption got interrupted; run it again when ready
            }
            yield return Say("f3_decrypt_done");
            GameState.Set("f3_code");
            Inventory.Remove("code_enc");
            Give("code_server", "Server room code: " + ServerCode);
        }

        // ------------------------------------------------------------------ the server room door: D3

        private IEnumerator ServerDoor()
        {
            if (deciding) yield break;
            deciding = true;
            if (!GameState.Has("unlocked_server"))
            {
                bool opened = false;
                KeypadUI.Instance.Open("Server room", ServerCode, () => opened = true);
                yield return new WaitUntil(() => opened || !KeypadUI.Instance.IsOpen);
                if (!opened) { deciding = false; yield break; }
                yield return new WaitUntil(() => !KeypadUI.Instance.IsOpen);
                GameState.Set("unlocked_server");
                HudUI.Instance.Toast("Unlocked: Server room");
            }
            yield return Call("f3_server_call");
            int picked = -1;
            DialogueUI.Instance.ShowChoice(DialogueBank.One("f3_decision_prompt"),
                new[] { DialogueBank.One("f3_choice_truth"), DialogueBank.One("f3_choice_lie") }, i => picked = i);
            yield return new WaitUntil(() => picked >= 0);
            GameState.Set(picked == 0 ? "d3_truth" : "d3_lie");
            yield return Say(picked == 0 ? "f3_truth" : "f3_lie");
            EndHunt();
            yield return Say("f3_server_enter");
            Phase = "done";
            GameState.Set("f3_complete");
            rooms.GoTo("server", "from_floor3");
            deciding = false;
        }

        // ------------------------------------------------------------------ doors and the lift

        private string DoorPrompt(Door d)
        {
            if (d.TargetRoomId == "f3_round" && Phase == "liam_inside") return "Lock the door";
            if (d.TargetRoomId == "server" && !GameState.Has("f3_complete")) return "Use keypad";
            return null;
        }

        private bool DoorUse(Door d)
        {
            string target = d.TargetRoomId;
            if (target == "server")
            {
                if (GameState.Has("f3_complete")) return false;
                if (!GameState.Has("f3_code")) { Show("f3_server_locked"); return true; }
                StartCoroutine(ServerDoor());
                return true;
            }
            if (target == "f3_round")
            {
                if (Phase == "liam_inside") { StartCoroutine(LockLiam()); return true; }
                if (GameState.Has("f3_liam_locked")) { Show("f3_liam_door"); return true; }
            }
            if (Phase == "liam" && target != Floor && target != "f3_round") { Show("f3_chase_door"); return true; }
            if (Phase == "dark" && target != Floor && Darkness.FloorOf(target) == "3" && target != "f3_exec_a" && target != "f3_ceo")
            {
                Show("f3_door_dark");
                return true;
            }
            return false;
        }

        private string LiftBlocked()
        {
            if (!InRoom(Floor)) return null;
            if (Phase == "liam" || Phase == "liam_inside") return DialogueBank.One("f3_elevator_liam");
            if (Phase == "alert" || Phase == "dark") return DialogueBank.One("f3_elevator_dark");
            return null;
        }

        // ------------------------------------------------------------------ caught

        private void OnCaught(Hunter h)
        {
            var room = h.GetComponentInParent<Room>(true);
            if (room == null || (room.roomId != Floor && Darkness.FloorOf(room.roomId) != "3")) return;   // Floor 2's people
            if (GameOverUI.Instance == null || GameOverUI.Instance.IsOpen) return;
            if (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen) TerminalUI.Instance.Close();
            if (HideSpot.Current != null) HideSpot.Current.ComeOut();
            if (h.Kind == Hunter.Mode.Liam)
            {
                h.enabled = false;
                GameOverUI.Instance.Show("CAUGHT", DialogueBank.One("f3_caught_liam"), () =>
                {
                    // Liam's back by the windows, still wet; Donnie's in the lounge with another coffee
                    var inside = Npc.Find("f3_liam_in");
                    inside.StopWalking();
                    inside.gameObject.SetActive(false);
                    var liam = Npc.Find("f3_liam");
                    liam.gameObject.SetActive(true);
                    liam.GetComponent<Hunter>().enabled = false;
                    liam.GetComponent<Hunter>().ResetToStart();
                    liam.Talkable = true;
                    HideSpot.Enabled = false;
                    SetGuards(true);
                    LiamSearching = false;
                    if (liamCone != null) liamCone.enabled = false;
                    HudUI.Instance.SetHint(null);
                    Phase = "";
                    if (!Inventory.Has("coffee")) Inventory.Add("coffee", "Hot coffee (lid off)", false);
                    HudUI.Instance.SetObjective("Coffee in hand (its number key), then talk to Liam");
                    rooms.GoTo("f3_lounge", "door0");
                });
                return;
            }
            string line = h.Kind == Hunter.Mode.Ryker ? "f3_caught_ceo" : "f3_caught_security";
            GameOverUI.Instance.Show("CAUGHT", DialogueBank.One(line), () =>
            {
                foreach (var id in Hunters) { var n = Npc.Find(id); if (n != null) n.GetComponent<Hunter>().ResetToStart(); }
                // back to the last safe room he reached: the server room once he's been in it (Good Samaritan: out
                // looking for the CEO), the CEO's office once he's taken the CEO's keycard (no running back from the
                // link station to try again), else the link station office
                if (GameState.Has("f3_complete")) rooms.GoTo("server", "from_floor3");
                else if (Inventory.Has("keycard_ceo")) rooms.GoTo("f3_ceo", "door0");
                else rooms.GoTo("f3_exec_a", "door0");
            });
        }

        // ------------------------------------------------------------------ setup

        private void SetupPeople()
        {
            void Barks(string id, string entry)
            {
                var n = Npc.Find(id);
                if (n != null) n.SetBarks(Plain(entry));
            }
            void Shoo(string id, string entry)
            {
                var n = Npc.Find(id);
                var g = n != null ? n.GetComponent<Guard>() : null;
                if (g != null) g.SetCaughtLines(Plain(entry));
            }
            Barks("f3_guard_a", "npc_f3_sec");
            Barks("f3_guard_b", "npc_f3_sec_b");
            Barks("f3_assistant", "npc_f3_assistant");
            Barks("f3_lounge", "npc_f3_lounge");
            Shoo("f3_guard_a", "guard_f3_sec");
            Shoo("f3_guard_b", "guard_f3_sec");
            Shoo("f3_assistant", "guard_f3_assistant");

            foreach (var id in Hunters)
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.GetComponent<Hunter>().enabled = false;
                n.gameObject.SetActive(false);
                n.Talkable = false;
            }
            var ceoHunt = Npc.Find("f3_ceo_hunt");
            if (ceoHunt != null)
            {
                var h = ceoHunt.GetComponent<Hunter>();
                h.SetSpotLines(Plain("f3_ceo_spot"));
                h.SetTaunts(Plain("f3_ceo_taunts"), "Who's there? Liam?");
            }
            foreach (var id in new[] { "f3_sec_1", "f3_sec_2", "f3_sec_3" })
            {
                var n = Npc.Find(id);
                if (n != null) n.GetComponent<Hunter>().SetSpotLines(Plain("f3_sec_spot"));
            }
            var liamIn = Npc.Find("f3_liam_in");
            if (liamIn != null) { liamIn.gameObject.SetActive(false); liamIn.Talkable = false; }
            var liam = Npc.Find("f3_liam");
            if (liam != null) liam.GetComponent<Hunter>().enabled = false;
            SetupLiam();

            var ceo = Npc.Find("f3_ceo");
            if (ceo != null)
            {
                ceo.Talkable = false;
                StartCoroutine(PaceCeo(ceo));
            }
        }

        private IEnumerator PaceCeo(Npc ceo)
        {
            bool toB = true;
            while (ceo != null && ceo.gameObject != null)
            {
                yield return new WaitUntil(() => Phase == "" && ceo.gameObject.activeInHierarchy && !ceo.IsWalking);
                bool done = false;
                ceo.WalkPath(new[] { rooms.MarkerPosition("f3_ceo", toB ? "ceo_b" : "ceo_a") }, () => done = true);
                yield return new WaitUntil(() => done || Phase != "");
                toB = !toB;
                float t = 0f;
                yield return new WaitUntil(() => (t += Time.deltaTime) > 2.2f);
            }
        }

        private static string[] Plain(string entry)
        {
            var lines = DialogueBank.Get(entry);
            var plain = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++) plain[i] = DialogueBank.Split(lines[i]).text;
            return plain;
        }

        private static void SetHunter(string id, bool on)
        {
            var n = Npc.Find(id);
            var h = n != null ? n.GetComponent<Hunter>() : null;
            if (h != null) h.enabled = on;
        }

        private void SetupHotspots()
        {
            // the join puzzles, and the two machines that only serve a table
            PuzzlePc("f3_pc_board_side", "board_side", "f3_p1_rota", "f3_p1_done", null);
            PuzzlePc("f3_pc_open_l", "open_l", "f3_p2_cover", "f3_p2_done", null);
            PuzzlePc("f3_pc_exec_b", "exec_b", "f3_p3_code", "f3_p3_done", () =>
            {
                GameState.Set("f3_code_enc");
                Give("code_enc", "Encrypted code: VQ3L");
            });
            Use("f3_pc_board_long", () => Show(Dark ? "f3_pc_dead" : "f3_pc_staff"));
            Use("f3_pc_open_r", () => Show(Dark ? "f3_pc_dead" : "f3_pc_badges"));

            Use("f3_link", () => StartCoroutine(LinkStation()));   // on backup power: works in the dark too
            Use("f3_drawer", () =>
            {
                if (GameState.Has("f3_got_drive")) { Show("f3_drawer_empty"); return; }
                GameState.Set("f3_got_drive");
                Give("pen_drive", "Pen drive (KEYS)");
                Show("f3_drawer_drive");
            });
            Use("f3_torch", () =>
            {
                if (Inventory.Has("flashlight")) { Show("f3_torch_done"); return; }
                Give("flashlight", "Torch");
                Show("f3_torch");
            });

            // coins in the plant pots, coffee from the lounge machine
            for (int i = 0; i < CoinPlants.Length; i++)
            {
                string flag = "f3_coin_" + (i + 1);
                Use(CoinPlants[i], () =>
                {
                    if (GameState.Has(flag)) { Show("f3_plant_empty"); return; }
                    GameState.Set(flag);
                    int n = CoinsFound;
                    Inventory.Remove("coins");
                    if (!GameState.Has("f3_coffee_bought")) Inventory.Add("coins", $"Coins ({n})", false);
                    HudUI.Instance?.Toast("Got: Coin");
                    Show("f3_coin_" + Mathf.Clamp(n, 1, 3));
                });
            }
            Use("f3_vending", () =>
            {
                if (GameState.Has("f3_coffee_bought"))
                {
                    if (Inventory.Has("coffee")) Show("f3_vending_again");
                    else Show("f3_vending_idle");
                    return;
                }
                int n = CoinsFound;
                if (n >= 3)
                {
                    Inventory.Remove("coins");
                    GameState.Set("f3_coffee_bought");
                    Give("coffee", "Hot coffee (lid off)");
                    Show("f3_vending_coffee");
                    return;
                }
                if (!GameState.Has("f3_liam_met")) { Show("f3_vending_idle"); return; }
                var lines = DialogueBank.Get("f3_vending_short");
                var filled = new string[lines.Length];
                for (int i = 0; i < lines.Length; i++) filled[i] = lines[i].Replace("{n}", n == 0 ? "none" : n.ToString());
                DialogueUI.Instance.Show("", filled);
            });

            Use("f3_terminal", () =>
            {
                if (Dark) { Show("f3_pc_dead"); return; }
                TerminalUI.Instance.OpenTerminal(tutorial, GameState.Has("f3_hacked"), "Server Room",
                    () =>
                    {
                        GameState.Set("f3_hacked");
                        GameState.Clearance = Mathf.Max(GameState.Clearance, 4);
                        HudUI.Instance.Toast("Clearance: Server Room");
                    },
                    () => { if (!tutorialDone) { tutorialDone = true; StartCoroutine(SayWhenFree("f3_terminal_done")); } });
            });

            // the CEO's office: his keycard, his laptop
            Use("f3_ceo_desk", () =>
            {
                if (Inventory.Has("keycard_ceo") || GameState.Has("f3_code")) { Show("f3_ceo_desk_empty"); return; }
                Give("keycard_ceo", "CEO's keycard");
                Show("f3_ceo_card");
            });
            Use("f3_ceo_pc", () => StartCoroutine(CeoMail()));

            Use("f3_bin", () =>
            {
                var cart = JanitorCart.Find();
                if (cart.Held || (cart.Spawned && cart.RoomId == "f3_closet")) { Show("f3_bin_done"); return; }
                cart.Spawn(rooms.Current, rooms.MarkerPosition("f3_closet", "cart_spawn"));
                Show("f3_bin");
            });

            // the pen drive prints on HR's printer, one floor down
            var printer = ScriptedInteractable.Find("f2_printer");
            if (printer != null)
            {
                var hr = printer.OnUse;
                printer.OnUse = () =>
                {
                    if (Inventory.Has("pen_drive") && !GameState.Has("f3_keymap"))
                    {
                        Inventory.Remove("pen_drive");
                        GameState.Set("f3_keymap");
                        Give("keymap", "Key map (printout)");
                        Show("f3_print_keymap");
                        HudUI.Instance?.Toast("Clue found (KODEX)");
                        return;
                    }
                    hr?.Invoke();
                };
            }

            if (PhoneUI.Instance != null) PhoneUI.Instance.StocksBought += () => StartCoroutine(SayWhenFree("f3_bank_bought"));
        }

        private IEnumerator CeoMail()
        {
            bool first = !GameState.Has("f3_mails_read");
            if (!first) { yield return Say("f3_ceo_mail_again"); yield break; }
            yield return Say("f3_ceo_mail");
            GameState.Set("f3_mails_read");
            yield return Say(GameState.Has("f2_magicorp") ? "f3_ceo_mail_magicorp" : "f3_ceo_mail_plain");
        }

        private static int CoinsFound
        {
            get { int n = 0; for (int i = 1; i <= 3; i++) if (GameState.Has("f3_coin_" + i)) n++; return n; }
        }

        private static bool Dark => GameState.Has("power_out_floor3");

        /// <summary>A green computer: it sees its own table plus whatever the link station points it at.</summary>
        private void PuzzlePc(string hotspot, string machine, string puzzleId, string doneLine, Action onFirstSolve)
        {
            var h = ScriptedInteractable.Find(hotspot);
            if (h == null || !puzzles.TryGetValue(puzzleId, out var puzzle)) return;
            h.OnUse = () =>
            {
                if (Dark) { Show("f3_pc_dead"); return; }
                bool before = GameState.Has(puzzle.rewardFlag);
                TerminalUI.Instance.Open(ViewFor(puzzle, machine), ComputerSkin.EmployeePc, () =>
                {
                    if (before || !GameState.Has(puzzle.rewardFlag)) return;
                    onFirstSolve?.Invoke();
                    Show(doneLine);
                });
            };
        }

        /// <summary>The puzzle as this computer sees it right now: its own table and the linked machine's.</summary>
        public PuzzleData ViewFor(PuzzleData p, string machine)
        {
            var view = JsonUtility.FromJson<PuzzleData>(JsonUtility.ToJson(p));
            var keep = new List<string> { MachineTables[Array.IndexOf(Machines, machine)] };
            string linked = LinkOf(machine);
            if (linked != null) keep.Add(MachineTables[Array.IndexOf(Machines, linked)]);
            view.tables = Array.FindAll(p.tables, t => keep.Contains(t.name));
            int me = Array.IndexOf(Machines, machine);
            string status = linked == null
                ? $"THIS COMPUTER: {MachineNames[me]}, table {MachineTables[me]}.\nLINK: reads from nothing. Only {MachineTables[me]} is here.\n\n"
                : $"THIS COMPUTER: {MachineNames[me]}, table {MachineTables[me]}.\nLINK: reads from {NameOf(linked)}, so {MachineTables[Array.IndexOf(Machines, linked)]} is here too.\n\n";
            view.brief = status + p.brief;
            if (!string.IsNullOrEmpty(p.briefMissing)) view.briefMissing = status + p.briefMissing;
            return view;
        }

        public PuzzleData Puzzle(string id) => puzzles.TryGetValue(id, out var p) ? p : null;

        // ------------------------------------------------------------------ helpers

        private static void Use(string hotspot, Action action)
        {
            var h = ScriptedInteractable.Find(hotspot);
            if (h != null) h.OnUse = action;
        }

        private static void Give(string id, string name)
        {
            Inventory.Add(id, name);
            HudUI.Instance?.Toast("Got: " + name);
        }

        private static void Show(string id) => DialogueUI.Instance.Show("", DialogueBank.Get(id));

        private static IEnumerator Say(string id)
        {
            yield return new WaitUntil(() => !DialogueUI.Instance.IsOpen);
            bool done = false;
            DialogueUI.Instance.Show("", DialogueBank.Get(id), () => done = true);
            yield return new WaitUntil(() => done);
        }

        private IEnumerator SayWhenFree(string id)
        {
            yield return new WaitUntil(() => !UiKit.AnyModalOpen);
            yield return Say(id);
        }

        private IEnumerator Call(string id)
        {
            yield return new WaitUntil(() => !UiKit.AnyModalOpen && !rooms.IsTransitioning && !DialogueUI.Instance.IsOpen);
            bool answered = false;
            PhoneUI.Instance.Ring(Caller, () => answered = true);
            yield return new WaitUntil(() => answered);
            yield return Say(id);
        }

        private bool Paused => UiKit.AnyModalOpen || DialogueUI.Instance.IsOpen || rooms.IsTransitioning;

        /// <summary>Wait this many seconds of actual play (menus, dialogue and fades don't count).</summary>
        private IEnumerator Unpaused(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (!Paused) t += Time.deltaTime;
                yield return null;
            }
        }

        private bool InRoom(string id) => rooms.Current != null && rooms.Current.roomId == id && !rooms.IsTransitioning;
    }
}

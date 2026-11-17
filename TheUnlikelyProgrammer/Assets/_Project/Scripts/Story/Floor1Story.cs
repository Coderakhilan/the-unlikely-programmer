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
    /// Floor 1 (Administration): find out who controls the building's power.
    ///
    /// Room chain (keys from drawers/shelves, door codes from computers, info slips needed in other queries):
    ///   open office SW  drawer -> Office A key           | PC: meeting room is booked all day (Linda + Ken)
    ///   open office SE  drawer -> slip "vendor V-207"     | PC: Office E code 2468
    ///   Office A (key)  PC -> Office B code 3172
    ///   Office B (3172) drawer -> Office C key            | PC: Facilities moved to HR; Joey's reputation
    ///   Office C (key)  PC (needs V-207) -> Office D code 5590
    ///   Office D (5590) PC -> cost centre PF-02 (printout)
    ///   Office E (2468) shelf -> Office F key             | PC: plans moved to HR plant room
    ///   Office F (key)  PC -> manager's spare key is in lounge locker 4
    ///   lounge locker 4 -> Manager's key
    ///   Manager's office (key) PC (needs PF-02) -> main power is HR, Plant room 2B; desk -> Linda's tape recorder
    ///   washroom: tape in the cubicle -> Linda and Ken leave the meeting room -> employee terminal
    ///   terminal: hack (clearance Human Resources) + LIKE / IN / NOT / ORDER BY / GROUP BY lessons
    ///   near the elevator: the call. Truth or lie (flags d1_truth / d1_lie, used for the ending).
    /// Patrolling staff (open offices, Office C, Office F) shoo Donnie off their desks unless he's pushing his cart.
    /// </summary>
    public class Floor1Story : MonoBehaviour
    {
        [SerializeField] private TextAsset dialogue;
        [SerializeField] private TextAsset[] puzzleFiles;
        [SerializeField] private TextAsset[] tutorialSteps;

        private const string Floor = "floor1";
        private const string Caller = "Unknown number";

        private RoomManager rooms;
        private readonly Dictionary<string, PuzzleData> puzzles = new Dictionary<string, PuzzleData>();
        private PuzzleData[] tutorial;
        private bool tutorialDone;

        public void Configure(TextAsset dialogueJson, TextAsset[] puzzleJson, TextAsset[] tutorialJson)
        {
            dialogue = dialogueJson;
            puzzleFiles = puzzleJson;
            tutorialSteps = tutorialJson;
        }

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

            SetupPeople();
            SetupHotspots();
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ the floor, in order

        private IEnumerator Run()
        {
            var hud = HudUI.Instance;
            // anywhere on this floor (a resumed game may start in one of its offices)
            yield return new WaitUntil(() => (GameState.Clearance >= 1 && OnThisFloor) || GameState.Has("f1_complete"));
            if (GameState.Has("f1_complete")) yield break;   // finished (or skipped by a dev jump)
            if (jumpToCall) goto Call;

            yield return StoryBeats.Beat("f1_arrive", Arrival());
            if (!GameState.Has("f1_m_systems_solved"))
            {
                hud.SetObjective("Find out who controls the building's power");
                hud.SetHint("Bring your cart into staffed offices. Use computers when nobody is looking.");
            }

            yield return new WaitUntil(() => GameState.Has("f1_m_systems_solved") && !TerminalUI.Instance.IsOpen);
            hud.SetHint(null);
            if (!Inventory.Has("tape") && !GameState.Has("f1_tape_used")) hud.SetObjective("Check the shelf in Manager Linda's office");
            yield return new WaitUntil(() => Inventory.Has("tape") || GameState.Has("f1_tape_used"));

            if (!GameState.Has("f1_meeting_open")) hud.SetObjective("Put the tape in the men's washroom cubicle");
            yield return new WaitUntil(() => GameState.Has("f1_meeting_open"));

            if (!GameState.Has("f1_hacked")) hud.SetObjective("Plug into the employee terminal in the meeting room");
            yield return new WaitUntil(() => GameState.Has("f1_hacked"));
            hud.SetObjective("Finish KODEX's lessons on the terminal");
            yield return new WaitUntil(() => tutorialDone && !TerminalUI.Instance.IsOpen);
            yield return StoryBeats.Beat("f1_terminal_done", Say("f1_terminal_done"));

            hud.SetObjective("Head back to the elevator");
        Call:
            if (!GameState.Has("d1_truth") && !GameState.Has("d1_lie"))
            {
                yield return new WaitUntil(() => InRoom(Floor) && Near(Floor, "elevator", 3f));
                yield return StoryBeats.Beat("f1_decision", DecisionCall());
            }

            GameState.Set("f1_complete");
            hud.SetObjective("Take the elevator to Floor 2");   // Floor2Story takes over from there
        }

        private IEnumerator Arrival()
        {
            yield return Say("f1_arrive");
            yield return Call("f1_arrival_call");
        }

        /// <summary>Donnie is somewhere on Administration (its corridor or any of its rooms).</summary>
        private bool OnThisFloor => rooms.Current != null && !rooms.IsTransitioning && Darkness.FloorOf(rooms.Current.roomId) == "1";

        /// <summary>A resumed game (after WorldState put everyone back): what the floor's done steps left running.</summary>
        public void Resume()
        {
            tutorialDone = tutorial != null && tutorial.Length > 0 && Array.TrueForAll(tutorial, t => GameState.Has(t.rewardFlag));
            // Linda and Ken are still waiting outside the gents' (WorldState put them there): their commentary goes on
            if (GameState.Has("f1_meeting_open") && !GameState.Has("f1_complete")) StartCoroutine(LindaAndKenTalk());
        }

        private bool jumpToCall;

        /// <summary>Dev jump (F10 menu): everything on Floor 1 done; Donnie at the lift, where the truth-or-lie call rings.</summary>
        public void DevJumpToDecision()
        {
            jumpToCall = true;
            tutorialDone = true;
            foreach (var f in new[] { "f1_m_systems_solved", "f1_meeting_open", "f1_hacked" }) GameState.Set(f);
            foreach (var l in ManualLessons.All) if (l.floor <= 1) GameState.Set("lesson_" + l.id);
            GameState.Clearance = Mathf.Max(GameState.Clearance, 2);
            RoomManager.Instance.GoTo(Floor, "elevator");
        }

        /// <summary>D1: truth or lie about the power supply. Remembered for the ending.</summary>
        private IEnumerator DecisionCall()
        {
            yield return new WaitUntil(() => !UiKit.AnyModalOpen && !rooms.IsTransitioning && !DialogueUI.Instance.IsOpen);
            bool answered = false;
            PhoneUI.Instance.Ring(Caller, () => answered = true);
            yield return new WaitUntil(() => answered);
            yield return Say("f1_decision_call");

            int picked = -1;
            DialogueUI.Instance.ShowChoice(DialogueBank.One("f1_decision_prompt"),
                new[] { DialogueBank.One("f1_choice_truth"), DialogueBank.One("f1_choice_lie") }, i => picked = i);
            yield return new WaitUntil(() => picked >= 0);

            if (picked == 0) { GameState.Set("d1_truth"); yield return Say("f1_truth"); }
            else { GameState.Set("d1_lie"); yield return Say("f1_lie"); }
        }

        // ------------------------------------------------------------------ setup

        private void SetupPeople()
        {
            void Barks(string id, string entry)
            {
                var n = Npc.Find(id);
                if (n == null) return;
                var lines = DialogueBank.Get(entry);
                var plain = new string[lines.Length];
                for (int i = 0; i < lines.Length; i++) plain[i] = DialogueBank.Split(lines[i]).text;
                n.SetBarks(plain);
            }
            Barks("f1_guard_sw", "npc_sw");
            Barks("f1_guard_se", "npc_se");
            Barks("omar", "npc_omar");
            Barks("tomas", "npc_tomas");
            Barks("joey", "npc_joey");
            Barks("f1_lounge_b", "npc_lounge");
            Barks("f1_hall_a", "npc_f1_hall_a");
            Barks("f1_hall_b", "npc_f1_hall_b");

            void Shoo(string id, string entry)
            {
                var n = Npc.Find(id);
                var g = n != null ? n.GetComponent<Guard>() : null;
                if (g == null) return;
                var lines = DialogueBank.Get(entry);
                var plain = new string[lines.Length];
                for (int i = 0; i < lines.Length; i++) plain[i] = DialogueBank.Split(lines[i]).text;
                g.SetCaughtLines(plain);
            }
            Shoo("f1_guard_sw", "guard_sw");
            Shoo("f1_guard_se", "guard_se");
            Shoo("omar", "guard_c");
            Shoo("tomas", "guard_f");
        }

        private void SetupHotspots()
        {
            // computers: open the puzzle; first time it's solved, Donnie says what it means
            Pc("f1_pc_sw", "f1_sw_bookings", "f1_pc_sw_done");
            Pc("f1_pc_se", "f1_se_tickets", "f1_pc_se_done", null, () => Give("code_f1_e", "Office E code: 2468"));
            Pc("f1_pc_a", "f1_a_doorcodes", "f1_pc_a_done", null, () => Give("code_f1_b", "Office B code: 3172"));
            Pc("f1_pc_b", "f1_b_inbox", "f1_pc_b_done");
            Pc("f1_pc_c", "f1_c_invoices", "f1_pc_c_done", null,
                () => { Inventory.Remove("slip"); Give("code_f1_d", "Office D code: 5590"); });
            Pc("f1_pc_d", "f1_d_budget", "f1_pc_d_done", null, () => Give("budget", "Budget printout (PF-02)"));
            Pc("f1_pc_e", "f1_e_archive", "f1_pc_e_done");
            Pc("f1_pc_f", "f1_f_keys", "f1_pc_f_done");
            Pc("f1_pc_m", "f1_m_systems", "f1_pc_m_done", null, () => Inventory.Remove("budget"));

            // things to search
            Search("f1_sw_drawer", "f1_sw_drawer", () => Give("key_f1_a", "Office A key"));
            Search("f1_se_drawer", "f1_se_drawer", () => Give("slip", "Slip: vendor V-207"));
            Search("f1_b_drawer", "f1_b_drawer", () => Give("key_f1_c", "Office C key"));
            Search("f1_d_drawer", "f1_d_drawer", null);
            Search("f1_e_shelf", "f1_e_shelf", () => Give("key_f1_f", "Office F key"));

            Use("f1_locker", () =>
            {
                if (GameState.Has("f1_got_key_f1_m")) Show("f1_searched");
                else if (!GameState.Has("f1_f_keys_solved")) Show("f1_locker_unknown");
                else { Give("key_f1_m", "Manager Linda's office key"); GameState.Set("f1_got_key_f1_m"); Show("f1_locker"); }
            });

            Use("f1_m_shelf", () =>
            {
                if (Inventory.Has("tape") || GameState.Has("f1_tape_used")) Show("f1_searched");
                else if (!GameState.Has("f1_m_systems_solved")) Show("f1_m_shelf");
                else { Give("tape", "Tape recorder"); StartCoroutine(SayThen("f1_tape_found")); }
            });

            Use("f1_bin", () =>
            {
                var cart = JanitorCart.Find();
                if (cart.Held || (cart.Spawned && cart.RoomId == "f1_closet")) { Show("f1_bin_done"); return; }
                cart.Spawn(rooms.Current, rooms.MarkerPosition("f1_closet", "cart_spawn"));
                Show("f1_bin");
            });

            var cubicle = ScriptedInteractable.Find("f1_toilet");
            if (cubicle != null) cubicle.PromptOverride = () => Inventory.Has("tape") ? "Put the tape in the cubicle" : "End cubicle";
            Use("f1_toilet", () =>
            {
                if (!Inventory.Has("tape")) { Show("f1_toilet"); return; }
                StartCoroutine(TheGag());
            });

            Use("f1_terminal", () =>
            {
                TerminalUI.Instance.OpenTerminal(tutorial, GameState.Has("f1_hacked"), "Floor 2 (Human Resources)",
                    () =>
                    {
                        GameState.Set("f1_hacked");
                        GameState.Clearance = Mathf.Max(GameState.Clearance, 2);
                        HudUI.Instance.Toast("Clearance: Human Resources");
                        // a fresh start for the next floor: only the USB-C cable stays (it plugs into every terminal)
                        foreach (var item in new System.Collections.Generic.List<Inventory.Item>(Inventory.Items))
                            if (item.id != "usb") Inventory.Remove(item.id);
                    },
                    () => tutorialDone = true);
            });
        }

        private IEnumerator TheGag()
        {
            Inventory.Remove("tape");
            GameState.Set("f1_tape_used");
            yield return Say("f1_gag");
            // Linda and Ken go to "check on Joey"
            foreach (var id in new[] { "linda", "ken" })
            {
                var n = Npc.Find(id);
                if (n != null) n.gameObject.SetActive(false);
            }
            GameState.Set("f1_meeting_open");
            HudUI.Instance.Toast("The meeting room is empty");
            StartCoroutine(LindaAndKenWait());
        }

        /// <summary>As Donnie steps out of the gents', Linda and Ken arrive from the meeting room and wait outside.</summary>
        private IEnumerator LindaAndKenWait()
        {
            yield return new WaitUntil(() => InRoom(Floor));
            var linda = Npc.Find("linda_hall");
            var ken = Npc.Find("ken_hall");
            Vector3 M(string id) => rooms.MarkerPosition(Floor, id);
            if (linda != null)
            {
                linda.gameObject.SetActive(true);
                linda.transform.position = M("meeting_door_out");
                linda.Talkable = false;
                linda.WalkPath(new[] { M("hall_corner"), M("hall_turn_a"), M("wash_wait_a") }, () => linda.Face(Vector2.left));
            }
            yield return new WaitForSeconds(0.7f);
            if (ken != null)
            {
                ken.gameObject.SetActive(true);
                ken.transform.position = M("meeting_door_out");
                ken.Talkable = false;
                ken.WalkPath(new[] { M("hall_corner"), M("hall_turn_b"), M("wash_wait_b") }, () => ken.Face(Vector2.left));
            }
            yield return LindaAndKenTalk();
        }

        /// <summary>A little commentary while they wait (shown when Donnie is nearby on this floor).</summary>
        private IEnumerator LindaAndKenTalk()
        {
            var linda = Npc.Find("linda_hall");
            var ken = Npc.Find("ken_hall");
            var lindaLines = DialogueBank.Get("f1_wait_linda");
            var kenLines = DialogueBank.Get("f1_wait_ken");
            for (int i = 0; ; i++)
            {
                yield return new WaitForSeconds(3.5f);
                if (!InRoom(Floor)) continue;
                if (i % 2 == 0 && linda != null) linda.Bark(lindaLines[(i / 2) % lindaLines.Length], 3f);
                if (i % 2 == 1 && ken != null) ken.Bark(kenLines[(i / 2) % kenLines.Length], 3f);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void Pc(string hotspot, string puzzleId, string doneLine, Func<string> blockedLine = null, Action onFirstSolve = null)
        {
            var h = ScriptedInteractable.Find(hotspot);
            if (h == null || !puzzles.TryGetValue(puzzleId, out var puzzle)) return;
            h.OnUse = () =>
            {
                bool before = GameState.Has(puzzle.rewardFlag);
                TerminalUI.Instance.Open(puzzle, ComputerSkin.EmployeePc, () =>
                {
                    if (!before && GameState.Has(puzzle.rewardFlag))
                    {
                        onFirstSolve?.Invoke();
                        Show(doneLine);
                    }
                    else if (!GameState.Has(puzzle.rewardFlag) && blockedLine != null)
                    {
                        string line = blockedLine();
                        if (line != null) Show(line);
                    }
                });
            };
        }

        private void Search(string hotspot, string line, Action reward)
        {
            Use(hotspot, () =>
            {
                string flag = "searched_" + hotspot;
                if (GameState.Has(flag)) { Show("f1_searched"); return; }
                GameState.Set(flag);
                reward?.Invoke();
                Show(line);
            });
        }

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
            bool done = false;
            DialogueUI.Instance.Show("", DialogueBank.Get(id), () => done = true);
            yield return new WaitUntil(() => done);
        }

        private static IEnumerator SayThen(string id) { yield return Say(id); }

        private IEnumerator Call(string id)
        {
            yield return new WaitUntil(() => !UiKit.AnyModalOpen && !rooms.IsTransitioning && !DialogueUI.Instance.IsOpen);
            bool answered = false;
            PhoneUI.Instance.Ring(Caller, () => answered = true);
            yield return new WaitUntil(() => answered);
            yield return Say(id);
        }

        private bool InRoom(string id) => rooms.Current != null && rooms.Current.roomId == id && !rooms.IsTransitioning;

        private bool Near(string roomId, string marker, float metres)
        {
            var p = PlayerController.Instance;
            return p != null && Vector2.Distance(p.transform.position, rooms.MarkerPosition(roomId, marker)) <= metres * p.transform.localScale.x;
        }
    }
}

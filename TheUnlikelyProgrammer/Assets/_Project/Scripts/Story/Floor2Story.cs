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
    /// Floor 2 (Human Resources): cut the whole building's power.
    ///
    ///   arrival call: the kidnappers want the building dark; the breaker only does one floor; find "Plant Room 2B".
    ///   Head of HR walks the corridors all the time: seen out there without the cart = chased; caught = game over.
    ///   HR open office: four computers -> 2B is an off-site plant, switched from this floor's breaker room (Donnie said so).
    ///   breaker panel: needs an authorised duty login -> the reception computer's duty list, which needs five clues:
    ///     printer room (role ends in Engineer), token in the conference plant (2014), projector video (Night shift),
    ///     the hungry candidate on the sofa, fed crisps from the Floor 0 vending machine with that token (only 'active'),
    ///     the interview room computer (Bram's login dies at noon; also the restructure file = leverage).
    ///   employee terminal (long hallway, centre table): hack (Executive clearance) + JOIN / keys / LIMIT lessons.
    ///   breaker panel with the login: the call. Truth or lie (d2_truth / d2_lie). Either way: Liam is behind it.
    ///     lie:   Ryker (the spy) comes to kill Donnie and clears the floor ("fire!"). Donnie kills the floor's power.
    ///            In the dark: torch, hide in lockers/plants, flick the beam to lure Ryker into the interview room, lock it.
    ///            Ryker lets slip MagiCorp's database login. Donnie's leverage buys the deal: overheat the server room.
    ///   truth: the kidnappers switch the building off and tip off security. Dodge security to the elevator; same deal.
    /// </summary>
    public class Floor2Story : MonoBehaviour
    {
        [SerializeField] private TextAsset dialogue;
        [SerializeField] private TextAsset[] puzzleFiles;
        [SerializeField] private TextAsset[] tutorialSteps;

        public static Floor2Story Instance { get; private set; }

        private const string Floor = "floor2";
        private const string Caller = "Unknown number";
        // the open office's four computers: t1 + t2 move the story on; t3 (plants) leads to the coin, t4 (lock code) to the Ryker trap
        private static readonly string[] OfficePcs = { "f2_t1_northgate", "f2_t2_remote", "f2_t3_plants", "f2_t4_locks" };
        private static readonly string[] Compulsory = { "f2_t1_northgate", "f2_t2_remote" };
        private static readonly string[] People =
        {
            "f2_hr", "f2_recep", "f2_wait_a", "f2_wait_b", "f2_wait_c", "f2_wait_d", "f2_candidate", "f2_interviewer", "f2_iv_candidate",
            "f2_office_staff", "f2_office_a", "f2_office_b", "f2_printer_guy", "f2_conf", "f2_hall", "f2_lift"
        };

        private RoomManager rooms;
        private readonly Dictionary<string, PuzzleData> puzzles = new Dictionary<string, PuzzleData>();
        private PuzzleData[] tutorial;
        private bool tutorialDone;

        /// <summary>Where the floor's ending is: "", "cut" (lie: kill the lights), "ryker", "trapped", "restore", "security", "done".</summary>
        public string Phase { get; private set; } = "";
        public bool RykerInside { get; private set; }
        private float insideFor;
        private bool decisionRunning;

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

            SetupPeople();
            SetupHotspots();
            Hunter.Caught += OnCaught;
            Door.PromptHook = DoorPrompt;
            Door.InteractHook = DoorUse;
            ElevatorUI.StoryBlock = floor => floor >= 3 && !GameState.Has("f2_complete") ? DialogueBank.One("f2_floor3_blocked") : null;
            Elevator.Blocked = () => Phase == "cut" || Phase == "ryker" || Phase == "trapped" || Phase == "restore" || Phase == "security"
                ? DialogueBank.One("f2_elevator_dark") : null;
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Hunter.Caught -= OnCaught;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ the floor, in order

        private IEnumerator Run()
        {
            var hud = HudUI.Instance;
            // anywhere on this floor (a resumed game may start in one of its rooms)
            yield return new WaitUntil(() => (GameState.Clearance >= 2 && OnThisFloor) || GameState.Has("f2_complete"));
            if (GameState.Has("f2_complete")) { Phase = "done"; BackToNormal(); yield break; }   // (dev skip to Floor 3)

            yield return StoryBeats.Beat("f2_arrive", Arrival());
            if (Phase == "" && !Decided) SetHunter("f2_hr", true);   // not once the call has changed everything (Resume sets that up)

            if (!Solved(Compulsory))
            {
                hud.SetObjective("Find out where Plant Room 2B is");
                hud.SetHint("Head of HR patrols the corridors. Keep your cart with you out there.");
            }
            yield return new WaitUntil(() => Solved(Compulsory) && !TerminalUI.Instance.IsOpen && !DialogueUI.Instance.IsOpen);
            hud.SetHint(null);
            GameState.Set("f2_plant_found");
            yield return StoryBeats.Beat("f2_plant", PlantReveal());

            if (!GameState.Has("f2_need_login")) hud.SetObjective("Check the breaker panel (breaker room)");
            yield return new WaitUntil(() => GameState.Has("f2_need_login"));
            if (!Inventory.Has("creds") && !Decided)
            {
                hud.SetObjective("Find the right duty login on the reception computer");
                hud.SetHint("KODEX lists clues as you find them. Look around the floor.");
            }

            // (the login is used up at the panel: after the call, it's gone for good)
            yield return new WaitUntil(() => (Inventory.Has("creds") || Decided) && !TerminalUI.Instance.IsOpen);
            hud.SetHint(null);
            if (!GameState.Has("f2_hacked") || !tutorialDone)
            {
                hud.SetObjective("Get Executive clearance at the employee terminal (long hallway)");
                yield return new WaitUntil(() => GameState.Has("f2_hacked") && tutorialDone && !TerminalUI.Instance.IsOpen);
            }
            if (!Decided) hud.SetObjective("Log in at the breaker panel");
            yield return Ending();
        }

        private IEnumerator Arrival()
        {
            var hud = HudUI.Instance;
            yield return Say("f2_arrive");
            yield return Call("f2_arrival_call");
            hud.SetObjective("Find out where Plant Room 2B is");
            if (!Guard.HasCart()) yield return Say("f2_hr_warning");
        }

        private IEnumerator PlantReveal()
        {
            yield return Say("f2_plant_reveal");
            yield return Call("f2_insecure_call");
        }

        /// <summary>The truth-or-lie call at the breaker panel has been made.</summary>
        private static bool Decided => GameState.Has("d2_truth") || GameState.Has("d2_lie");

        /// <summary>Donnie is somewhere on Human Resources (its corridor or any of its rooms).</summary>
        private bool OnThisFloor => rooms.Current != null && !rooms.IsTransitioning && Darkness.FloorOf(rooms.Current.roomId) == "2";

        /// <summary>
        /// Safe to autosave: before the call, waiting to cut the power (lie), Ryker locked in and the power still off (lie),
        /// or done. Not during the call, the hunts in the dark, or the scripted bits between them.
        /// </summary>
        public bool CanSave => (Phase == "" || Phase == "cut" || Phase == "restore" || Phase == "done") && !decisionRunning && !ambushing && !dodging;

        /// <summary>
        /// A resumed game (after WorldState put everyone back): which part of the floor's ending it is, from the flags.
        /// </summary>
        public void Resume()
        {
            tutorialDone = tutorial != null && tutorial.Length > 0 && Array.TrueForAll(tutorial, t => GameState.Has(t.rewardFlag));
            if (GameState.Has("f2_complete")) { Phase = "done"; return; }
            if (GameState.Has("d2_lie"))
            {
                // lie: Ryker's been sent; the floor is empty. Locked in already = turn the power back on, else cut it.
                Evacuate();
                SetHunter("f2_hr", false);
                SetHunter("ryker_f2", false);
                HideSpot.Enabled = false;
                bool trapped = GameState.Has("f2_ryker_trapped");
                Phase = trapped ? "restore" : "cut";
                HudUI.Instance.SetObjective(trapped ? "Turn the power back on (breaker panel)" : "Cut this floor's power at the breaker panel");
                return;
            }
            Phase = "";
            if (StoryBeats.Done("f2_arrive")) SetHunter("f2_hr", true);
        }

        private IEnumerator Ending()
        {
            var hud = HudUI.Instance;
            yield return new WaitUntil(() => Phase == "done");
            GameState.Set("f2_complete");
            hud.SetHint(null);
            hud.SetObjective("Take the elevator to Floor 3");   // Floor3Story takes over from there
            yield return new WaitUntil(() => rooms.Current != null && rooms.Current.roomId == "floor3");
            BackToNormal();
        }

        /// <summary>
        /// After Floor 2's job: a normal working day again. Everyone is back (the 'fire' and the blackout are over), Head of HR
        /// no longer hunts, security and Ryker are gone. On the lie branch Ryker stays locked in the interview room.
        /// </summary>
        public void BackToNormal()
        {
            foreach (var id in People)
            {
                var n = Npc.Find(id);
                if (n != null) n.gameObject.SetActive(true);
            }
            SetHunter("f2_hr", false);
            foreach (var id in new[] { "ryker_f2", "f2_sec_1", "f2_sec_2", "f2_sec_3" })
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                SetHunter(id, false);
                n.gameObject.SetActive(false);
            }
            HideSpot.Enabled = false;
            for (int f = 0; f <= 2; f++) GameState.Unset("power_out_floor" + f);
        }

        /// <summary>
        /// Development jump (F10 menu): everything on Floor 2 done up to the truth-or-lie call.
        /// Donnie is put in the breaker room and the call starts. The torch is NOT given, so its table can be tested too.
        /// </summary>
        public void DevSkipToDecision()
        {
            StopAllCoroutines();
            GameState.Clearance = Mathf.Max(GameState.Clearance, 3);
            foreach (var f in new[] { "has_manual", "f0_complete", "f1_complete", "f2_plant_found", "f2_need_login", "f2_hacked",
                         "f2_clue_printer", "f2_clue_video", "f2_clue_candidate", "f2_clue_interview", "f2_got_coin", "f2_leverage",
                         "f2_reception_logins_solved", "f2_iv_restructure_solved" })
                GameState.Set(f);
            foreach (var id in OfficePcs) GameState.Set(id + "_solved");
            foreach (var l in ManualLessons.All) GameState.Set("lesson_" + l.id);
            tutorialDone = true;
            Inventory.Add("creds", "Duty login: i.vidal", false);
            Inventory.Add("code_iv", "Interview room lock code: 7341", false);
            Inventory.Add("restructure", "Restructure file (copy)", false);
            SetHunter("f2_hr", false);
            Phase = "";
            HudUI.Instance.SetHint(null);
            HudUI.Instance.SetObjective("Log in at the breaker panel");
            HudUI.Instance.Toast("DEV: Floor 2 skipped to the call");
            StartCoroutine(DevDecision());
        }

        private IEnumerator DevDecision()
        {
            rooms.GoTo("f2_breaker", "door0");
            yield return new WaitUntil(() => !rooms.IsTransitioning && rooms.Current.roomId == "f2_breaker");
            StartCoroutine(Ending());
            yield return Decision();
        }

        private static bool Solved(string[] ids)
        {
            foreach (var id in ids) if (!GameState.Has(id + "_solved")) return false;
            return true;
        }

        private static bool AllOfficePcs() => Solved(OfficePcs);

        /// <summary>D2, at the breaker panel with the login: truth or lie about the switch.</summary>
        private IEnumerator Decision()
        {
            decisionRunning = true;
            yield return Say("f2_panel_login");
            yield return Call("f2_decision_call");
            int picked = -1;
            DialogueUI.Instance.ShowChoice(DialogueBank.One("f2_decision_prompt"),
                new[] { DialogueBank.One("f2_choice_truth"), DialogueBank.One("f2_choice_lie") }, i => picked = i);
            yield return new WaitUntil(() => picked >= 0);
            Inventory.Remove("creds");
            var hud = HudUI.Instance;

            if (picked == 1)
            {
                GameState.Set("d2_lie");
                GameState.Set("ryker_discovered");
                yield return Say("f2_lie");
                yield return RykerArrives();
                Phase = "cut";
                hud.SetObjective("Cut this floor's power at the breaker panel");
            }
            else
            {
                GameState.Set("d2_truth");
                yield return Say("f2_truth");
                Evacuate();
                for (int f = 0; f <= 3; f++) GameState.Set("power_out_floor" + f);
                yield return Say(Inventory.Has("flashlight") ? "f2_dark_truth_torch" : "f2_dark_truth");
                StartSecurity();
            }
            decisionRunning = false;
        }

        /// <summary>Lie branch: the camera goes to the stairs; Ryker comes up shouting "fire" and the floor empties.</summary>
        private IEnumerator RykerArrives()
        {
            var ryker = Npc.Find("ryker_f2");
            var hunter = ryker.GetComponent<Hunter>();
            hunter.enabled = false;
            ryker.Talkable = false;
            ryker.gameObject.SetActive(true);
            ryker.transform.position = rooms.MarkerPosition(Floor, "stairs_top");
            SetHunter("f2_hr", false);
            PlayerController.Frozen = true;   // Donnie stays put in the breaker room while we watch the stairs
            yield return rooms.Peek(Floor, ryker.transform);
            bool arrived = false;
            ryker.WalkPath(new[] { rooms.MarkerPosition(Floor, "stairs_bottom"), rooms.MarkerPosition(Floor, "ryker_stop") }, () => arrived = true);
            yield return new WaitUntil(() => arrived);
            ryker.Face(Vector2.left);
            foreach (var id in new[] { "f2_lift", "f2_wait_b", "f2_wait_c", "f2_recep" })
            {
                var n = Npc.Find(id);
                if (n != null && n.gameObject.activeInHierarchy) n.Bark(id == "f2_recep" ? "Fire?! Everyone, the stairs!" : "FIRE?!", 2f);
            }
            yield return Say("f2_ryker_fire");
            yield return new WaitForSeconds(0.6f);
            yield return rooms.EndPeek(Evacuate);
            PlayerController.Frozen = false;
            ryker.gameObject.SetActive(false);   // he's out there; he starts his beat when the lights go
            yield return Say("f2_ryker_fire_donnie");
        }

        /// <summary>Lie branch: the lights go, Ryker starts hunting.</summary>
        private IEnumerator CutPower()
        {
            GameState.Set("power_out_floor2");
            Phase = "ryker";
            yield return Say(Inventory.Has("flashlight") ? "f2_power_cut_lie_torch" : "f2_power_cut_lie");
            HideSpot.Enabled = true;
            var ryker = Npc.Find("ryker_f2");
            ryker.gameObject.SetActive(true);
            ryker.GetComponent<Hunter>().ResetToStart();
            ryker.GetComponent<Hunter>().enabled = true;
            HudUI.Instance.SetHint(DialogueBank.One("f2_hint_dark"));
            yield return TorchThen("Lure Ryker into the interview room, then lock him in");
        }

        private void StartSecurity()
        {
            Phase = "security";
            HideSpot.Enabled = true;
            foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" })
            {
                var n = Npc.Find(id);
                if (n == null) continue;
                n.gameObject.SetActive(true);
                var h = n.GetComponent<Hunter>();
                h.ResetToStart();
                h.enabled = true;
            }
            HudUI.Instance.SetHint(DialogueBank.One("f2_hint_dark"));
            StartCoroutine(TorchThen("Get to the elevator without security catching you"));
        }

        private IEnumerator TorchThen(string objective)
        {
            var hud = HudUI.Instance;
            if (!Inventory.Has("flashlight"))
            {
                hud.SetObjective("Grab the torch from the table in the long hallway");
                yield return new WaitUntil(() => Inventory.Has("flashlight"));
            }
            hud.SetObjective(objective);
        }

        private void Update()
        {
            if (rooms == null) return;
            if (Phase == "ryker") WatchTheTrap();
            if (Phase == "cut" && !ambushing && InRoom(Floor) && GameOverUI.Instance != null && !GameOverUI.Instance.IsOpen && !DialogueUI.Instance.IsOpen)
                StartCoroutine(Ambush());
            if (Phase == "security" && InRoom(Floor) && Near(Floor, "elevator", 2.2f) && !UiKit.AnyModalOpen && !DialogueUI.Instance.IsOpen)
                StartCoroutine(SecurityDodged());
        }

        /// <summary>Ryker follows a torch flick to the interview room door and goes in to look; then Donnie can lock it.</summary>
        private void WatchTheTrap()
        {
            var ryker = Npc.Find("ryker_f2");
            if (ryker == null) return;
            Vector3 door = rooms.MarkerPosition(Floor, "f2_interview_door0");
            if (RykerInside)
            {
                if (UiKit.AnyModalOpen || DialogueUI.Instance.IsOpen) return;
                insideFor -= Time.deltaTime;
                if (insideFor <= 0f)
                {
                    // he's had a look and comes back out
                    RykerInside = false;
                    ryker.gameObject.SetActive(true);
                    ryker.transform.position = door;
                    ryker.GetComponent<Hunter>().ResetTo(door);
                    ryker.Bark("Nice try.", 2f);
                }
                return;
            }
            var h = ryker.GetComponent<Hunter>();
            if (!ryker.gameObject.activeInHierarchy || !h.enabled) return;
            bool lured = h.SinceLure < 15f && Vector2.Distance(h.LurePoint, door) < 2.2f;
            if (lured && Vector2.Distance(ryker.transform.position, door) < 1.3f)
                SendRykerIn();
        }

        private bool ambushing;

        /// <summary>Lie branch: Donnie steps out before killing the lights. Ryker is right there.</summary>
        private IEnumerator Ambush()
        {
            ambushing = true;
            var p = PlayerController.Instance;
            var ryker = Npc.Find("ryker_f2");
            var hunter = ryker.GetComponent<Hunter>();
            hunter.enabled = false;
            PlayerController.Frozen = true;
            var grid = NavGrid.For(rooms.Current);
            Vector3 spot = p.transform.position + new Vector3(-1.4f, 0.2f, 0f);
            if (grid != null) spot = grid.Nearest(spot);
            ryker.gameObject.SetActive(true);
            ryker.transform.position = spot;
            ryker.Face(Vector2.right);
            yield return new WaitForSeconds(0.8f);   // just long enough to see him, then the usual caught screen
            PlayerController.Frozen = false;
            OnCaught(hunter);
            ambushing = false;
        }

        /// <summary>Ryker steps into the interview room (also used by tests).</summary>
        public void SendRykerIn()
        {
            var ryker = Npc.Find("ryker_f2");
            RykerInside = true;
            insideFor = 9f;
            ryker.Bark("Donnie? You in here?", 2f);
            ryker.gameObject.SetActive(false);
            Show("f2_ryker_in");
        }

        private string DoorPrompt(Door d)
        {
            if (Phase == "ryker" && RykerInside && d.TargetRoomId == "f2_interview") return "Lock the door (code)";
            return null;
        }

        private bool DoorUse(Door d)
        {
            if (Phase == "ryker" && RykerInside && d.TargetRoomId == "f2_interview")
            {
                if (!Inventory.Has("code_iv")) { Show("f2_lock_no_code"); return true; }
                Inventory.Remove("code_iv");
                StartCoroutine(Trapped());
                return true;
            }
            if (GameState.Has("f2_ryker_trapped") && d.TargetRoomId == "f2_interview")
            {
                Show("f2_ryker_door");   // he's still in there; nobody's opening that door
                return true;
            }
            bool dark = Phase == "ryker" || Phase == "security" || Phase == "trapped";
            if (dark && d.TargetRoomId != Floor && Darkness.FloorOf(d.TargetRoomId) == "2")
            {
                Show("f2_door_dark");
                return true;
            }
            return false;
        }

        private IEnumerator Trapped()
        {
            Phase = "trapped";
            RykerInside = false;
            GameState.Set("f2_ryker_trapped");
            HideSpot.Enabled = false;
            HudUI.Instance.SetHint(null);
            HudUI.Instance.Toast("Locked: Interview room");
            yield return Say("f2_ryker_trapped");
            GameState.Set("f2_magicorp");
            Inventory.Add("magicorp", "MagiCorp login (written on hand)", false);
            yield return Say("f2_deal_lie");
            GameState.Set("f2_deal");
            Phase = "restore";
            HudUI.Instance.SetObjective("Turn the power back on (breaker panel)");
        }

        private bool dodging;
        private IEnumerator SecurityDodged()
        {
            if (dodging) yield break;
            dodging = true;
            foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" }) SetHunter(id, false);
            HideSpot.Enabled = false;
            HudUI.Instance.SetHint(null);
            yield return Call("f2_deal_truth");
            GameState.Set("f2_deal");
            for (int f = 0; f <= 3; f++) GameState.Unset("power_out_floor" + f);
            yield return Say("f2_power_on_truth");
            yield return SecurityStandDown();
            Phase = "done";
        }

        /// <summary>Truth branch, lights back on: the guards come over to Donnie, mumble "false alarm" and leave by the stairs.</summary>
        private IEnumerator SecurityStandDown()
        {
            var grid = NavGrid.For(rooms.Current);
            var p = PlayerController.Instance;
            PlayerController.Frozen = true;
            var guards = new List<Npc>();
            foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" })
            {
                var n = Npc.Find(id);
                if (n == null || !n.gameObject.activeInHierarchy) continue;
                guards.Add(n);
            }
            int arrived = 0;
            for (int i = 0; i < guards.Count; i++)
            {
                var n = guards[i];
                // stand in a little arc in front of Donnie
                Vector3 spot = p.transform.position + new Vector3((i - (guards.Count - 1) / 2f) * 0.9f, -1.3f, 0f);
                if (grid != null) spot = grid.Nearest(spot);
                var route = grid != null ? grid.Path(n.transform.position, spot) : new List<Vector3> { spot };
                if (route.Count == 0) route.Add(spot);
                var who = n;
                n.WalkPath(route, () => { who.Face(Vector2.up); arrived++; });
            }
            float t = 0f;
            yield return new WaitUntil(() => arrived >= guards.Count || (t += Time.deltaTime) > 12f);
            foreach (var n in guards) if (n.IsWalking) { n.StopWalking(); n.Face(Vector2.up); }
            yield return Say("f2_security_stand_down");
            PlayerController.Frozen = false;
            // off they go to the lift, along the corridor (never through a wall), and they're gone
            Vector3 exit = rooms.MarkerPosition(Floor, "elevator");
            if (grid != null) exit = grid.Nearest(exit);
            foreach (var n in guards)
            {
                var who = n;
                var route = grid != null ? grid.Path(grid.Nearest(n.transform.position), exit) : null;
                if (route == null || route.Count == 0) { who.gameObject.SetActive(false); continue; }   // no clean way out: just gone
                n.WalkPath(route, () => who.gameObject.SetActive(false));
            }
        }

        // ------------------------------------------------------------------ caught

        private void OnCaught(Hunter h)
        {
            var room = h.GetComponentInParent<Room>(true);
            if (room == null || room.roomId != Floor) return;   // another floor's hunter
            if (GameOverUI.Instance == null || GameOverUI.Instance.IsOpen) return;
            if (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen) TerminalUI.Instance.Close();
            if (HideSpot.Current != null) HideSpot.Current.ComeOut();
            switch (h.Kind)
            {
                case Hunter.Mode.Hr:
                    GameOverUI.Instance.Show("CAUGHT", DialogueBank.One("f2_caught_hr"), () =>
                    {
                        h.ResetToStart();
                        rooms.GoTo(Floor, "elevator");
                    });
                    break;
                case Hunter.Mode.Ryker:
                    GameOverUI.Instance.Show("CAUGHT", DialogueBank.One("f2_caught_ryker"), () =>
                    {
                        RykerInside = false;
                        h.ResetToStart();
                        h.gameObject.SetActive(Phase != "cut");   // before the blackout he's waiting outside, not on his beat
                        rooms.GoTo("f2_breaker", "door0");   // safe in the breaker room; he's back on his beat
                    });
                    break;
                default:
                    GameOverUI.Instance.Show("CAUGHT", DialogueBank.One("f2_caught_security"), () =>
                    {
                        foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" }) { var n = Npc.Find(id); if (n != null) n.GetComponent<Hunter>().ResetToStart(); }
                        rooms.GoTo("f2_breaker", "door0");
                    });
                    break;
            }
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
            Barks("f2_hr", "npc_f2_hr");
            Barks("f2_recep", "npc_f2_recep");
            Barks("f2_wait_a", "npc_f2_wait_a");
            Barks("f2_wait_b", "npc_f2_wait_b");
            Barks("f2_wait_c", "npc_f2_wait_c");
            Barks("f2_wait_d", "npc_f2_wait_d");
            Barks("f2_interviewer", "npc_f2_interviewer");
            Barks("f2_iv_candidate", "npc_f2_iv_candidate");
            Barks("f2_office_staff", "npc_f2_office");
            Barks("f2_office_a", "npc_f2_office_a");
            Barks("f2_office_b", "npc_f2_office_b");
            Barks("f2_printer_guy", "npc_f2_printer");
            Barks("f2_conf", "npc_f2_conf");
            Barks("f2_hall", "npc_f2_hall");
            Barks("f2_lift", "npc_f2_lift");
            Shoo("f2_recep", "guard_f2_recep");
            Shoo("f2_interviewer", "guard_f2_interviewer");
            Shoo("f2_office_staff", "guard_f2_office");

            SetHunter("f2_hr", false);   // starts his rounds once Donnie has had the call
            foreach (var id in new[] { "ryker_f2", "f2_sec_1", "f2_sec_2", "f2_sec_3" })
            {
                var n = Npc.Find(id);
                if (n != null) n.gameObject.SetActive(false);
            }
            SetSpot("f2_hr", "hr_spot");
            SetSpot("ryker_f2", "ryker_spot");
            foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" }) SetSpot(id, "guard_sec");

            // the hungry candidate on the sofa
            var cand = Npc.Find("f2_candidate");
            if (cand != null) cand.OnTalk = () =>
            {
                if (GameState.Has("f2_clue_candidate")) Show("f2_candidate_done");
                else if (Inventory.IsEquipped("crisps"))
                {
                    Inventory.Remove("crisps");
                    GameState.Set("f2_clue_candidate");
                    Show("f2_candidate_thanks");
                    HudUI.Instance.Toast("Clue found (KODEX)");
                }
                else if (Inventory.Has("crisps")) Show("f2_candidate_need_equip");
                else { GameState.Set("f2_need_crisps"); Show("f2_candidate_hungry"); }
            };
        }

        private static void SetSpot(string id, string entry)
        {
            var n = Npc.Find(id);
            var h = n != null ? n.GetComponent<Hunter>() : null;
            if (h == null) return;
            var lines = DialogueBank.Get(entry);
            var plain = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++) plain[i] = DialogueBank.Split(lines[i]).text;
            h.SetSpotLines(plain);
        }

        private static void SetHunter(string id, bool on)
        {
            var n = Npc.Find(id);
            var h = n != null ? n.GetComponent<Hunter>() : null;
            if (h != null) h.enabled = on;
        }

        /// <summary>Everyone leaves the floor (the fire alarm, or the blackout).</summary>
        private static void Evacuate()
        {
            foreach (var id in People)
            {
                var n = Npc.Find(id);
                if (n != null) n.gameObject.SetActive(false);
            }
        }

        private void SetupHotspots()
        {
            Pc("f2_pc_t1", "f2_t1_northgate", "f2_pc_t1_done");
            Pc("f2_pc_t2", "f2_t2_remote", "f2_pc_t2_done");
            Pc("f2_pc_t3", "f2_t3_plants", "f2_pc_t3_done");
            Pc("f2_pc_t4", "f2_t4_locks", "f2_pc_t4_done", () => Give("code_iv", "Interview room lock code: 7341"));
            Pc("f2_pc_iv", "f2_iv_restructure", "f2_pc_iv_done", () =>
            {
                GameState.Set("f2_clue_interview");
                GameState.Set("f2_leverage");
                Inventory.Add("restructure", "Restructure file (copy)", false);
                HudUI.Instance.Toast("Clue found (KODEX)");
            });

            var recep = ScriptedInteractable.Find("f2_pc_recep");
            if (recep != null && puzzles.TryGetValue("f2_reception_logins", out var duty))
                recep.OnUse = () =>
                {
                    if (Dark) { Show("f2_terminal_dead"); return; }
                    if (!GameState.Has("f2_need_login")) { Show("f2_reception_before"); return; }
                    bool before = GameState.Has(duty.rewardFlag);
                    TerminalUI.Instance.Open(duty, ComputerSkin.EmployeePc, () =>
                    {
                        if (!before && GameState.Has(duty.rewardFlag))
                        {
                            Give("creds", "Duty login: i.vidal");
                            Show("f2_pc_recep_done");
                        }
                        else if (!GameState.Has(duty.rewardFlag) && duty.CluesFound < duty.clues.Length) Show("f2_recep_clues_missing");
                    });
                };

            Use("f2_printer", () => Clue("f2_clue_printer", "f2_printer"));
            Use("f2_projector", () =>
            {
                if (Dark) { Show("f2_terminal_dead"); return; }
                if (GameState.Has("f2_clue_video")) { Show("f2_projector_again"); return; }
                Clue("f2_clue_video", "f2_projector");
            });
            Use("f2_plant", () =>
            {
                if (GameState.Has("f2_got_coin")) { Show("f2_plant_empty"); return; }
                if (!GameState.Has("f2_t3_plants_solved")) { Show("f2_plant_nothing"); return; }   // the cleaning rota says why
                GameState.Set("f2_got_coin");
                Give("coin", "Coin");
                Show("f2_plant_search");
            });
            Use("f2_flashlight", () =>
            {
                if (Inventory.Has("flashlight")) { Show("f2_searched"); return; }
                Give("flashlight", "Torch");
                Show("f2_flashlight");
            });
            Use("f2_bin", () =>
            {
                var cart = JanitorCart.Find();
                if (cart.Held || (cart.Spawned && cart.RoomId == "f2_closet")) { Show("f2_bin_done"); return; }
                cart.Spawn(rooms.Current, rooms.MarkerPosition("f2_closet", "cart_spawn"));
                Show("f2_bin");
            });

            Use("f2_terminal", () =>
            {
                if (Dark) { Show("f2_terminal_dead"); return; }
                TerminalUI.Instance.OpenTerminal(tutorial, GameState.Has("f2_hacked"), "Floor 3 (Executive Offices)",
                    () =>
                    {
                        GameState.Set("f2_hacked");
                        GameState.Clearance = Mathf.Max(GameState.Clearance, 3);
                        HudUI.Instance.Toast("Clearance: Executive Offices");
                    },
                    () => { if (!tutorialDone) { tutorialDone = true; StartCoroutine(SayWhenFree("f2_terminal_done")); } });
            });

            Use("f2_breaker_panel", BreakerPanel);

            // the vending machine downstairs takes the plant crew's token
            var vending = ScriptedInteractable.Find("vending");
            if (vending != null)
            {
                var floor0 = vending.OnUse;
                vending.OnUse = () =>
                {
                    // the coin from the conference room plant (Floor 0's own coin bought the USB-C cable long ago)
                    if (Inventory.IsEquipped("coin") && GameState.Has("f2_got_coin"))
                    {
                        Inventory.Remove("coin");
                        Give("crisps", "Crisps (salt & vinegar)");
                        Show("f2_vending_coin");
                        return;
                    }
                    floor0?.Invoke();
                };
            }
        }

        private void BreakerPanel()
        {
            if (decisionRunning) return;
            switch (Phase)
            {
                case "cut": StartCoroutine(CutPower()); return;
                case "restore":
                    GameState.Unset("power_out_floor2");
                    Phase = "done";
                    Show("f2_power_on_lie");
                    return;
                case "": break;
                default: Show("f2_panel_idle"); return;
            }
            if (!GameState.Has("f2_plant_found")) { Show("f2_panel_early"); return; }
            if (!GameState.Has("f2_need_login")) { GameState.Set("f2_need_login"); Show("f2_panel_login_needed"); return; }
            if (!Inventory.Has("creds")) { Show("f2_panel_need_login"); return; }
            if (!GameState.Has("f2_hacked") || !tutorialDone) { Show("f2_panel_terminal_first"); return; }
            if (!AllOfficePcs()) { Show("f2_panel_office_first"); return; }
            StartCoroutine(Decision());
        }

        private static bool Dark => GameState.Has("power_out_floor2");

        private static void Clue(string flag, string line)
        {
            bool isNew = !GameState.Has(flag);
            GameState.Set(flag);
            Show(line);
            if (isNew) HudUI.Instance?.Toast("Clue found (KODEX)");
        }

        // ------------------------------------------------------------------ helpers

        private void Pc(string hotspot, string puzzleId, string doneLine, Action onFirstSolve = null)
        {
            var h = ScriptedInteractable.Find(hotspot);
            if (h == null || !puzzles.TryGetValue(puzzleId, out var puzzle)) return;
            h.OnUse = () =>
            {
                if (Dark) { Show("f2_terminal_dead"); return; }
                bool before = GameState.Has(puzzle.rewardFlag);
                TerminalUI.Instance.Open(puzzle, ComputerSkin.EmployeePc, () =>
                {
                    if (before || !GameState.Has(puzzle.rewardFlag)) return;
                    onFirstSolve?.Invoke();
                    Show(doneLine);
                    if (Array.IndexOf(OfficePcs, puzzleId) >= 0 && !AllOfficePcs())
                        DialogueUI.Instance.Show("", DialogueBank.Get("f2_office_progress"));
                });
            };
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

        private bool InRoom(string id) => rooms.Current != null && rooms.Current.roomId == id && !rooms.IsTransitioning;

        private bool Near(string roomId, string marker, float metres)
        {
            var p = PlayerController.Instance;
            return p != null && Vector2.Distance(p.transform.position, rooms.MarkerPosition(roomId, marker)) <= metres * p.transform.localScale.x;
        }
    }
}

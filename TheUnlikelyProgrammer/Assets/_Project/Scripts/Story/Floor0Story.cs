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
    /// Floor 0 (General), the guided tutorial, start to finish:
    /// cart -> Dan's office (mop while Dan teaches Ryker) -> stairs (kidnappers call) -> Dan's PC puzzle ->
    /// call -> USB-C cable (lounge rumour, closet coin, vending machine) -> call + Manual download ->
    /// employee terminal while the receptionist is away -> clearance for Floor 1.
    /// All text lives in Data/Dialogue/floor0.json.
    /// </summary>
    public class Floor0Story : MonoBehaviour
    {
        [SerializeField] private TextAsset dialogue;
        [SerializeField] private TextAsset danPcPuzzle;
        [SerializeField] private TextAsset[] tutorialSteps;

        private const string Floor = "floor0";
        private const string Office = "f0_office";
        private const string Caller = "Unknown number";

        private RoomManager rooms;
        private Npc dan, ryker, receptionist;
        private bool tutorialDone, receptionistAway, catching;
        private PuzzleData[] tutorial;
        private PuzzleData danPc;
        private float caughtCooldown;
        private Coroutine run;
        private bool skipped;

        /// <summary>
        /// Floor 0 was skipped (dev key F10 gives clearance without the Floor 0 terminal hack):
        /// stop this floor's script so it doesn't react to the cart, objectives or the receptionist any more.
        /// </summary>
        private bool CheckSkipped()
        {
            if (skipped) return true;
            if (GameState.Clearance < 1 || GameState.Has("f0_hacked")) return false;
            skipped = true;
            if (run != null) StopCoroutine(run);
            StopAllCoroutines();
            GameState.Set("f0_complete");
            HudUI.Instance?.SetObjective(null);
            HudUI.Instance?.SetHint(null);
            return true;
        }

        public void Configure(TextAsset dialogueJson, TextAsset danPc, TextAsset[] tutorialJson)
        {
            dialogue = dialogueJson;
            danPcPuzzle = danPc;
            tutorialSteps = tutorialJson;
        }

        /// <summary>Called by StoryDirector once the intro has finished.</summary>
        public void Begin()
        {
            DialogueBank.Load(dialogue);
            rooms = RoomManager.Instance;
            dan = Npc.Find("dan");
            ryker = Npc.Find("ryker");
            receptionist = Npc.Find("receptionist");
            danPc = PuzzleData.FromJson(danPcPuzzle.text);
            tutorial = new PuzzleData[tutorialSteps.Length];
            for (int i = 0; i < tutorialSteps.Length; i++) tutorial[i] = PuzzleData.FromJson(tutorialSteps[i].text);
            // lessons already done (a loaded save, or Leet's skipped ones): the terminal step is over
            tutorialDone = tutorial.Length > 0 && System.Array.TrueForAll(tutorial, t => GameState.Has(t.rewardFlag));

            SetupNpcs();
            SetupHotspots();
            run = StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ the floor, in order

        /// <summary>
        /// The floor as a list of steps. Each scripted bit is a beat (StoryBeats) and each player step remembers it's done,
        /// so a resumed game skips straight to where Donnie was. Everything a step changes in the world is either a flag,
        /// an item, or put back by WorldState / Resume().
        /// </summary>
        private IEnumerator Run()
        {
            // already past Floor 0, or a dev skip gave Floor 1 clearance without the hack
            if (GameState.Has("f0_complete") || (GameState.Clearance >= 1 && !GameState.Has("f0_hacked"))) yield break;
            var hud = HudUI.Instance;
            var cart = JanitorCart.Find(); // hidden in the closet until the bin brings it out

            yield return Beat("f0_start", Opening());

            // 1. gear up in the janitor's closet
            if (!Done("f0_gear"))
            {
                hud.SetObjective("Get your gear from the janitor's closet (top left)");
                yield return new WaitUntil(() => cart.Spawned);
                hud.SetObjective("Take hold of your cart");
                hud.SetHint("Q next to the cart to push it. Q again to let go.");
                yield return new WaitUntil(() => cart.EverHeld);
                Mark("f0_gear");
            }

            // 2. bring it to Dan's office
            if (!Done("f0_to_office"))
            {
                hud.SetObjective("Take the cart to Dan's office (east side of the lobby)");
                hud.SetHint("Push the cart through doors with it in hand (E at the door)");
                yield return new WaitUntil(() => cart.RoomId == Office && InRoom(Office));
                hud.SetHint(null);
                Mark("f0_to_office");
            }

            // 3. mop (the mop is on the cart, so Donnie mops while pushing it)
            if (!Done("f0_mop"))
            {
                hud.SetObjective("Mop the spill at the far right of the office");
                hud.SetHint("With the cart in hand, hold F next to the spill");
                var spill = FindSpill("dan_office_spill");
                yield return new WaitUntil(() => spill == null || spill.Clean);
                hud.SetHint(null);
                Mark("f0_mop");
            }

            // 4. floor's clean: Donnie overhears Dan teaching Ryker; then they leave
            yield return Beat("f0_teach", Teaching());

            // 5. the kidnappers call; Dan's computer
            if (!Done("f0_stairs"))
            {
                hud.SetObjective("Head for the stairs (top right of the lobby)");
                yield return new WaitUntil(() => InRoom(Floor) && Near(Floor, "stairs_zone", 2.2f));
                Mark("f0_stairs");
            }
            yield return Beat("f0_stairs_call", Call("stairs_call"));
            if (!GameState.Has(danPc.rewardFlag))
            {
                hud.SetObjective("Use Dan's computer: find the new hire");
                hud.SetHint("Type a query and press Enter. Esc leaves the computer.");
            }
            yield return new WaitUntil(() => GameState.Has(danPc.rewardFlag) && !TerminalUI.Instance.IsOpen);
            hud.SetHint(null);
            yield return Beat("f0_pc_done", Say("pc_done"));

            // 6. call on the way out
            if (!Done("f0_left_office"))
            {
                hud.SetObjective("Leave Dan's office");
                yield return new WaitUntil(() => InRoom(Floor));
                Mark("f0_left_office");
            }
            yield return Beat("f0_leave_call", Call("leave_office_call"));
            GameState.Set("f0_need_usb");
            LoungeTalksAboutCables();
            if (!Inventory.Has("usb"))
            {
                hud.SetObjective(Inventory.Has("coin") ? "Buy a USB-C cable from the vending machine (staff lounge)"
                    : GameState.Has("f0_need_coin") ? "Get a coin from your locker (janitor's closet)" : "Find a USB-C cable");
                if (!GameState.Has("f0_need_coin")) hud.SetHint("Somebody in the staff lounge (west side) might know");
            }
            yield return new WaitUntil(() => Inventory.Has("usb"));
            hud.SetHint(null);

            // 7. call: download Manual
            yield return Beat("f0_cable_call", CableCall());
            if (!GameState.HasManual)
            {
                hud.SetObjective("Install KODEX from your messages");
                hud.SetHint("Tab opens the phone");
            }
            yield return new WaitUntil(() => GameState.HasManual && !PhoneUI.Instance.IsOpen);
            hud.SetHint(null);
            yield return Beat("f0_manual", Say("manual_installed"));

            // 8. the employee terminal, while the receptionist is on her rounds
            StartCoroutine(ReceptionistRounds());
            if (!GameState.Has("f0_hacked"))
            {
                hud.SetObjective("Plug into the employee terminal while the receptionist is away");
                hud.SetHint("USB-C cable in hand (number key), then use the terminal on the front desk");
            }
            yield return new WaitUntil(() => GameState.Has("f0_hacked"));
            hud.SetHint(null);
            hud.SetObjective("Finish KODEX's lessons on the terminal");
            yield return new WaitUntil(() => tutorialDone && !TerminalUI.Instance.IsOpen);

            // 9. Floor 1 unlocked
            GameState.Set("f0_complete");
            yield return Beat("f0_done", Say("tutorial_done"));
            hud.SetObjective("Take the elevator to Floor 1");
        }

        private IEnumerator Opening()
        {
            PhoneData.AddMessage("Betty", DialogueBank.One("msg_betty"));
            yield return Say("start");
        }

        private IEnumerator Teaching()
        {
            yield return new WaitForSeconds(0.4f);
            yield return Say("teach");
            yield return DanAndRykerLeave();
            yield return Say("after_teaching");
        }

        private IEnumerator CableCall()
        {
            yield return Call("cable_call");
            PhoneData.AddMessage(Caller, DialogueBank.One("msg_kidnappers"), "manual");
        }

        private static void LoungeTalksAboutCables()
        {
            Npc.Find("lounge_a")?.SetBarks(DialogueBank.Get("lounge_usb_a"));
            Npc.Find("lounge_b")?.SetBarks(DialogueBank.Get("lounge_usb_b"));
        }

        /// <summary>Safe to autosave: the receptionist isn't marching Donnie away.</summary>
        public bool CanSave => !catching;

        /// <summary>A resumed game (after WorldState put the world back): what the steps done so far left behind.</summary>
        public void Resume()
        {
            tutorialDone = tutorial != null && tutorial.Length > 0 && System.Array.TrueForAll(tutorial, t => GameState.Has(t.rewardFlag));
            if (Done("f0_gear")) JanitorCart.Find()?.MarkEverHeld();
            if (GameState.Has("f0_need_usb")) LoungeTalksAboutCables();
        }

        private static IEnumerator Beat(string id, IEnumerator body) => StoryBeats.Beat(id, body);
        private static bool Done(string id) => StoryBeats.Done(id);
        private static void Mark(string id) => StoryBeats.Mark(id);

        // ------------------------------------------------------------------ pieces

        private IEnumerator DanAndRykerLeave()
        {
            yield return Say("dan_leaves");
            var exit = rooms.MarkerPosition(Office, "npc_exit");
            var mid = rooms.MarkerPosition(Office, "npc_mid");
            int gone = 0;
            if (dan != null)
            {
                dan.Talkable = false;
                dan.WalkPath(new[] { mid, exit }, () => { dan.gameObject.SetActive(false); gone++; });
            }
            else gone++;
            yield return new WaitForSeconds(1.0f);
            if (ryker != null)
            {
                ryker.Talkable = false;
                ryker.WalkPath(new[] { mid, exit }, () => { ryker.gameObject.SetActive(false); gone++; });
            }
            else gone++;
            float t = 0f;
            while (gone < 2 && t < 12f) { t += Time.deltaTime; yield return null; }
            if (dan != null) dan.gameObject.SetActive(false);
            if (ryker != null) ryker.gameObject.SetActive(false);
        }

        private IEnumerator ReceptionistRounds()
        {
            if (receptionist == null) yield break;
            var desk = rooms.MarkerPosition(Floor, "recep_desk");
            var route = new List<Vector3>();
            for (int i = 1; i <= 5; i++) route.Add(rooms.MarkerPosition(Floor, "recep_p" + i));
            var back = new List<Vector3>(route);
            back.Reverse();
            back.RemoveAt(0);
            back.Add(desk);

            while (true)
            {
                receptionistAway = false;
                receptionist.Face(Vector2.up);
                yield return new WaitForSeconds(11f);
                receptionist.Bark(DialogueBank.One("receptionist_leaving"), 2.5f);
                receptionistAway = true;
                bool walking = true;
                receptionist.WalkPath(route, () => walking = false);
                yield return new WaitUntil(() => !walking);
                receptionist.Face(Vector2.up);
                yield return new WaitForSeconds(7f);
                walking = true;
                receptionist.WalkPath(back, () => walking = false);
                // she counts as "back" once she's close to the desk again
                yield return new WaitUntil(() => !walking || Vector2.Distance(receptionist.transform.position, desk) < 3.5f);
                receptionistAway = false;
                yield return new WaitUntil(() => !walking);
            }
        }

        /// <summary>The receptionist can see the terminal: she's at (or nearly back at) her desk.</summary>
        private bool ReceptionistWatching =>
            receptionist != null && receptionist.gameObject.activeSelf && !receptionistAway;

        private void Update()
        {
            if (rooms == null || CheckSkipped()) return;
            // walking up to the terminal while she's at the desk gets Donnie marched off
            if (!GameState.HasManual || tutorialDone || catching || rooms == null) return;
            caughtCooldown -= Time.deltaTime;
            if (caughtCooldown > 0f || TerminalUI.Instance.IsOpen || !InRoom(Floor) || Unlikely.NPC.Guard.Frozen) return;   // not while a text box is up
            if (ReceptionistWatching && Near(Floor, "terminal_zone", 1.3f)) StartCoroutine(Caught());
        }

        private IEnumerator Caught()
        {
            catching = true;
            PlayerController.Frozen = true;
            var p = PlayerController.Instance.transform;
            receptionist.Face(((Vector2)(p.position - receptionist.transform.position)).x < 0 ? Vector2.left : Vector2.right);
            yield return Say("caught");
            // Donnie shuffles away from the desk by himself
            PlayerController.Frozen = false;
            bool walked = false;
            PlayerController.Instance.WalkTo(rooms.MarkerPosition(Floor, "caught"), () => walked = true);
            yield return new WaitUntil(() => walked);
            caughtCooldown = 2f;
            catching = false;
        }

        // ------------------------------------------------------------------ setup

        private void SetupNpcs()
        {
            void Talk(string id, string entry)
            {
                var n = Npc.Find(id);
                if (n != null) n.SetBarks(DialogueBank.Get(entry));
            }
            Talk("lounge_a", "lounge_a");
            Talk("lounge_b", "lounge_b");
            Talk("hall_a", "npc_hall_a");
            Talk("hall_b", "npc_hall_b");
            Talk("hall_c", "npc_hall_c");
            Talk("receptionist", "receptionist");
            if (dan != null) dan.OnTalk = () => dan.Bark(DialogueBank.One("dan_busy"));
            if (ryker != null) ryker.OnTalk = () => ryker.Bark(DialogueBank.One("ryker_busy"));
        }

        private void SetupHotspots()
        {
            var pc = ScriptedInteractable.Find("dan_pc");
            if (pc != null) pc.OnUse = () =>
            {
                if (!GameState.Has("f0_called_once")) Show("pc_before");
                else TerminalUI.Instance.Open(danPc, ComputerSkin.EmployeePc);
            };

            var vending = ScriptedInteractable.Find("vending");
            if (vending != null) vending.OnUse = () =>
            {
                if (Inventory.Has("usb") || GameState.Has("f0_bought_usb")) Show("vending_done");
                else if (!GameState.Has("f0_need_usb")) Show("vending_before");
                else if (!Inventory.Has("coin")) { GameState.Set("f0_need_coin"); Show("vending_no_coin"); HudUI.Instance.SetObjective("Get a coin from your locker (janitor's closet)"); HudUI.Instance.SetHint(null); }
                else if (!Inventory.IsEquipped("coin")) Show("vending_need_equip");
                else
                {
                    Inventory.Remove("coin");
                    Inventory.Add("usb", "USB-C cable");
                    GameState.Set("f0_bought_usb");
                    HudUI.Instance.Toast("Got: USB-C cable");
                    Show("vending_bought");
                }
            };

            var locker = ScriptedInteractable.Find("locker");
            if (locker != null) locker.OnUse = () =>
            {
                if (GameState.Has("f0_need_usb") && !Inventory.Has("coin") && !Inventory.Has("usb") && !GameState.Has("f0_bought_usb"))
                {
                    Inventory.Add("coin", "Coin");
                    HudUI.Instance.Toast("Got: Coin");
                    Show("locker_coin");
                    HudUI.Instance.SetObjective("Buy a USB-C cable from the vending machine (staff lounge)");
                }
                else Show("locker_default");
            };

            var terminal = ScriptedInteractable.Find("employee_terminal");
            if (terminal != null) terminal.OnUse = () =>
            {
                if (tutorialDone) { OpenTerminal(); return; }
                if (!Inventory.Has("usb")) { Show("terminal_before"); return; }
                if (!GameState.HasManual) { Show("terminal_need_manual"); return; }
                if (ReceptionistWatching) { if (!catching) StartCoroutine(Caught()); return; }
                if (!Inventory.IsEquipped("usb")) { Show("terminal_need_cable"); return; }
                OpenTerminal();
            };

            var bin = ScriptedInteractable.Find("closet_bin");
            if (bin != null) bin.OnUse = () =>
            {
                var cart = JanitorCart.Find();
                if (cart.Spawned) { Show("closet_bin_done"); return; }
                cart.Spawn(rooms.Current, rooms.MarkerPosition("f0_closet", "cart_spawn"));
                HudUI.Instance.Toast("Your cart is out");
                Show("cart_spawned");
            };

            var entrance = ScriptedInteractable.Find("entrance");
            if (entrance != null) entrance.OnUse = () =>
                Show(GameState.Has("f0_called_once") ? "entrance_after" : "entrance_before");
        }

        private void OpenTerminal()
        {
            TerminalUI.Instance.OpenTerminal(tutorial, GameState.Has("f0_hacked"), "Floor 1 (Administration)",
                () =>
                {
                    GameState.Set("f0_hacked");
                    GameState.Clearance = Mathf.Max(GameState.Clearance, 1);
                    HudUI.Instance.Toast("Clearance: Floor 1 (Administration)");
                },
                () => tutorialDone = true);
        }

        // ------------------------------------------------------------------ helpers

        private static void Show(string id) => DialogueUI.Instance.Show("", DialogueBank.Get(id));

        private static IEnumerator Say(string id)
        {
            bool done = false;
            DialogueUI.Instance.Show("", DialogueBank.Get(id), () => done = true);
            yield return new WaitUntil(() => done);
        }

        private IEnumerator Call(string id)
        {
            // wait for a calm moment (no computer, cutscene or room change)
            yield return new WaitUntil(() => !UiKit.AnyModalOpen && !rooms.IsTransitioning && !DialogueUI.Instance.IsOpen);
            GameState.Set("f0_called_once");
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

        private static Spill FindSpill(string id)
        {
            foreach (var s in FindObjectsByType<Spill>(FindObjectsInactive.Include))
                if (s.Id == id) return s;
            return null;
        }
    }
}

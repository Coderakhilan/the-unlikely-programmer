using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.SQL;
using Unlikely.Puzzles;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>
    /// Plays the Executive floor from the Floor 3 dev jump: Liam says no; coins, coffee, the splash; Liam catches Donnie once,
    /// then gets locked in the small meeting room; the terminal; the pen drive printed on Floor 2; the three linked joins;
    /// the revoked keycard and the blackout; the CEO's keycard and laptop; the phone update and MagiCorp shares;
    /// the code, the keypad and the truth-or-lie call at the server room door.
    /// </summary>
    public class Floor3PlayTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        private static IEnumerator Start()
        {
            GameState.ResetAll();
            GameState.Set("intro_seen");
            Inventory.Clear();
            PhoneData.Clear();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            Time.timeScale = 4f;
            Floor3Story.Instance.DevSkipToFloor3();
            yield return Wait(() => HudUI.Instance.Objective.Contains("Liam"), "arrival call", 60f);
            yield return Settle();
            foreach (var g in UnityEngine.Object.FindObjectsByType<Guard>(FindObjectsInactive.Include)) g.enabled = false;
        }

        [UnityTest]
        public IEnumerator Floor3_Liam_Blackout_ServerRoom()
        {
            yield return Start();
            var story = Floor3Story.Instance;
            var p = PlayerController.Instance;
            var liam = Npc.Find("f3_liam");

            // Liam says no
            yield return Go("floor3", "liam_b");
            liam.OnTalk();
            yield return Settle();
            Assert.IsTrue(GameState.Has("f3_liam_met"));
            yield return Wait(() => HudUI.Instance.Objective.Contains("coffee"), "coffee objective");

            // three coins, one coffee
            yield return Use("f3_plant_coin_1");
            yield return Use("f3_plant_coin_2");
            yield return Use("f3_plant_coin_2");
            Assert.AreEqual("Coins (2)", Inventory.Items.First(i => i.id == "coins").name, "the same plant twice is just soil");
            yield return Go("f3_lounge", "door0");
            yield return Use("f3_vending");
            Assert.IsFalse(Inventory.Has("coffee"), "two coins aren't enough");
            yield return Use("f3_plant_coin_3");
            yield return Use("f3_vending");
            Assert.IsTrue(Inventory.Has("coffee"));
            Assert.IsFalse(Inventory.Has("coins"));

            // the splash; Liam catches him the first time
            yield return Go("floor3", "liam_b");
            Equip("coffee");
            liam.OnTalk();
            yield return Settle();
            Assert.AreEqual("liam", story.Phase);
            Assert.IsTrue(liam.GetComponent<Hunter>().enabled);
            Assert.IsNotNull(Elevator.Blocked(), "no lift with Liam behind him");
            var board = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Exclude).First(d => d.TargetRoomId == "f3_board");
            board.Interact(p);
            yield return Settle();
            Assert.AreEqual("floor3", RoomManager.Instance.Current.roomId, "no running into a room with no way out");
            yield return Wait(() => { p.transform.position = liam.transform.position; return GameOverUI.Instance.IsOpen; }, "Liam catches Donnie", 20f);
            GameOverUI.Instance.Retry();
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "f3_lounge", "retry in the lounge");
            Assert.AreEqual("", story.Phase);
            Assert.IsTrue(Inventory.Has("coffee"), "a fresh coffee");

            // again: this time into the small meeting room and the locker
            yield return Go("floor3", "liam_b");
            Equip("coffee");
            liam.OnTalk();
            yield return Settle();
            Assert.AreEqual("liam", story.Phase);
            RoomManager.Instance.GoTo("f3_round", "door0");
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "f3_round", "into the meeting room");
            var locker = UnityEngine.Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Exclude).First(h => h.Id == "f3_locker");
            locker.Interact(p);
            Assert.IsTrue(HideSpot.IsHidden);
            yield return Wait(() => story.LiamSearching, "Liam comes in and searches the far end", 30f);
            Assert.IsFalse(GameOverUI.Instance.IsOpen);
            locker.ComeOut();
            yield return Go("floor3", "f3_round_door0");
            yield return Wait(() => story.Phase == "liam_inside", "Donnie slips out");
            var roundDoor = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Exclude).First(d => d.TargetRoomId == "f3_round");
            Assert.AreEqual("Lock the door", roundDoor.Prompt);
            roundDoor.Interact(p);
            yield return Settle();
            Assert.IsTrue(GameState.Has("f3_liam_locked"));
            Assert.IsTrue(Inventory.Has("keycard_liam"));
            roundDoor.Interact(p);
            yield return Settle();
            Assert.AreEqual("floor3", RoomManager.Instance.Current.roomId, "he stays locked in");

            // the terminal: Server room clearance
            for (int i = 1; i <= 3; i++) GameState.Set($"f3_tut_{i}_done");
            yield return Go("floor3", "elevator");
            ScriptedInteractable.Find("f3_terminal").OnUse();
            yield return Wait(() => GameState.Has("f3_hacked"), "hack");
            yield return Wait(() => { TerminalUI.Instance.Close(); return !TerminalUI.Instance.IsOpen; }, "leave terminal");
            yield return Settle();
            Assert.AreEqual(4, GameState.Clearance);

            // the pen drive, printed on Floor 2's printer
            yield return Go("f3_exec_a", "door0");
            yield return Use("f3_drawer");
            Assert.IsTrue(Inventory.Has("pen_drive"));
            // Floor 2 is a normal working day again: its story doesn't restart, people are back, Ryker's still locked in
            yield return Go("floor2", "elevator");
            yield return Settle();
            Assert.AreEqual("done", Floor2Story.Instance.Phase);
            Assert.IsFalse(PhoneUI.Instance.IsRinging, "no Floor 2 arrival call again");
            Assert.IsTrue(Npc.Find("f2_recep").gameObject.activeSelf, "the receptionist is back");
            Assert.IsFalse(Npc.Find("f2_hr").GetComponent<Hunter>().enabled, "Head of HR isn't hunting");
            var iv = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Exclude).First(d => d.TargetRoomId == "f2_interview");
            iv.Interact(p);
            yield return Settle();
            Assert.AreEqual("floor2", RoomManager.Instance.Current.roomId, "Ryker's still locked in the interview room");
            yield return Go("f2_printer", "door0");
            yield return Use("f2_printer");
            Assert.IsTrue(GameState.Has("f3_keymap"));
            Assert.IsFalse(Inventory.Has("pen_drive"));

            // the three joins, each through the right link
            Assert.AreEqual("open_r", story.LinkOf("board_side"), "starts wired wrong");
            yield return Go("f3_exec_a", "door0");
            yield return Station(0);
            Point("board_side", "board_long");
            Point("open_l", "board_long");
            Point("exec_b", "open_r");
            LinkStationUI.Instance.Close();
            yield return Settle();
            yield return Go("floor3", "elevator");
            yield return Solve("f3_pc_board_side", "board_side", "f3_p1_rota", "SELECT s.staff_id, s.name FROM rota r JOIN staff s ON r.holder = s.staff_id WHERE r.rota_day = 'Tuesday'");
            yield return Wait(() => GameState.Has("f3_ready_p2"), "the leave puzzle opens up");
            yield return Solve("f3_pc_open_l", "open_l", "f3_p2_cover", "SELECT s.staff_id, s.name FROM leave l JOIN staff s ON l.cover = s.staff_id WHERE l.on_leave = 118 AND l.leave_day = 'Tuesday'");
            yield return Go("f3_exec_b", "door0");
            yield return Solve("f3_pc_exec_b", "exec_b", "f3_p3_code", "SELECT c.position, c.cipher FROM code_parts c JOIN badges b ON c.card = b.badge_no WHERE b.owner = 205 AND b.status = 'active' ORDER BY c.position");
            Assert.IsTrue(Inventory.Has("code_enc"));

            // decrypt with Liam's card: revoked, the CEO finds out, the lights go
            yield return Go("f3_exec_a", "door0");
            yield return Station(1);
            yield return Wait(() => story.Phase == "dark", "the blackout", 90f);
            yield return Settle();
            Assert.AreEqual("f3_exec_a", RoomManager.Instance.Current.roomId, "back with Donnie after the CEO's shot");
            Assert.IsTrue(GameState.Has("power_out_floor3"));
            Assert.IsFalse(Inventory.Has("keycard_liam"), "revoked");
            Assert.IsTrue(Npc.Find("f3_ceo_hunt").GetComponent<Hunter>().enabled);
            Assert.IsFalse(Npc.Find("f3_guard_a").gameObject.activeSelf, "the day staff are gone");
            yield return Use("f3_torch");
            Assert.IsTrue(Inventory.Has("flashlight"));

            // the CEO catches Donnie: retry in the safe office
            yield return Go("floor3", "f3_exec_a_door0");
            Assert.IsNotNull(Elevator.Blocked(), "lift dead, stairs locked");
            var ceo = Npc.Find("f3_ceo_hunt");
            yield return Wait(() => { p.transform.position = ceo.transform.position + (Vector3)(ceo.Facing * 0.3f); return GameOverUI.Instance.IsOpen; }, "the CEO catches Donnie", 30f);
            GameOverUI.Instance.Retry();
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "f3_exec_a", "retry in office A");

            // hiding in the CEO's office with the CEO right outside its door: stepping out again must not be an instant catch
            yield return Go("f3_ceo", "door0");
            ceo.transform.position = RoomManager.Instance.MarkerPosition("floor3", "f3_ceo_door0");   // he froze at the door
            yield return Go("floor3", "f3_ceo_door0");
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { Assert.IsFalse(GameOverUI.Instance.IsOpen, "caught the moment he stepped out"); yield return null; }
            Assert.Greater(Vector2.Distance(ceo.transform.position, p.transform.position), 2f, "the CEO went back to his rounds");
            yield return Go("f3_exec_a", "door0");
            foreach (var id in new[] { "f3_ceo_hunt", "f3_sec_1", "f3_sec_2", "f3_sec_3" }) Npc.Find(id).GetComponent<Hunter>().enabled = false;

            // dark rooms are refused, the CEO's office is open
            var boardDoor = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Include).First(d => d.TargetRoomId == "f3_board");
            yield return Go("floor3", "f3_board_door0");
            boardDoor.Interact(p);
            yield return Settle();
            Assert.AreEqual("floor3", RoomManager.Instance.Current.roomId);
            yield return Go("f3_ceo", "door0");
            yield return Use("f3_ceo_desk");
            Assert.IsTrue(Inventory.Has("keycard_ceo"));
            yield return Use("f3_ceo_pc");
            Assert.IsTrue(GameState.Has("f3_mails_read"));
            Assert.IsTrue(PhoneUI.KnowsAboutMagiCorp, "lie branch: Ryker's login + the mail");

            // decrypt: the phone updates first (Calls works, Bank app), then the code
            yield return Go("f3_exec_a", "door0");
            yield return Station(1);
            yield return Wait(() => GameState.Has("bank_app") && !PhoneUI.Instance.IsUpdating, "the phone update", 60f);
            yield return Settle();
            Assert.IsFalse(GameState.Has("f3_code"), "the update interrupted it");
            PhoneUI.Instance.Buy(1);
            Assert.IsTrue(GameState.Has("f3_stocks"));
            yield return Settle();
            yield return Station(1);
            yield return Settle();
            Assert.IsTrue(GameState.Has("f3_code"));
            Assert.IsTrue(Inventory.Has("code_server"));

            // the server room: keypad, then the call
            yield return Go("floor3", "server_door");
            var server = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Exclude).First(d => d.TargetRoomId == "server");
            server.Interact(p);
            yield return Wait(() => KeypadUI.Instance.IsOpen, "keypad");
            Assert.AreEqual("Use keypad", server.Prompt);
            foreach (char c in Floor3Story.ServerCode) KeypadUI.Instance.Press(c.ToString());   // the clickable keys; four digits try it
            yield return Wait(() => PhoneUI.Instance.IsRinging || DialogueUI.Instance.IsChoosing, "the call", 30f);
            PhoneUI.Instance.Answer();
            yield return Wait(() => DialogueUI.Instance.IsChoosing || (DialogueUI.Instance.IsOpen && Advance()), "choice", 60f);
            DialogueUI.Instance.Choose(1);
            yield return Wait(() => story.Phase == "done", "into the server room", 60f);
            yield return Settle();
            Assert.IsTrue(GameState.Has("d3_lie"));
            Assert.IsTrue(GameState.Has("f3_complete"));
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "server", "server room");
        }

        // ---- helpers

        /// <summary>Use the link station; option 1 = press DECRYPT, 0 = just leave it open.</summary>
        private static IEnumerator Station(int option)
        {
            yield return Settle();
            ScriptedInteractable.Find("f3_link").OnUse();
            yield return Wait(() => LinkStationUI.Instance.IsOpen || (DialogueUI.Instance.IsOpen && Advance()), "link station screen", 30f);
            if (option == 1) LinkStationUI.Instance.Decrypt();
        }

        /// <summary>On the link station screen: select a computer and step its link until it reads from the wanted one.</summary>
        private static void Point(string machine, string target)
        {
            var story = Floor3Story.Instance;
            LinkStationUI.Instance.Select(Array.IndexOf(Floor3Story.Machines, machine));
            for (int i = 0; i < 6 && story.LinkOf(machine) != target; i++) LinkStationUI.Instance.Cycle(1);
            Assert.AreEqual(target, story.LinkOf(machine), machine + " -> " + target);
        }

        /// <summary>Open a green computer, check it sees exactly its table and the linked one, and solve it with the real query.</summary>
        private static IEnumerator Solve(string hotspot, string machine, string puzzleId, string sql)
        {
            yield return Settle();
            var story = Floor3Story.Instance;
            var view = story.ViewFor(story.Puzzle(puzzleId), machine);
            Assert.AreEqual(2, view.tables.Length, machine + " sees its own table and the linked one");
            Assert.IsTrue(PuzzleChecker.Check(view, SqlEngine.Execute(view.BuildDatabase(), sql), out string fb), puzzleId + ": " + fb);
            ScriptedInteractable.Find(hotspot).OnUse();
            Assert.IsTrue(TerminalUI.Instance.IsOpen, hotspot + " opens");
            GameState.Set(puzzleId + "_solved");
            TerminalUI.Instance.Close();
            yield return Settle();
        }

        private static void Equip(string id)
        {
            for (int i = 0; i < Inventory.Items.Count; i++) if (Inventory.Items[i].id == id) Inventory.Equip(i);
        }

        private static bool Advance() { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); return false; }

        private static IEnumerator Use(string hotspot)
        {
            yield return Settle();
            ScriptedInteractable.Find(hotspot).OnUse();
            yield return Settle();
        }

        private static IEnumerator Go(string room, string spawn)
        {
            yield return Settle();
            RoomManager.Instance.GoTo(room, spawn);
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == room, "enter " + room);
            yield return null;
        }

        private static IEnumerator Settle()
        {
            for (int quiet = 0, n = 0; quiet < 10 && n < 3000; n++)
            {
                bool busy = false;
                if (PhoneUI.Instance.IsRinging) { PhoneUI.Instance.Answer(); busy = true; }
                if (DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); busy = true; }
                if (PhoneUI.Instance.IsUpdating) busy = true;
                quiet = busy ? 0 : quiet + 1;
                yield return null;
            }
        }

        private static IEnumerator Wait(Func<bool> condition, string what, float seconds = 30f)
        {
            float t = 0f;
            while (!condition())
            {
                if (DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                if (PhoneUI.Instance.IsRinging) PhoneUI.Instance.Answer();
                t += Time.unscaledDeltaTime;
                if (t > seconds) Assert.Fail("Timed out waiting for: " + what + "   (objective: " + HudUI.Instance.Objective + ", phase: " + Floor3Story.Instance.Phase + ")");
                yield return null;
            }
        }
    }
}

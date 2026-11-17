using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>
    /// Plays Human Resources: Head of HR's game over, the four office computers, the five clues (including crisps
    /// from the Floor 0 vending machine), the duty login, the terminal, then each branch of the call:
    /// the lie (blackout, Ryker lured into the interview room and locked in) and the truth (security, to the elevator).
    /// </summary>
    public class Floor2PlayTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        private static IEnumerator Start()
        {
            GameState.ResetAll();
            GameState.Set("intro_seen");
            GameState.Set("f0_complete");
            GameState.Set("f1_complete");
            GameState.Set("has_manual");
            GameState.Clearance = 2;
            Inventory.Clear();
            Inventory.Add("usb", "USB-C cable");
            PhoneData.Clear();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            Time.timeScale = 4f;
            yield return Go("floor2", "elevator");
            yield return Wait(() => HudUI.Instance.Objective.Contains("2B"), "arrival call");
            yield return Settle();
        }

        [UnityTest]
        public IEnumerator Floor2_Lie_RykerLockedIn()
        {
            yield return Start();
            var p = PlayerController.Instance;

            // Head of HR: seen without the cart and caught = game over; retry puts Donnie back by the elevator
            var hr = Npc.Find("f2_hr").GetComponent<Hunter>();
            yield return Wait(() => hr.enabled, "HR starts his rounds after the call");
            yield return Wait(() => { p.transform.position = hr.transform.position + (Vector3)(hr.GetComponent<Npc>().Facing * 0.3f); return GameOverUI.Instance.IsOpen; }, "HR catches Donnie", 20f);
            GameOverUI.Instance.Retry();
            yield return Wait(() => RoomManager.Instance.Current.roomId == "floor2" && !RoomManager.Instance.IsTransitioning, "retry");
            hr.enabled = false;
            foreach (var g in UnityEngine.Object.FindObjectsByType<Guard>(FindObjectsInactive.Include)) g.enabled = false;

            // the HR open office: four computers -> Plant Room 2B is switched from the breaker room
            yield return Go("f2_office", "door0");
            foreach (var pc in new[] { ("f2_pc_t1", "f2_t1_northgate"), ("f2_pc_t2", "f2_t2_remote"), ("f2_pc_t3", "f2_t3_plants"), ("f2_pc_t4", "f2_t4_locks") })
                yield return SolvePc(pc.Item1, pc.Item2);
            yield return Wait(() => HudUI.Instance.Objective.Contains("breaker"), "the reveal and the call");
            yield return Settle();

            // the panel wants a login
            yield return Go("f2_breaker", "door0");
            yield return Use("f2_breaker_panel");
            Assert.IsTrue(GameState.Has("f2_need_login"));
            yield return Wait(() => HudUI.Instance.Objective.Contains("reception"), "login objective");

            // the clues
            yield return Go("f2_printer", "door0");
            yield return Use("f2_printer");
            yield return Go("f2_conference", "door0");
            yield return Use("f2_projector");
            yield return Use("f2_plant");
            Assert.IsTrue(Inventory.Has("coin"), "the cleaning rota said there's a coin in this plant");
            yield return Go("floor2", "elevator");
            var cand = Npc.Find("f2_candidate");
            cand.Interact(p);
            yield return Settle();
            Assert.IsTrue(GameState.Has("f2_need_crisps"));
            yield return Go("f0_lounge", "door0");
            Equip("coin");
            yield return Use("vending");
            Assert.IsTrue(Inventory.Has("crisps"), "the token buys crisps downstairs");
            yield return Go("floor2", "elevator");
            Equip("crisps");
            cand.Interact(p);
            yield return Settle();
            Assert.IsTrue(GameState.Has("f2_clue_candidate"));
            yield return Go("f2_interview", "door0");
            yield return SolvePc("f2_pc_iv", "f2_iv_restructure");
            Assert.IsTrue(Inventory.Has("restructure"), "leverage");
            foreach (var c in new[] { "f2_clue_printer", "f2_clue_video", "f2_clue_candidate", "f2_clue_interview" })
                Assert.IsTrue(GameState.Has(c), c);

            // reception: the duty login
            yield return Go("floor2", "recep_shoo");
            yield return SolvePc("f2_pc_recep", "f2_reception_logins");
            Assert.IsTrue(Inventory.Has("creds"));

            // the panel says terminal first; the terminal gives Executive clearance
            yield return Go("f2_breaker", "door0");
            yield return Use("f2_breaker_panel");
            Assert.IsFalse(PhoneUI.Instance.IsRinging);
            yield return Terminal();
            Assert.AreEqual(3, GameState.Clearance);
            Assert.IsNotNull(ElevatorUI.StoryBlock(3), "Executive floor waits until this floor's job is done");

            // the call: lie
            yield return Go("f2_breaker", "door0");
            ScriptedInteractable.Find("f2_breaker_panel").OnUse();
            yield return Choose(1);
            Assert.IsTrue(GameState.Has("d2_lie"));
            yield return Wait(() => Floor2Story.Instance.Phase == "cut", "Ryker comes up the stairs", 60f);
            yield return Settle();
            Assert.AreEqual("f2_breaker", RoomManager.Instance.Current.roomId, "back with Donnie after the shot of the stairs");
            Assert.IsFalse(Npc.Find("f2_recep").gameObject.activeSelf, "everyone ran from the 'fire'");

            // stepping out before killing the lights: Ryker is waiting
            yield return Go("floor2", "f2_breaker_door0");
            yield return Wait(() => GameOverUI.Instance.IsOpen, "Ryker ambushes Donnie", 20f);
            GameOverUI.Instance.Retry();
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "f2_breaker", "retry in the breaker room");
            Assert.AreEqual("cut", Floor2Story.Instance.Phase);
            yield return Go("f2_breaker", "door0");
            Assert.AreEqual("cut", Floor2Story.Instance.Phase);
            Assert.IsNotNull(Elevator.Blocked(), "no elevator during the blackout");

            // lights out
            yield return Use("f2_breaker_panel");
            Assert.IsTrue(GameState.Has("power_out_floor2"));
            yield return null;
            Assert.IsTrue(Darkness.IsDark);
            yield return Use("f2_flashlight");
            Assert.IsTrue(Inventory.Has("flashlight"));
            Assert.IsTrue(HideSpot.Enabled);

            // hide in a locker, out again
            yield return Go("floor2", "f2_breaker_door0");
            var locker = UnityEngine.Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Exclude).First(h => h.Type == HideSpot.Kind.Locker);
            locker.Interact(p);
            Assert.IsTrue(HideSpot.IsHidden);
            locker.ComeOut();
            Assert.IsFalse(HideSpot.IsHidden);

            // Ryker's beat never goes near the interview room, and a flash he can't see does nothing
            var ryker = Npc.Find("ryker_f2");
            var rh = ryker.GetComponent<Hunter>();
            Vector3 door = RoomManager.Instance.MarkerPosition("floor2", "f2_interview_door0");
            Assert.IsFalse(Hunter.OnScreen(ryker.transform.position), "Ryker starts out of sight");
            Darkness.RaiseFlash(door);
            Assert.IsFalse(rh.Investigating, "too far away to see the flash");

            // hide in a lobby plant near the interview room; Ryker wanders close; flash at the door: he goes in to look
            var plant = UnityEngine.Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Exclude).First(h => h.Id == "plant3");
            p.transform.position = plant.transform.position;
            plant.Interact(p);
            Assert.IsTrue(HideSpot.IsHidden);
            rh.ResetTo(RoomManager.Instance.MarkerPosition("floor2", "hr_start"));
            yield return null;
            yield return null;
            float flashClock = 0f;
            yield return Wait(() =>
            {
                if (GameOverUI.Instance.IsOpen) Assert.Fail("caught while hiding");
                flashClock -= Time.deltaTime;
                if (flashClock <= 0f && ryker.gameObject.activeSelf) { Darkness.RaiseFlash(door); flashClock = 1f; }
                return Floor2Story.Instance.RykerInside;
            }, "Ryker follows the torch into the interview room", 60f);
            Assert.IsFalse(ryker.gameObject.activeSelf);
            plant.ComeOut();
            yield return Settle();

            // lock him in
            var ivDoor = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Exclude).First(d => d.TargetRoomId == "f2_interview");
            Assert.AreEqual("Lock the door (code)", ivDoor.Prompt);
            Assert.IsTrue(Inventory.Has("code_iv"), "the lock code from the open office");
            ivDoor.Interact(p);
            yield return Settle();
            Assert.IsTrue(GameState.Has("f2_ryker_trapped"));
            Assert.IsTrue(GameState.Has("f2_magicorp"), "Ryker lets the MagiCorp login slip");
            Assert.IsTrue(GameState.Has("f2_deal"));

            // he stays locked in: the door just gets a word from Ryker
            ivDoor.Interact(p);
            yield return Settle();
            Assert.AreNotEqual("f2_interview", RoomManager.Instance.Current.roomId);

            // lights back on, up to Floor 3
            yield return Go("f2_breaker", "door0");
            yield return Use("f2_breaker_panel");
            Assert.IsFalse(GameState.Has("power_out_floor2"));
            yield return Wait(() => HudUI.Instance.Objective.Contains("Floor 3"), "next floor");
            Assert.IsNull(ElevatorUI.StoryBlock(3));
        }

        [UnityTest]
        public IEnumerator Floor2_Truth_DodgeSecurity()
        {
            yield return Start();
            var p = PlayerController.Instance;
            Npc.Find("f2_hr").GetComponent<Hunter>().enabled = false;
            foreach (var g in UnityEngine.Object.FindObjectsByType<Guard>(FindObjectsInactive.Include)) g.enabled = false;

            foreach (var id in new[] { "f2_t1_northgate", "f2_t2_remote", "f2_t3_plants", "f2_t4_locks" }) GameState.Set(id + "_solved");
            yield return Wait(() => HudUI.Instance.Objective.Contains("breaker"), "the reveal");
            yield return Settle();
            yield return Go("f2_breaker", "door0");
            yield return Use("f2_breaker_panel");
            Inventory.Add("creds", "Duty login");
            yield return Terminal();

            yield return Go("f2_breaker", "door0");
            ScriptedInteractable.Find("f2_breaker_panel").OnUse();
            yield return Choose(0);
            Assert.IsTrue(GameState.Has("d2_truth"));
            Assert.AreEqual("security", Floor2Story.Instance.Phase);
            Assert.IsTrue(GameState.Has("power_out_floor1"), "the whole building is dark");

            // security on their rounds; walking into one is a game over, and retry puts Donnie back by the breaker room
            yield return Go("floor2", "f2_breaker_door0");
            var sec = Npc.Find("f2_sec_1").GetComponent<Hunter>();
            Assert.IsTrue(sec.enabled);
            yield return Wait(() => { p.transform.position = sec.transform.position; return GameOverUI.Instance.IsOpen; }, "security catches Donnie", 20f);
            GameOverUI.Instance.Retry();
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "f2_breaker", "retry: back in the breaker room");
            yield return Go("floor2", "recep_shoo");   // the guard by the stairs-side lobby is on screen, too far to see Donnie

            // a torch flick sends a guard off to look
            Darkness.RaiseFlash(RoomManager.Instance.MarkerPosition("floor2", "hr_start"));
            Assert.IsTrue(sec.Investigating);

            // reach the elevator: the deal, the lights come back
            foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" }) Npc.Find(id).GetComponent<Hunter>().enabled = false;
            p.transform.position = RoomManager.Instance.MarkerPosition("floor2", "elevator");
            foreach (var id in new[] { "f2_sec_1", "f2_sec_2", "f2_sec_3" }) Assert.IsTrue(Npc.Find(id).gameObject.activeSelf, id + " still around for the call");
            yield return Wait(() => Floor2Story.Instance.Phase == "done", "the deal and security standing down", 90f);
            yield return Wait(() => !Npc.Find("f2_sec_1").gameObject.activeSelf, "security leave by the stairs", 60f);
            Assert.IsFalse(GameState.Has("power_out_floor2"));
            Assert.IsNull(Elevator.Blocked());
            yield return Wait(() => HudUI.Instance.Objective.Contains("Floor 3"), "next floor");
        }

        [UnityTest]
        public IEnumerator Floor2_DevSkip_GoesStraightToTheCall()
        {
            GameState.ResetAll();
            GameState.Set("intro_seen");
            Inventory.Clear();
            PhoneData.Clear();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            Time.timeScale = 4f;
            yield return Settle();
            Floor2Story.Instance.DevSkipToDecision();
            yield return Choose(1);
            Assert.IsTrue(GameState.Has("d2_lie"));
            yield return Wait(() => Floor2Story.Instance.Phase == "cut", "Ryker's entrance", 60f);
            Assert.AreEqual("f2_breaker", RoomManager.Instance.Current.roomId);
            Assert.IsFalse(Inventory.Has("flashlight"), "the torch is still on its table");
        }

        // ---- helpers

        private static IEnumerator Terminal()
        {
            for (int i = 1; i <= 5; i++) GameState.Set($"f2_tut_{i}_done");
            yield return Go("floor2", "f2_breaker_door0");
            ScriptedInteractable.Find("f2_terminal").OnUse();
            yield return Wait(() => GameState.Has("f2_hacked"), "hack", 30f);
            yield return Wait(() => { TerminalUI.Instance.Close(); return !TerminalUI.Instance.IsOpen; }, "leave terminal", 30f);
            yield return Settle();
        }

        private static IEnumerator Choose(int option)
        {
            yield return Wait(() => PhoneUI.Instance.IsRinging || (DialogueUI.Instance.IsOpen && Advance()), "decision call", 60f);
            PhoneUI.Instance.Answer();
            yield return Wait(() => DialogueUI.Instance.IsChoosing || (DialogueUI.Instance.IsOpen && Advance()), "choice", 60f);
            DialogueUI.Instance.Choose(option);
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

        private static IEnumerator SolvePc(string hotspot, string puzzleId)
        {
            yield return Settle();
            ScriptedInteractable.Find(hotspot).OnUse();
            Assert.IsTrue(TerminalUI.Instance.IsOpen, hotspot + " opens");
            GameState.Set(puzzleId + "_solved");
            TerminalUI.Instance.Close();
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
                if (PhoneUI.Instance.IsRinging && what != "decision call") PhoneUI.Instance.Answer();
                t += Time.unscaledDeltaTime;
                if (t > seconds) Assert.Fail("Timed out waiting for: " + what + "   (objective: " + HudUI.Instance.Objective + ")");
                yield return null;
            }
        }
    }
}

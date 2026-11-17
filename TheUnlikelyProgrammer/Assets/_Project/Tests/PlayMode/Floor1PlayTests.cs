using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>
    /// Plays Administration start to finish through its lock chain (keys, keypads, info slips), the patrol,
    /// the tape-recorder gag, the terminal and the truth-or-lie call. Any error logged fails the test.
    /// </summary>
    public class Floor1PlayTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator Floor1_CanBeFinished()
        {
            GameState.ResetAll();
            GameState.Set("intro_seen");
            GameState.Set("f0_complete");
            GameState.Set("has_manual");
            GameState.Clearance = 1;
            Inventory.Clear();
            PhoneData.Clear();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            Time.timeScale = 4f;

            // arrive: the kidnappers' call sets the goal
            yield return Go("floor1", "elevator");
            yield return Wait(() => HudUI.Instance.Objective.Contains("power"), "arrival call");
            yield return Settle();

            // Floor 0 was skipped: taking the cart here must not restart Floor 0's objectives
            yield return Go("f1_closet", "door0");
            yield return Use("f1_bin");
            JanitorCart.Find().PickUp();
            yield return new WaitForSeconds(0.5f);
            StringAssert.DoesNotContain("Dan", HudUI.Instance.Objective);
            StringAssert.Contains("power", HudUI.Instance.Objective);
            JanitorCart.Find().Drop();

            // patrols: spotted without the cart -> thrown out into the corridor
            yield return Go("f1_office_sw", "door0");
            var guard = Npc.Find("f1_guard_sw").GetComponent<Guard>();
            var p = PlayerController.Instance;
            yield return Wait(() => guard.InSight() || PutInFront(guard, p), "guard looks at Donnie");
            yield return Wait(() => RoomManager.Instance.Current.roomId == "floor1" && !RoomManager.Instance.IsTransitioning, "thrown out", 10f);

            // with the cart beside him he's allowed in...
            yield return Go("f1_office_sw", "door0");
            var cart = JanitorCart.Find();
            cart.Spawn(RoomManager.Instance.Current, p.transform.position);
            float t0 = Time.time;
            yield return Wait(() => { PutInFront(guard, p); cart.transform.position = p.transform.position; return Time.time - t0 > 3f; }, "stay with cart", 20f);
            Assert.AreEqual("f1_office_sw", RoomManager.Instance.Current.roomId, "allowed in with the cart");

            // ...but reaching for the computer while they're looking gets refused
            PutInFront(guard, p);
            yield return null;
            Assert.IsTrue(guard.InSight());
            ScriptedInteractable.Find("f1_pc_sw").Interact(p);
            Assert.IsFalse(TerminalUI.Instance.IsOpen, "refused while watched");
            yield return Wait(() => !p.IsAutoWalking, "steps back", 5f);

            // once a computer is open he's safe, even in plain sight
            ScriptedInteractable.Find("f1_pc_sw").OnUse();
            Assert.IsTrue(TerminalUI.Instance.IsOpen);
            cart.transform.position += Vector3.right * 50f; // no cart nearby either
            t0 = Time.time;
            yield return Wait(() => { PutInFront(guard, p); return Time.time - t0 > 3f; }, "safe at computer", 20f);
            Assert.IsTrue(TerminalUI.Instance.IsOpen, "still at the computer");
            Assert.AreEqual("f1_office_sw", RoomManager.Instance.Current.roomId);
            TerminalUI.Instance.Close();
            foreach (var g in UnityEngine.Object.FindObjectsByType<Guard>(FindObjectsInactive.Include)) g.enabled = false; // keep the rest deterministic

            // SW: drawer -> Office A key; PC -> the meeting room is booked
            yield return Use("f1_sw_drawer");
            Assert.IsTrue(Inventory.Has("key_f1_a"));
            yield return SolvePc("f1_pc_sw", "f1_sw_bookings");

            // Office A: the key has to be in hand
            Inventory.Add("dummy", "Something else");   // now the key is in the pocket, not in hand
            yield return Door("f1_office_a");
            Inventory.Remove("dummy");
            Assert.AreNotEqual("f1_office_a", RoomManager.Instance.Current.roomId, "key in the pocket isn't enough");
            yield return Door("f1_office_a", "key_f1_a");
            Assert.AreEqual("f1_office_a", RoomManager.Instance.Current.roomId);
            Assert.IsFalse(Inventory.Has("key_f1_a"), "key used up in the door");
            yield return SolvePc("f1_pc_a", "f1_a_doorcodes");
            Assert.IsTrue(Inventory.Has("code_f1_b"), "the code goes in the pockets");

            // Office B (its code, in hand): drawer -> Office C key
            yield return Door("f1_office_b", "code_f1_b");
            yield return Use("f1_b_drawer");
            Assert.IsTrue(Inventory.Has("key_f1_c"));
            yield return SolvePc("f1_pc_b", "f1_b_inbox");

            // SE: drawer -> vendor slip; PC -> Office E code
            yield return Go("f1_office_se", "door0");
            yield return Use("f1_se_drawer");
            Assert.IsTrue(Inventory.Has("slip"));
            yield return SolvePc("f1_pc_se", "f1_se_tickets");

            // Office C (key) -> Office D code; Office D (5590) -> cost centre printout
            yield return Door("f1_office_c", "key_f1_c");
            yield return SolvePc("f1_pc_c", "f1_c_invoices");
            Assert.IsFalse(Inventory.Has("slip"), "slip used");
            yield return Door("f1_office_d", "code_f1_d");
            yield return SolvePc("f1_pc_d", "f1_d_budget");
            Assert.IsTrue(Inventory.Has("budget"));

            // Office E (2468): shelf -> Office F key; Office F -> the spare is in the lounge locker
            yield return Door("f1_office_e", "code_f1_e");
            yield return Use("f1_e_shelf");
            Assert.IsTrue(Inventory.Has("key_f1_f"));
            yield return SolvePc("f1_pc_e", "f1_e_archive");
            yield return Door("f1_office_f", "key_f1_f");
            yield return SolvePc("f1_pc_f", "f1_f_keys");
            yield return Go("f1_lounge", "door0");
            yield return Use("f1_locker");
            Assert.IsTrue(Inventory.Has("key_f1_m"));

            // Manager's office: the power is on the HR floor; Linda's desk has the tape recorder
            yield return Door("f1_manager", "key_f1_m");
            yield return SolvePc("f1_pc_m", "f1_m_systems");
            yield return Use("f1_m_shelf");
            Assert.IsTrue(Inventory.Has("tape"));

            // the meeting room is locked until the gag
            yield return Door("f1_meeting");
            Assert.AreNotEqual("f1_meeting", RoomManager.Instance.Current.roomId);
            yield return Settle();
            yield return Go("f1_wash_m", "door0");
            yield return Use("f1_toilet");
            Assert.IsTrue(GameState.Has("f1_meeting_open"));
            Assert.IsFalse(Npc.Find("linda").gameObject.activeSelf);
            yield return Go("floor1", "f1_wash_m_door0");
            yield return Wait(() => Npc.Find("linda_hall").gameObject.activeSelf && !Npc.Find("ken_hall").IsWalking && Npc.Find("ken_hall").gameObject.activeSelf, "Linda and Ken wait outside the gents'", 20f);
            yield return Door("f1_meeting");
            Assert.AreEqual("f1_meeting", RoomManager.Instance.Current.roomId);

            // terminal: hack (clearance 2) then the lessons
            for (int i = 1; i <= 5; i++) GameState.Set($"f1_tut_{i}_done");
            ScriptedInteractable.Find("f1_terminal").OnUse();
            yield return Wait(() => GameState.Has("f1_hacked"), "hack", 30f);
            Assert.AreEqual(2, GameState.Clearance);
            Assert.IsFalse(Inventory.Items.Any(i => i.id != "usb"), "pockets cleared for the next floor");
            yield return Wait(() => { TerminalUI.Instance.Close(); return !TerminalUI.Instance.IsOpen; }, "leave terminal", 30f);
            yield return Settle();
            StringAssert.Contains("elevator", HudUI.Instance.Objective);

            // the call by the elevator: lie
            yield return Go("floor1", "elevator");
            yield return Wait(() => PhoneUI.Instance.IsRinging, "decision call");
            PhoneUI.Instance.Answer();
            yield return Wait(() => DialogueUI.Instance.IsChoosing || (DialogueUI.Instance.IsOpen && Advance()), "choice", 60f);
            DialogueUI.Instance.Choose(1);
            yield return Settle();
            Assert.IsTrue(GameState.Has("d1_lie"));
            Assert.IsFalse(GameState.Has("d1_truth"));
            StringAssert.Contains("Floor 2", HudUI.Instance.Objective);
        }

        // ---- helpers

        private static bool Advance() { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); return false; }

        private static bool PutInFront(Guard g, PlayerController p)
        {
            var npc = g.GetComponent<Npc>();
            p.transform.position = g.transform.position + (Vector3)(npc.Facing * 1.2f * g.transform.localScale.x);
            return false;
        }

        private static IEnumerator Use(string hotspot)
        {
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

        /// <summary>Use the corridor door into a room, holding an item (key or code) if given.</summary>
        private static IEnumerator Door(string room, string holding = null)
        {
            yield return Settle();
            if (RoomManager.Instance.Current.roomId != "floor1") yield return Go("floor1", "elevator");
            var door = UnityEngine.Object.FindObjectsByType<World.Door>(FindObjectsInactive.Include)
                .First(d => d.TargetRoomId == room && d.GetComponentInParent<Room>(true).roomId == "floor1");
            if (holding != null)
            {
                Assert.IsTrue(Inventory.Has(holding), "should have " + holding);
                for (int i = 0; i < Inventory.Items.Count; i++) if (Inventory.Items[i].id == holding) Inventory.Equip(i);
            }
            door.Interact(PlayerController.Instance);
            // wait to arrive (a locked door or wrong code just times out here; the caller checks the room)
            float t = 0f;
            while (t < 3f && !(RoomManager.Instance.Current.roomId == room && !RoomManager.Instance.IsTransitioning))
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;
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

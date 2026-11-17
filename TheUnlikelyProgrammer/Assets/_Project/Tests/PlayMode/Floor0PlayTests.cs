using System;
using System.Collections;
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
    /// Boots the real game scene and plays Floor 0 start to finish, standing in for the player's key presses
    /// (any error logged along the way fails the test). Runs at 8x speed.
    /// </summary>
    public class Floor0PlayTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator Floor0_CanBeFinished()
        {
            GameState.ResetAll();
            GameState.Set("intro_seen");
            Inventory.Clear();
            PhoneData.Clear();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            Time.timeScale = 8f;

            var rooms = RoomManager.Instance;
            Assert.AreEqual("floor0", rooms.Current.roomId);
            Assert.IsTrue(DialogueUI.Instance.IsOpen, "opening line should show");
            yield return Settle();
            StringAssert.Contains("closet", HudUI.Instance.Objective);
            Assert.AreEqual(1, PhoneData.Threads.Count, "Betty's message");

            // 1. the closet bin brings out the cart; Q takes hold of it
            var cart = JanitorCart.Find();
            Assert.IsFalse(cart.Spawned, "no cart before the closet");
            yield return Go("f0_closet", "door0");
            ScriptedInteractable.Find("closet_bin").OnUse();
            yield return Settle();
            Assert.IsTrue(cart.Spawned, "the bin brings out the cart");
            Assert.AreEqual(0, Inventory.Items.Count, "nothing goes in the pockets");
            cart.PickUp();
            Assert.IsTrue(JanitorCart.Holding);

            // 2-3. push it into Dan's office and mop
            yield return Go("f0_office", "door0");
            yield return Wait(() => HudUI.Instance.Objective.Contains("Mop"), "mop objective");
            var dan = Npc.Find("dan");
            Assert.IsTrue(dan.gameObject.activeSelf, "Dan is still here while Donnie mops");
            var spill = UnityEngine.Object.FindAnyObjectByType<Spill>(FindObjectsInactive.Include);
            spill.DevClean();

            // 4. floor clean: compulsory conversation, then Dan and Ryker walk out
            yield return Wait(() => !dan.gameObject.activeSelf && !Npc.Find("ryker").gameObject.activeSelf, "Dan and Ryker leave", 60f);
            yield return Wait(() => HudUI.Instance.Objective.Contains("stairs"), "stairs objective");
            yield return Settle();

            // computers are off-limits while holding the cart
            ScriptedInteractable.Find("dan_pc").Interact(PlayerController.Instance);
            Assert.IsFalse(TerminalUI.Instance.IsOpen, "no computers with the cart in hand");
            yield return Settle();
            cart.Drop();
            Assert.IsFalse(JanitorCart.Holding);

            // 5. stairs -> call -> Dan's PC
            yield return Go("floor0", "stairs_zone");
            yield return Wait(() => PhoneUI.Instance.IsRinging, "stairs call");
            yield return Settle();
            StringAssert.Contains("computer", HudUI.Instance.Objective);
            yield return Go("f0_office", "door0");
            ScriptedInteractable.Find("dan_pc").OnUse();
            Assert.IsTrue(TerminalUI.Instance.IsOpen, "Dan's PC opens");
            GameState.Set("f0_dan_pc_solved");
            TerminalUI.Instance.Close();
            yield return Settle();

            // 6. leave the office -> call -> find a cable
            yield return Go("floor0", "start");
            yield return Wait(() => PhoneUI.Instance.IsRinging, "leave-office call");
            yield return Settle();
            StringAssert.Contains("USB", HudUI.Instance.Objective);

            // 7. vending (no coin) -> locker coin -> vending with coin in hand
            ScriptedInteractable.Find("vending").OnUse();
            yield return Settle();
            StringAssert.Contains("coin", HudUI.Instance.Objective);
            ScriptedInteractable.Find("locker").OnUse();
            yield return Settle();
            Assert.IsTrue(Inventory.IsEquipped("coin"));
            ScriptedInteractable.Find("vending").OnUse();
            yield return Settle();
            Assert.IsTrue(Inventory.Has("usb"), "bought the cable");

            // 8. call (already answered by Settle) -> Manual arrives in messages -> download
            yield return Wait(() => HudUI.Instance.Objective.Contains("KODEX"), "cable call");
            yield return Settle();
            Assert.IsTrue(PhoneData.Threads.Exists(t => t.messages.Exists(m => m.attachment == "manual")), "Manual message");
            GameState.Set("has_manual");
            yield return Settle();
            StringAssert.Contains("terminal", HudUI.Instance.Objective);

            // 9. terminal while the receptionist is on her rounds (lessons already solved -> tutorial completes on close)
            var desk = rooms.MarkerPosition("floor0", "recep_desk");
            var recep = Npc.Find("receptionist");
            yield return Wait(() => Vector2.Distance(recep.transform.position, desk) > 6f, "receptionist leaves the desk", 90f);
            for (int i = 1; i <= 5; i++) GameState.Set($"f0_tut_{i}_done");
            Inventory.Equip(Inventory.Items.Count - 1);
            Assert.IsTrue(Inventory.IsEquipped("usb"));
            ScriptedInteractable.Find("employee_terminal").OnUse();
            Assert.IsTrue(TerminalUI.Instance.IsOpen, "terminal opens");
            yield return Wait(() => GameState.Has("f0_hacked"), "hack upgrades clearance", 30f);
            Assert.AreEqual(1, GameState.Clearance);
            // the hack finishes its last lines, then the screen can be left
            yield return Wait(() => { TerminalUI.Instance.Close(); return !TerminalUI.Instance.IsOpen; }, "leave terminal", 30f);
            yield return Settle();

            StringAssert.Contains("elevator", HudUI.Instance.Objective);
            // the elevator: Floor 2 is refused at clearance 1, Floor 1 works
            ElevatorUI.Instance.Open("floor0");
            Assert.IsTrue(ElevatorUI.Instance.IsOpen);
            ElevatorUI.Instance.Choose(2);
            StringAssert.Contains("denied", ElevatorUI.Instance.Caption);
            ElevatorUI.Instance.Choose(1);
            yield return Wait(() => RoomManager.Instance.Current.roomId == "floor1" && !RoomManager.Instance.IsTransitioning, "ride to floor 1");
            Assert.IsFalse(ElevatorUI.Instance.IsOpen);
            yield return Go("floor0", "start");

            ScriptedInteractable.Find("entrance").OnUse();
            yield return null;
            Assert.IsTrue(DialogueUI.Instance.IsOpen);
            yield return Settle();
        }

        // ---- helpers

        private static IEnumerator Go(string room, string spawn)
        {
            yield return Settle();
            RoomManager.Instance.GoTo(room, spawn);
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == room, "enter " + room);
            yield return null;
        }

        /// <summary>Answers calls and clicks through dialogue until nothing is waiting.</summary>
        private static IEnumerator Settle()
        {
            for (int quiet = 0, n = 0; quiet < 10 && n < 2000; n++)
            {
                bool busy = false;
                if (PhoneUI.Instance.IsRinging) { PhoneUI.Instance.Answer(); busy = true; }
                if (DialogueUI.Instance.IsOpen) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); busy = true; }
                quiet = busy ? 0 : quiet + 1;
                yield return null;
            }
        }

        private static IEnumerator Wait(Func<bool> condition, string what, float seconds = 30f)
        {
            float t = 0f;
            while (!condition())
            {
                if (DialogueUI.Instance.IsOpen) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                t += Time.unscaledDeltaTime;
                if (t > seconds) Assert.Fail("Timed out waiting for: " + what + "   (objective: " + HudUI.Instance.Objective + ")");
                yield return null;
            }
        }
    }
}

using System.Collections;
using Unlikely.Core;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unlikely.Story
{
    /// <summary>
    /// Development jump points (editor / dev builds): F10 opens a menu; picking a point reloads the game fresh with
    /// everything before that point done, and puts Donnie there. Used to test each floor, each truth-or-lie call,
    /// and Floor 3's phone / bank / server room without replaying the game.
    /// </summary>
    public static class DevJump
    {
        public static readonly (string id, string label)[] Points =
        {
            ("f0", "Floor 0  ·  start of the game"),
            ("f1", "Floor 1  ·  arrival"),
            ("f1_call", "Floor 1  ·  truth / lie call  (D1)"),
            ("f2", "Floor 2  ·  arrival"),
            ("f2_call", "Floor 2  ·  truth / lie call  (D2)"),
            ("f3_lie", "Floor 3  ·  arrival  (lied on Floor 2: Ryker discovered)"),
            ("f3_truth", "Floor 3  ·  arrival  (told the truth on Floor 2)"),
            ("f3_blackout", "Floor 3  ·  decrypt with Liam's card  (blackout next)"),
            ("f3_ceo_card", "Floor 3  ·  blackout, CEO's keycard in hand  (phone update + bank next)"),
            ("f3_server", "Floor 3  ·  server room door  (D3 call)"),
            ("end_acc", "Ending  ·  Accomplice  (F2 truth, F3 truth)"),
            ("end_lq", "Ending  ·  The Last Query  (F2 truth, F3 lie)"),
            ("end_cap", "Ending  ·  Capitalist  (F2 lie, F3 truth, CEO mail read, shares not bought)"),
            ("end_cap_missed", "Ending  ·  Capitalist missed  (F2 lie, F3 truth, CEO mail NOT read)"),
            ("end_sam", "Ending  ·  Good Samaritan  (F2 lie, F3 lie)"),
        };

        /// <summary>The point to set up after the reload (null = a normal start).</summary>
        public static string Pending { get; private set; }

        public static void Go(string id)
        {
            Pending = id;
            if (id != null) SaveSystem.Stop();   // a dev jump is a test run: it doesn't touch the save slots
            Time.timeScale = 1f;
            HideSpot.Enabled = false;
            Unlikely.Player.PlayerController.Frozen = false;
            GameState.ResetAll();
            Inventory.Clear();
            PhoneData.Clear();
            SceneManager.LoadScene(Unlikely.UI.TitleMenuUI.GameScene);   // from the title screen
        }

        private static int FloorOf(string id) => id.StartsWith("end_") ? 3 : id[1] - '0';

        /// <summary>Before the floors' scripts start: what the earlier floors would have left behind.</summary>
        public static void BeforeBegin()
        {
            if (Pending == null) return;
            GameState.Set("intro_seen");
            int floor = FloorOf(Pending);
            if (floor >= 1)
            {
                GameState.Clearance = 1;
                foreach (var f in new[] { "has_manual", "f0_complete", "f0_hacked" }) GameState.Set(f);
                Inventory.Add("usb", "USB-C cable", false);
            }
            if (floor >= 2)
            {
                GameState.Clearance = 2;
                foreach (var f in new[] { "f1_complete", "f1_hacked", "d1_truth" }) GameState.Set(f);
            }
            if (floor >= 3)
            {
                GameState.Clearance = 3;
                foreach (var f in new[] { "f2_complete", "f2_hacked", "f2_plant_found", "f2_deal", "f2_leverage" }) GameState.Set(f);
            }
            foreach (var l in ManualLessons.All) if (l.floor < floor) GameState.Set("lesson_" + l.id);
        }

        /// <summary>After the floors' scripts have started: move Donnie and set the floor's own progress.</summary>
        public static IEnumerator AfterBegin()
        {
            string id = Pending;
            Pending = null;
            if (id == null) yield break;
            yield return null;
            var rooms = RoomManager.Instance;
            switch (id)
            {
                case "f0": break;
                case "f1": rooms.GoTo("floor1", "elevator"); break;
                case "f1_call": Object.FindAnyObjectByType<Floor1Story>().DevJumpToDecision(); break;
                case "f2": rooms.GoTo("floor2", "elevator"); break;
                case "f2_call": Floor2Story.Instance.DevSkipToDecision(); break;
                case "f3_lie": Floor3Story.Instance.DevJump("start", true); break;
                case "f3_truth": Floor3Story.Instance.DevJump("start", false); break;
                case "f3_blackout": Floor3Story.Instance.DevJump("blackout", true); break;
                case "f3_ceo_card": Floor3Story.Instance.DevJump("ceo_card", true); break;
                case "f3_server": Floor3Story.Instance.DevJump("server", true); break;
                case "end_acc": EndingStory.Instance.DevJump("acc"); break;
                case "end_lq": EndingStory.Instance.DevJump("lq"); break;
                case "end_cap": EndingStory.Instance.DevJump("cap"); break;
                case "end_cap_missed": EndingStory.Instance.DevJump("cap_missed"); break;
                case "end_sam": EndingStory.Instance.DevJump("sam"); break;
            }
            Unlikely.UI.HudUI.Instance?.Toast("DEV: " + System.Array.Find(Points, p => p.id == id).label);
        }
    }
}

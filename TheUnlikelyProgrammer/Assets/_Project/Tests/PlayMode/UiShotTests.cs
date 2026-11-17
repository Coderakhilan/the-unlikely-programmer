using System.Collections;
using System.IO;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>
    /// Not a real test: renders the HUD, dialogue box and menus to PNGs for a look (run on its own with -testFilter UiShotTests).
    /// Output folder: the UI_SHOTS environment variable, else the system temp folder.
    /// </summary>
    [Explicit]
    public class UiShotTests
    {
        private static string Dir => System.Environment.GetEnvironmentVariable("UI_SHOTS") ?? Path.GetTempPath();

        /// <summary>
        /// Batch mode never reaches end-of-frame, so: put every overlay canvas on the main camera for a moment,
        /// render that camera into a 1920x1080 texture and save it.
        /// </summary>
        private static IEnumerator Shot(string name, int w = 1920, int h = 1080)
        {
            yield return null;
            var cam = Camera.main;
            var rt = new RenderTexture(w, h, 24);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                if (!canvases[i].isRootCanvas) continue;
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = 1f + (1000 - canvases[i].sortingOrder) * 0.001f;
            }
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            for (int i = 0; i < canvases.Length; i++) if (canvases[i].isRootCanvas) canvases[i].renderMode = modes[i];
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }

        [UnityTest]
        public IEnumerator Capture()
        {
            GameState.ResetAll();
            GameState.Set("intro_seen");
            // the start-up screens (never accepted here: the shot is taken before Enter)
            TitleSequences.SkipForTests = false;
            TitleSequences.BootDone = false;
            SceneManager.LoadScene("Title");
            yield return new WaitForSeconds(1.6f);
            yield return Shot("ui_splash");
            yield return new WaitForSeconds(3.2f);
            yield return Shot("ui_terms");
            // the credits after an ending
            TitleSequences.PendingCredits = true;
            SceneManager.LoadScene("Title");
            yield return new WaitForSeconds(6.2f);
            yield return Shot("ui_endings_count");
            yield return new WaitForSeconds(9f);
            yield return Shot("ui_credits");
            TitleSequences.SkipForTests = true;
            SceneManager.LoadScene("Title");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("ui_title");
            var title = Object.FindAnyObjectByType<TitleMenuUI>();
            typeof(TitleMenuUI).GetMethod("ShowOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(title, null);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("ui_options");
            yield return Shot("ui_options_16x10", 1920, 1200);
            foreach (var id in new[] { "betty_donnie", "another_day_at_work", "framed", "the_usual_suspects" }) Profile.HearTrack(id);
            const System.Reflection.BindingFlags np = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(TitleMenuUI).GetMethod("ShowSoundtrack", np).Invoke(title, null);
            title.SoundtrackPage.Play(System.Array.FindIndex(Soundtrack.All, t => t.id == "framed"));
            yield return new WaitForSeconds(2f);
            yield return Shot("ui_soundtrack");
            title.SoundtrackPage.Close();
            Profile.SeeArt("betty_being_kidnapped");
            Profile.SeeArt("sam_s_funeral");
            typeof(TitleMenuUI).GetMethod("ShowGallery", np).Invoke(title, null);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("ui_gallery");
            typeof(TitleMenuUI).GetMethod("ShowMain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.Invoke(title, null);

            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            DevJump.Go("f1");   // reloads the active scene, so Floors has to be it
            yield return new WaitForSeconds(2f);
            // let the arrival call play out
            for (float t = 0; t < 8f; t += Time.unscaledDeltaTime)
            {
                if (PhoneUI.Instance.IsRinging) PhoneUI.Instance.Answer();
                if (DialogueUI.Instance.IsOpen) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                yield return null;
            }
            if (PhoneUI.Instance.IsOpen) PhoneUI.Instance.Close();
            Inventory.Add("key_f1_a", "Office A key", false);
            Inventory.Add("slip", "Slip: vendor V-207", false);
            Inventory.Add("coffee", "Hot coffee (lid off)", false);
            Inventory.Add("keycard_ceo", "CEO's keycard", false);
            Inventory.Add("flashlight", "Torch", false);
            Inventory.Add("code_f1_b", "Office B code: 3172");
            HudUI.Instance.SetObjective("Find out who controls the building's power");
            HudUI.Instance.SetHint("Bring your cart into staffed offices. Use computers when nobody is looking.");
            HudUI.Instance.Toast("Got: Office A key", 10f);
            DialogueUI.Instance.SetPrompt("Search the cabinet");
            yield return new WaitForSeconds(0.8f);
            yield return Shot("ui_hud");

            DialogueUI.Instance.Show("", new[] { "Yuvie: Donnie. You're on Administration. Good. Now find out who runs the power, and do it quietly." });
            yield return new WaitForSeconds(0.4f);
            DialogueUI.Instance.Advance();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ui_dialogue");
            DialogueUI.Instance.Advance();
            DialogueUI.Instance.Show("", new[] { "[worried] Somewhere on this floor is a spreadsheet that knows where my daughter is." });
            yield return new WaitForSeconds(0.3f);
            DialogueUI.Instance.Advance();
            yield return Shot("ui_thought");
            DialogueUI.Instance.Advance();

            PauseMenuUI.Instance.Open();
            yield return null;
            yield return Shot("ui_pause");
            PauseMenuUI.Instance.Close();
            PhoneUI.Instance.Open();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ui_phone");
            PhoneUI.Instance.Close();

            KeypadUI.Instance.Open("Office B", "3172", () => { });
            yield return new WaitForSeconds(0.2f);
            yield return Shot("ui_keypad");
            KeypadUI.Instance.Close();

            ElevatorUI.Instance.Open("floor1");
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ui_elevator");
            ElevatorUI.Instance.Close();

            TerminalUI.Instance.Open(Unlikely.Puzzles.PuzzleData.FromJson(File.ReadAllText("Assets/_Project/Data/Puzzles/f1_a_doorcodes.json")), ComputerSkin.EmployeePc);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ui_pc");
            TerminalUI.Instance.Close();
            TerminalUI.Instance.Open(Unlikely.Puzzles.PuzzleData.FromJson(File.ReadAllText("Assets/_Project/Data/Puzzles/f0_dan_pc.json")), ComputerSkin.EmployeePc);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ui_pc_note");
            TerminalUI.Instance.Close();

            var cols = new[] { new[] { "staff_id", "name", "title" }, new[] { "rota_day", "holder" }, new[] { "leave_id", "on_leave", "leave_day", "cover" },
                new[] { "badge_no", "owner", "status" }, new[] { "part_id", "card", "position", "cipher" } };
            string[] ids = { "board_long", "board_side", "open_l", "open_r", "exec_b" }, tables = { "staff", "rota", "leave", "badges", "code_parts" };
            Vector2[] where = { new Vector2(105, 335), new Vector2(300, 335), new Vector2(180, 100), new Vector2(400, 100), new Vector2(505, 228) };
            var nodes = new LinkStationUI.Node[5];
            for (int i = 0; i < 5; i++) nodes[i] = new LinkStationUI.Node { id = ids[i], name = "PC " + ids[i], place = "Floor 3", table = tables[i], columns = cols[i], pos = where[i] };
            LinkStationUI.Instance.Open(nodes, m => m == "board_side" ? "board_long" : null, (a, b) => { }, () => null, m => false,
                () => "Encrypted code: not found yet.", () => { }, () => { });
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ui_linkstation");
            yield return Shot("ui_linkstation_16x10", 1920, 1200);
            LinkStationUI.Instance.Close();

            GameOverUI.Instance.Show("CAUGHT", "\"Since when does the janitor do spreadsheets?\"", () => { });
            yield return new WaitForSeconds(0.2f);
            yield return Shot("ui_gameover");
            GameOverUI.Instance.Retry();

            Inventory.Add(TheoryBookUI.ItemId, TheoryBookUI.ItemName, false);
            TheoryBookUI.Instance.Open();
            yield return new WaitForSeconds(0.2f);
            yield return Shot("ui_book");
            TheoryBookUI.Instance.Close();
            Assert.Pass("shots in " + Dir);
        }
    }
}

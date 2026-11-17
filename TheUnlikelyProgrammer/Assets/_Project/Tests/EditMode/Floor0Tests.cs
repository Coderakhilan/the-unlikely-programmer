using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Puzzles;
using Unlikely.SQL;
using Unlikely.Story;
using Unlikely.World;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unlikely.Tests
{
    /// <summary>Checks that Floor 0 can actually be finished: puzzles solvable, text present, story objects in the scene.</summary>
    public class Floor0Tests
    {
        private const string Data = "Assets/_Project/Data";

        private static readonly Dictionary<string, string> Solutions = new Dictionary<string, string>
        {
            { "f0_dan_pc", "SELECT * FROM employees WHERE role = 'Intern'" },
            { "f0_tut_1", "SELECT * FROM staff" },
            { "f0_tut_2", "SELECT name, department FROM staff" },
            { "f0_tut_3", "SELECT name FROM staff WHERE floor = 1" },
            { "f0_tut_4", "SELECT name FROM staff WHERE department = 'Accounts' AND floor = 1" },
            { "f0_tut_5", "SELECT name FROM staff WHERE department = 'Records' OR department = 'Human Resources'" },
        };

        [Test]
        public void EveryFloor0Puzzle_IsSolvedByItsIntendedQuery()
        {
            foreach (var kv in Solutions)
            {
                var puzzle = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/{kv.Key}.json"));
                var result = SqlEngine.Execute(puzzle.BuildDatabase(), kv.Value);
                Assert.IsTrue(PuzzleChecker.Check(puzzle, result, out string feedback), kv.Key + ": " + feedback);
            }
        }

        [Test]
        public void TutorialSteps_UnlockEveryFloor0Lesson()
        {
            var unlocked = new HashSet<string>();
            for (int i = 1; i <= 5; i++)
            {
                var p = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f0_tut_{i}.json"));
                if (!string.IsNullOrEmpty(p.unlocksLessons))
                    foreach (var l in p.unlocksLessons.Split(',')) unlocked.Add(l.Trim());
            }
            foreach (var lesson in ManualLessons.All.Where(l => l.floor == 0))
                Assert.IsTrue(unlocked.Contains(lesson.id), "No tutorial step unlocks lesson " + lesson.id);
        }

        [Test]
        public void Floor0Script_HasEveryDialogueEntry()
        {
            DialogueBank.Load(new TextAsset(File.ReadAllText($"{Data}/Dialogue/floor0.json")));
            string story = File.ReadAllText("Assets/_Project/Scripts/Story/Floor0Story.cs");
            var ids = System.Text.RegularExpressions.Regex.Matches(story, "(?:Say|Show|Call|Get|One)\\(\"([a-z0-9_]+)\"\\)")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.Greater(ids.Count, 20);
            foreach (var id in ids)
                Assert.IsFalse(DialogueBank.Get(id)[0].StartsWith("[missing"), "floor0.json has no entry " + id);
        }

        [Test]
        public void Cutscenes_UseImagesAndPortraitsThatExist()
        {
            StoryScenes.Load(new TextAsset(File.ReadAllText($"{Data}/Dialogue/cutscenes.json")));
            foreach (var id in new[] { StoryScenes.Intro, StoryScenes.EndingAccomplice, StoryScenes.EndingLastQuery, StoryScenes.EndingCapitalist, StoryScenes.EndingGoodSamaritan })
            {
                var seq = StoryScenes.Get(id);
                Assert.IsNotNull(seq, "missing sequence " + id);
                foreach (var shot in seq.shots)
                {
                    if (!string.IsNullOrEmpty(shot.image))
                        Assert.IsTrue(File.Exists($"Assets/_Project/Art/Processed/cutscenes/{shot.image}.png"), id + ": no image " + shot.image);
                    foreach (var line in shot.lines)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"^([A-Za-z][A-Za-z ]{0,20}?)(?:\[([a-z_]+)\])?:\s");
                        if (!m.Success) continue;
                        string who = m.Groups[1].Value.Trim().ToLowerInvariant();
                        bool face = File.Exists($"Assets/_Project/Art/Processed/portraits/{who}_neutral.png") || File.Exists($"Assets/_Project/Art/Processed/portraits/{who}.png");
                        Assert.IsTrue(face, id + ": no portrait for speaker " + who);
                        if (m.Groups[2].Success && who != "yuvie" && who != "adi" && who != "irshad")
                            Assert.IsTrue(File.Exists($"Assets/_Project/Art/Processed/portraits/{who}_{m.Groups[2].Value}.png"), id + ": no expression " + who + "_" + m.Groups[2].Value);
                    }
                }
            }
        }

        [Test]
        public void FloorsScene_HasAllFloor0StoryObjects()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Floors.unity");

            var hotspots = Object.FindObjectsByType<ScriptedInteractable>(FindObjectsInactive.Include).Select(s => s.Id).ToList();
            foreach (var id in new[] { "employee_terminal", "dan_pc", "vending", "locker", "closet_bin", "entrance" })
                Assert.Contains(id, hotspots);

            var people = Object.FindObjectsByType<Npc>(FindObjectsInactive.Include).Select(n => n.Id).ToList();
            foreach (var id in new[] { "dan", "ryker", "receptionist", "lounge_a", "lounge_b", "hall_a", "hall_b", "hall_c" })
                Assert.Contains(id, people);

            Assert.AreEqual(1, Object.FindObjectsByType<JanitorCart>(FindObjectsInactive.Include).Length);
            Assert.IsTrue(Object.FindObjectsByType<Spill>(FindObjectsInactive.Include).Any(s => s.Id == "dan_office_spill"));

            var rooms = Object.FindObjectsByType<Room>(FindObjectsInactive.Include).ToDictionary(r => r.roomId);
            foreach (var m in new[] { "start", "caught", "stairs_zone", "terminal_zone", "recep_desk", "recep_p1", "recep_p5" })
                Assert.AreNotEqual(rooms["floor0"].transform, rooms["floor0"].GetSpawn(m), "floor0 marker missing: " + m);
            foreach (var m in new[] { "npc_mid", "npc_exit" })
                Assert.AreNotEqual(rooms["f0_office"].transform, rooms["f0_office"].GetSpawn(m), "office marker missing: " + m);

            Assert.IsNotNull(Object.FindAnyObjectByType<Floor0Story>());
            Assert.IsNotNull(Object.FindAnyObjectByType<StoryDirector>());
        }
    }
}

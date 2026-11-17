using Unlikely.Player;
using Unlikely.Puzzles;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.Interaction
{
    /// <summary>
    /// A computer that opens the full-screen SQL interface for one puzzle.
    /// Black employee PCs hold the mission puzzles; the beige employee terminal (one per floor) teaches and grants clearance.
    /// </summary>
    public class ComputerTerminal : Interactable
    {
        [SerializeField] private TextAsset puzzleJson;
        [SerializeField] private string prompt = "Use computer";
        [SerializeField] private ComputerSkin skin = ComputerSkin.EmployeePc;

        private PuzzleData puzzle;

        public override string Prompt => prompt;

        public void Configure(TextAsset json, string promptText, ComputerSkin look = ComputerSkin.EmployeePc)
        {
            puzzleJson = json;
            prompt = promptText;
            skin = look;
        }

        public override void Interact(PlayerController player)
        {
            if (puzzle == null)
            {
                if (puzzleJson == null)
                {
                    DialogueUI.Instance.Show("", new[] { "The screen is asleep. Nothing on it for me." });
                    return;
                }
                puzzle = PuzzleData.FromJson(puzzleJson.text);
            }
            TerminalUI.Instance.Open(puzzle, skin);
        }
    }
}

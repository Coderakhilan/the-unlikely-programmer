using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.Interaction
{
    /// <summary>An object or NPC that just says some lines when interacted with.</summary>
    public class DialogueInteractable : Interactable
    {
        [SerializeField] private string prompt = "Inspect";
        [SerializeField] private string speaker = "";
        [SerializeField, TextArea(2, 4)] private string[] lines = { "..." };

        public override string Prompt => prompt;

        public void Configure(string promptText, string speakerName, params string[] dialogueLines)
        {
            prompt = promptText;
            speaker = speakerName;
            lines = dialogueLines;
        }

        public override void Interact(PlayerController player)
        {
            DialogueUI.Instance.Show(speaker, lines);
        }
    }
}

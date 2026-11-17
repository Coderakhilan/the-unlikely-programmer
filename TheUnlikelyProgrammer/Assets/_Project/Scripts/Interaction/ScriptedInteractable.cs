using System;
using System.Collections.Generic;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.Interaction
{
    /// <summary>
    /// A hotspot whose behaviour is supplied by the story script at runtime (terminal, vending machine, locker...).
    /// The scene builder places it and gives it an id; the story finds it with <see cref="Find"/> and sets OnUse.
    /// With no handler it just says its fallback lines.
    /// </summary>
    public class ScriptedInteractable : Interactable
    {
        [SerializeField] private string id;
        [SerializeField] private string prompt = "Inspect";
        [SerializeField, TextArea] private string[] fallbackLines = { "Nothing to do here." };

        public string Id => id;
        public Action OnUse;
        public Func<string> PromptOverride;

        public override string Prompt => PromptOverride != null ? PromptOverride() : prompt;

        public void Configure(string objectId, string promptText, params string[] lines)
        {
            id = objectId;
            prompt = promptText;
            fallbackLines = lines;
        }

        /// <summary>Computers: can't be used while Donnie is pushing the cart.</summary>
        [SerializeField] private bool needsFreeHands;

        public void RequireFreeHands() => needsFreeHands = true;

        /// <summary>Snooping (computers, drawers): if a patrolling employee is watching, Donnie gets shooed instead.</summary>
        [SerializeField] private bool snoop;
        public void MarkSnooping() { snoop = true; needsFreeHands = true; }

        public override void Interact(PlayerController player)
        {
            // anything hands-on (search, use, take, read...) needs both hands; only looking and the gear bin don't
            bool handsOn = needsFreeHands || !(prompt.StartsWith("Look") || prompt.StartsWith("Get your gear"));
            if (handsOn && World.JanitorCart.Holding)
            {
                DialogueUI.Instance.Show("", new[] { "Not with the cart in my hands. (Q to let go of it.)" });
                return;
            }
            if (snoop && NPC.Guard.RefuseIfWatched()) return;
            if (OnUse != null) OnUse();
            else DialogueUI.Instance.Show("", fallbackLines);
        }

        private static Dictionary<string, ScriptedInteractable> all;

        /// <summary>Finds a hotspot by id, including ones in rooms that are currently hidden.</summary>
        public static ScriptedInteractable Find(string objectId)
        {
            if (all == null)
            {
                all = new Dictionary<string, ScriptedInteractable>();
                foreach (var s in FindObjectsByType<ScriptedInteractable>(FindObjectsInactive.Include))
                    all[s.id] = s;
            }
            all.TryGetValue(objectId, out var found);
            if (found == null) Debug.LogWarning("No scripted hotspot with id " + objectId);
            return found;
        }

        private void OnDestroy() => all = null;
    }
}

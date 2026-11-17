using Unlikely.Player;
using UnityEngine;

namespace Unlikely.Interaction
{
    /// <summary>
    /// Anything the player can inspect/use with the interact button.
    /// Needs a (non-trigger) Collider2D on the same object.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>Short hint shown near the bottom of the screen, e.g. "Inspect".</summary>
        public virtual string Prompt => "Inspect";

        /// <summary>The key shown next to the prompt (E for most things; Q for the cart, F for mopping).</summary>
        public virtual string Key => "E";

        public abstract void Interact(PlayerController player);
    }
}

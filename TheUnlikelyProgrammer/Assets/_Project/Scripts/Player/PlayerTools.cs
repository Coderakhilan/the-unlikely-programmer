using Unlikely.Core;
using Unlikely.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unlikely.Player
{
    /// <summary>Number keys 1-9 pick the item in hand; gamepad shoulder buttons cycle.</summary>
    public class PlayerTools : MonoBehaviour
    {
        private static readonly Key[] Digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        private static readonly Key[] Pad = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };

        private void Update()
        {
            if (UiKit.AnyModalOpen) return; // the elevator panel and terminal use number keys themselves
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            // Q (gamepad X / Square): take hold of the cart, or let go of it
            bool q = (kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame);
            bool busy = PlayerController.Frozen || (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)
                        || (World.RoomManager.Instance != null && World.RoomManager.Instance.IsTransitioning)
                        || (PlayerController.Instance != null && PlayerController.Instance.IsAutoWalking);
            if (q && !busy)
            {
                var cart = World.JanitorCart.Find();
                if (cart != null)
                {
                    if (cart.Held) cart.Drop();
                    else if (cart.CanReach(PlayerController.Instance)) cart.PickUp();
                }
            }

            if (kb != null)
                for (int i = 0; i < Digits.Length; i++)
                    if (kb[Digits[i]].wasPressedThisFrame || kb[Pad[i]].wasPressedThisFrame) Inventory.Equip(i);

            var pad = Gamepad.current;
            int n = Inventory.Items.Count;
            if (pad != null && n > 0)
            {
                if (pad.rightShoulder.wasPressedThisFrame) Inventory.Equip((Inventory.EquippedIndex + 1) % n);
                if (pad.leftShoulder.wasPressedThisFrame) Inventory.Equip((Inventory.EquippedIndex + n - 1) % n);
            }
        }
    }
}

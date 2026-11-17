using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.World
{
    public enum LockKind { None, Key, Code, Story, Permanent }

    /// <summary>
    /// A doorway to another room. It can be locked: by a key (an inventory item, used up when the door opens),
    /// by a keypad code, or by the story (opened with a GameState flag). All doors into the same room share
    /// one lock, stored as the flag "unlocked_{room}".
    /// </summary>
    public class Door : Interactable
    {
        [SerializeField] private string targetRoomId;
        [SerializeField] private string targetSpawnId;
        [SerializeField] private string prompt = "Go through";
        [SerializeField] private LockKind lockKind;
        [SerializeField] private string lockValue;     // key item id, keypad code, or story flag
        [SerializeField] private string roomName;
        [SerializeField] private string[] lockedLines = { "It's locked." };

        public string TargetRoomId => targetRoomId;

        /// <summary>Story override for any door's prompt (return null to leave it alone).</summary>
        public static System.Func<Door, string> PromptHook;

        /// <summary>Story override for using a door (return true when the story handled it).</summary>
        public static System.Func<Door, bool> InteractHook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHooks() { PromptHook = null; InteractHook = null; }
        public bool IsLocked => lockKind != LockKind.None && !GameState.Has("unlocked_" + targetRoomId)
                                && !(lockKind == LockKind.Story && GameState.Has(lockValue));

        public override string Prompt
        {
            get
            {
                var hooked = PromptHook?.Invoke(this);
                if (hooked != null) return hooked;
                if (!IsLocked) return prompt;
                if (lockKind == LockKind.Key) return Inventory.IsEquipped(lockValue) ? "Unlock " + roomName : roomName + " (locked)";
                if (lockKind == LockKind.Code) return "Keypad: " + roomName;
                return roomName + " (locked)";
            }
        }

        public void Configure(string roomId, string spawnId, string promptText)
        {
            targetRoomId = roomId;
            targetSpawnId = spawnId;
            prompt = promptText;
        }

        /// <summary>A door that never opens (just says something).</summary>
        public void ConfigureLocked(string promptText, params string[] lines)
        {
            lockKind = LockKind.Permanent;
            prompt = promptText;
            lockedLines = lines;
        }

        /// <summary>Lock a working door: kind Key (value = item id), Code (value = digits) or Story (value = flag).</summary>
        public void SetLock(LockKind kind, string value, string name, params string[] lines)
        {
            lockKind = kind;
            lockValue = value;
            roomName = name;
            if (lines != null && lines.Length > 0) lockedLines = lines;
        }

        public override void Interact(PlayerController player)
        {
            if (InteractHook != null && InteractHook(this)) return;
            if (lockKind == LockKind.Permanent)
            {
                DialogueUI.Instance.Show("", lockedLines);
                return;
            }
            if (IsLocked)
            {
                switch (lockKind)
                {
                    case LockKind.Key:
                        // a key, or a door code Donnie has found: he needs it in hand (its number key)
                        if (Inventory.IsEquipped(lockValue))
                        {
                            Inventory.Remove(lockValue);   // used up: the key stays in the door / the code's been punched in
                            Unlock();
                            HudUI.Instance?.Toast("Unlocked: " + roomName);
                            break;
                        }
                        if (Inventory.Has(lockValue))
                        {
                            int slot = 1;
                            for (int i = 0; i < Inventory.Items.Count; i++) if (Inventory.Items[i].id == lockValue) slot = i + 1;
                            DialogueUI.Instance.Show("", new[] { $"I've got what opens this. I just need it in hand. (Press {slot}.)" });
                            return;
                        }
                        DialogueUI.Instance.Show("", lockedLines);
                        return;
                    case LockKind.Code:
                        KeypadUI.Instance.Open(roomName, lockValue, () =>
                        {
                            Unlock();
                            HudUI.Instance?.Toast("Unlocked: " + roomName);
                            RoomManager.Instance.GoTo(targetRoomId, targetSpawnId);
                        });
                        return;
                    default:
                        DialogueUI.Instance.Show("", lockedLines);
                        return;
                }
            }
            RoomManager.Instance.GoTo(targetRoomId, targetSpawnId);
        }

        private void Unlock()
        {
            GameState.Set("unlocked_" + targetRoomId);
            Sfx.Play(Sfx.Id.Lock);
        }
    }
}

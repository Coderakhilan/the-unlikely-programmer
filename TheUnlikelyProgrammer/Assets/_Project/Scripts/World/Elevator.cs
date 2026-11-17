using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;

namespace Unlikely.World
{
    /// <summary>The elevator doors: opens the floor-selection panel.</summary>
    public class Elevator : Interactable
    {
        public override string Prompt => "Use elevator";

        /// <summary>Story override: return a line to refuse the elevator (e.g. during a power cut), or null.</summary>
        public static System.Func<string> Blocked;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHook() => Blocked = null;

        public override void Interact(PlayerController player)
        {
            var refuse = Blocked?.Invoke();
            if (refuse != null) { DialogueUI.Instance.Show("", new[] { refuse }); return; }
            var room = RoomManager.Instance != null ? RoomManager.Instance.Current : null;
            ElevatorUI.Instance.Open(room != null ? room.roomId : null);
        }
    }
}

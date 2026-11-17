using System;
using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.World
{
    /// <summary>
    /// Donnie's janitor cart. It starts hidden; the bin in the janitor's closet brings it out.
    /// Q next to it: Donnie takes hold of it (his sprite switches to pushing the cart) and walks around with it,
    /// through doors too. Q again: he lets go and it's parked in front of him.
    /// While holding it he can't use computers. Later floors use it as the disguise.
    /// </summary>
    public class JanitorCart : Interactable
    {
        private const float Reach = 1.4f;   // metres (scaled with Donnie)
        private const float DropAhead = 0.8f;

        private static JanitorCart cached;

        /// <summary>The cart, found even while it's hidden or in a hidden room.</summary>
        public static JanitorCart Find()
        {
            if (cached == null) cached = FindAnyObjectByType<JanitorCart>(FindObjectsInactive.Include);
            return cached;
        }

        /// <summary>True while Donnie is pushing the cart.</summary>
        public static bool Holding => cached != null && cached.Held;

        public bool Held { get; private set; }
        public bool Spawned => gameObject.activeSelf;
        public bool EverHeld { get; private set; }
        public event Action PickedUp;

        /// <summary>A resumed game: he has pushed it before (the tutorial doesn't ask again).</summary>
        public void MarkEverHeld() => EverHeld = true;

        public string RoomId
        {
            get
            {
                if (Held) return RoomManager.Instance != null && RoomManager.Instance.Current != null ? RoomManager.Instance.Current.roomId : null;
                var r = GetComponentInParent<Room>(true);
                return r != null ? r.roomId : null;
            }
        }

        public override string Prompt => "Push cart";
        public override string Key => "Q";

        public override void Interact(PlayerController player) => PickUp();

        /// <summary>Bring the cart out (from the closet bin) at a world position in a room.</summary>
        public void Spawn(Room room, Vector3 position)
        {
            gameObject.SetActive(true);
            Park(room, position);
        }

        public bool CanReach(PlayerController p) =>
            Spawned && !Held && gameObject.activeInHierarchy &&
            Vector2.Distance(p.transform.position, transform.position) <= Reach * p.transform.localScale.x;

        public void PickUp()
        {
            var p = PlayerController.Instance;
            if (Held || p == null) return;
            Held = true;
            cached = this;
            transform.SetParent(p.transform, false);
            transform.localPosition = Vector3.zero;
            SetVisible(false);
            SetPushing(p, true);
            if (!EverHeld) { EverHeld = true; PickedUp?.Invoke(); }
        }

        public void Drop()
        {
            var p = PlayerController.Instance;
            if (!Held || p == null) return;
            var room = RoomManager.Instance.Current;
            float s = p.transform.localScale.x;
            Held = false;
            Park(room, p.transform.position + (Vector3)(p.Facing * DropAhead * s));
            SetPushing(p, false);
        }

        private void Park(Room room, Vector3 position)
        {
            cached = this;
            transform.SetParent(room.transform, true);
            transform.localScale = Vector3.one * room.actorScale;
            transform.position = position;
            SetVisible(true);
        }

        private void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true)) r.enabled = on;
            foreach (var c in GetComponents<Collider2D>()) c.enabled = on;
        }

        private static void SetPushing(PlayerController p, bool on)
        {
            p.SpeedFactor = on ? 0.85f : 1f;
            var anim = p.GetComponentInChildren<SpriteWalkAnimator>();
            if (anim != null) anim.UseAlt = on;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => cached = null;
    }
}

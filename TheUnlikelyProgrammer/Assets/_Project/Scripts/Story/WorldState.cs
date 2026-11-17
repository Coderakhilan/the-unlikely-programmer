using Unlikely.Core;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;

namespace Unlikely.Story
{
    /// <summary>
    /// The parts of a resume save that aren't flags: which room Donnie is in and where he stands, the objective on screen,
    /// the cart, which spills are mopped, and every character's state (shown or not, where, facing, talkable).
    /// </summary>
    public static class WorldState
    {
        /// <summary>Fill a checkpoint with the world as it is now (SaveSystem.WorldCapture).</summary>
        public static void Capture(SaveSystem.Checkpoint cp)
        {
            var rooms = RoomManager.Instance;
            var p = PlayerController.Instance;
            if (rooms == null || rooms.Current == null || p == null) return;
            cp.room = rooms.Current.roomId;
            cp.x = p.transform.position.x;
            cp.y = p.transform.position.y;
            cp.objective = HudUI.Instance != null ? HudUI.Instance.Objective : null;
            cp.hint = HudUI.Instance != null ? HudUI.Instance.Hint : null;

            var w = cp.world;
            var cart = JanitorCart.Find();
            if (cart != null && cart.Spawned)
            {
                w.cartOut = true;
                w.cartRoom = cart.RoomId;
                Vector3 at = cart.Held ? p.transform.position + new Vector3(0.6f * p.transform.localScale.x, 0f, 0f) : cart.transform.position;
                w.cartX = at.x;
                w.cartY = at.y;
            }
            foreach (var s in Object.FindObjectsByType<Spill>(FindObjectsInactive.Include))
                if (s.Clean && !string.IsNullOrEmpty(s.Id)) w.cleanSpills.Add(s.Id);
            foreach (var n in Npc.All)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                var pos = n.transform.position;
                w.people.Add(new SaveSystem.Person
                {
                    id = n.Id, active = n.gameObject.activeSelf, talkable = n.Talkable,
                    x = pos.x, y = pos.y, fx = n.Facing.x, fy = n.Facing.y,
                });
            }
        }

        /// <summary>Put the world back (after the floors have set themselves up).</summary>
        public static void Apply(SaveSystem.Checkpoint cp)
        {
            var w = cp.world;
            if (w == null) return;
            var rooms = RoomManager.Instance;
            var cart = JanitorCart.Find();
            if (cart != null && w.cartOut)
            {
                var room = rooms != null ? rooms.GetRoom(w.cartRoom) : null;
                if (room != null) cart.Spawn(room, new Vector3(w.cartX, w.cartY, cart.transform.position.z));
            }
            foreach (var s in Object.FindObjectsByType<Spill>(FindObjectsInactive.Include))
                if (w.cleanSpills.Contains(s.Id) && !s.Clean) s.DevClean();
            foreach (var saved in w.people)
            {
                var n = Npc.Find(saved.id);
                if (n == null) continue;
                n.StopWalking();
                n.gameObject.SetActive(saved.active);
                n.transform.position = new Vector3(saved.x, saved.y, n.transform.position.z);
                var rb = n.GetComponent<Rigidbody2D>();
                if (rb != null) rb.position = n.transform.position;
                if (saved.fx != 0f || saved.fy != 0f) n.Face(new Vector2(saved.fx, saved.fy));
                n.Talkable = saved.talkable;
            }
            if (HudUI.Instance != null)
            {
                if (!string.IsNullOrEmpty(cp.objective)) HudUI.Instance.SetObjective(cp.objective);
                HudUI.Instance.SetHint(cp.hint);
            }
        }
    }
}

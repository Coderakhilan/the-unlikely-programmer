using UnityEngine;

namespace Unlikely.World
{
    /// <summary>
    /// One room of a floor: a background, walls, props and named spawn points (children of a "Spawns" object).
    /// Only the current room is active; RoomManager swaps them.
    /// </summary>
    public class Room : MonoBehaviour
    {
        public string roomId;
        public Rect bounds; // world-space rectangle of the room image, used to clamp the camera

        /// <summary>How big characters are drawn here. Enlarged room interiors use 2 so Donnie matches the zoomed-in art.</summary>
        public float actorScale = 1f;

        public Transform GetSpawn(string spawnId)
        {
            var t = transform.Find("Spawns/" + spawnId);
            return t != null ? t : transform;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using Unlikely.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unlikely.World
{
    /// <summary>
    /// Owns the rooms of a floor, shows one at a time and moves the player between them with a short fade.
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager Instance { get; private set; }

        [SerializeField] private List<Room> rooms = new List<Room>();
        [SerializeField] private string startRoomId;
        [SerializeField] private string startSpawnId = "start";
        [SerializeField] private Transform player;
        [SerializeField] private CameraFollow cameraFollow;
        [SerializeField] private float fadeSeconds = 0.18f;

        private Room current;
        private Image fade;

        public bool IsTransitioning { get; private set; }
        public Room Current => current;
        /// <summary>The room on screen: the one a Peek is showing, else Donnie's.</summary>
        public Room Viewed => peekRoom != null ? peekRoom : current;
        /// <summary>A Peek is showing another room (Donnie is hidden where he is).</summary>
        public bool IsPeeking => peekBack != null;

        /// <summary>Raised after the player is placed in a new room: (previous room or null, new room).</summary>
        public event System.Action<Room, Room> RoomChanged;

        public Room GetRoom(string id) => Find(id);

        /// <summary>World position of a named spawn/marker in any room (rooms keep their positions while inactive).</summary>
        public Vector3 MarkerPosition(string roomId, string markerId)
        {
            var r = Find(roomId);
            return r != null ? r.GetSpawn(markerId).position : Vector3.zero;
        }

        public void Configure(List<Room> allRooms, string startRoom, string startSpawn, Transform playerTransform, CameraFollow follow)
        {
            rooms = allRooms;
            startRoomId = startRoom;
            startSpawnId = startSpawn;
            player = playerTransform;
            cameraFollow = follow;
        }

        private void Awake()
        {
            Instance = this;
            BuildFade();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            foreach (var r in rooms) r.gameObject.SetActive(false);
            Enter(Find(startRoomId), startSpawnId);
        }

        public void GoTo(string roomId, string spawnId)
        {
            if (IsTransitioning) return;
            var target = Find(roomId);
            if (target == null) { Debug.LogWarning("RoomManager: unknown room " + roomId); return; }
            StartCoroutine(Transition(target, spawnId));
        }

        private IEnumerator Transition(Room target, string spawnId)
        {
            IsTransitioning = true;
            Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Door, 0.7f);
            yield return Fade(0f, 1f);
            Enter(target, spawnId);
            yield return null; // let physics/camera settle on the new room before revealing it
            yield return Fade(1f, 0f);
            IsTransitioning = false;
        }

        /// <summary>A loaded save: straight into a room at an exact spot (no fade; the load screen already covered it).</summary>
        public bool Place(string roomId, Vector3 position)
        {
            var target = Find(roomId);
            if (target == null) return false;
            Enter(target, null, position);
            return true;
        }

        private void Enter(Room target, string spawnId, Vector3? exact = null)
        {
            if (target == null) return;
            var previous = current;
            if (current != null) current.gameObject.SetActive(false);
            current = target;
            current.gameObject.SetActive(true);

            Vector3 pos = exact ?? current.GetSpawn(spawnId).position;
            if (exact.HasValue) pos.z = player.position.z;
            player.localScale = Vector3.one * current.actorScale; // collider, speed and reach scale with it
            player.position = pos;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.position = pos; rb.linearVelocity = Vector2.zero; }

            if (cameraFollow != null)
            {
                cameraFollow.useBounds = true;
                cameraFollow.bounds = current.bounds;
            }
            RoomChanged?.Invoke(previous, current);
        }

        private Room peekBack, peekRoom;

        /// <summary>
        /// A short scripted shot: fade, show another room with the camera on someone there (Donnie stays where he is,
        /// hidden and frozen). End it with EndPeek.
        /// </summary>
        public IEnumerator Peek(string roomId, Transform focus)
        {
            var room = Find(roomId);
            if (room == null || current == null) yield break;
            IsTransitioning = true;
            yield return Fade(0f, 1f);
            peekBack = current;
            peekRoom = room;
            current.gameObject.SetActive(false);
            room.gameObject.SetActive(true);
            SetPlayerPresent(false);
            cameraFollow.bounds = room.bounds;
            cameraFollow.target = focus;
            yield return null;
            yield return Fade(1f, 0f);
            IsTransitioning = false;
        }

        public IEnumerator EndPeek(System.Action whileBlack = null)
        {
            if (peekBack == null) yield break;
            IsTransitioning = true;
            yield return Fade(0f, 1f);
            whileBlack?.Invoke();
            foreach (var r in rooms) if (r != peekBack) r.gameObject.SetActive(false);
            peekBack.gameObject.SetActive(true);
            SetPlayerPresent(true);
            cameraFollow.bounds = peekBack.bounds;
            cameraFollow.target = player;
            peekBack = null;
            peekRoom = null;
            yield return null;
            yield return Fade(1f, 0f);
            IsTransitioning = false;
        }

        private void SetPlayerPresent(bool on)
        {
            foreach (var r in player.GetComponentsInChildren<SpriteRenderer>()) r.enabled = on;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.simulated = on;
        }

        private Room Find(string id)
        {
            foreach (var r in rooms) if (r.roomId == id) return r;
            return null;
        }

        private IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                SetFade(Mathf.Lerp(from, to, t / fadeSeconds));
                yield return null;
            }
            SetFade(to);
        }

        private void SetFade(float a)
        {
            var c = fade.color;
            c.a = a;
            fade.color = c;
        }

        private void BuildFade()
        {
            var go = new GameObject("FadeCanvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var imgGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)imgGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            fade = imgGo.GetComponent<Image>();
            fade.color = new Color(0, 0, 0, 0);
            fade.raycastTarget = false;
        }
    }
}

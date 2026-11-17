using System;
using System.Collections.Generic;
using Unlikely.Player;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;

namespace Unlikely.NPC
{
    /// <summary>
    /// An employee who walks a fixed route around their office and keeps an eye out. Their sight is drawn on the
    /// floor as a cone (yellow, turning red as they get suspicious). The rules:
    ///   * Donnie in their sight without his cart nearby: he doesn't belong here, and gets put out into the corridor.
    ///   * With the cart (pushing it, or parked beside him) he's the janitor: allowed in.
    ///   * Touching a computer or a drawer while they're looking: refused ("Can I help you?"), he steps back.
    ///   * Once a computer is open he's safe (CatchAtComputers turns that off, for harder difficulties later).
    /// </summary>
    [RequireComponent(typeof(Npc))]
    public class Guard : MonoBehaviour
    {
        [SerializeField] private Vector3[] route = new Vector3[0];
        [SerializeField] private float pause = 2.2f;          // seconds at each stop
        [SerializeField] private Vector2[] lookAtStop = new Vector2[0];
        [SerializeField] private float[] pauseAtStop = new float[0];   // per-stop pause (0 = default)
        [SerializeField] private float range = 2.6f;          // metres, scaled with the room
        [SerializeField] private float halfAngle = 40f;
        [SerializeField] private float speed = 1.3f;
        [SerializeField] private Vector3 shooTo;               // where Donnie steps back to when refused
        [SerializeField] private string kickRoom;              // where he's put when thrown out (the corridor)...
        [SerializeField] private string kickSpawn;             // ...outside this office's door
        [SerializeField] private string[] caughtLines = { "Hey! Not your desk." };

        private static readonly List<Guard> active = new List<Guard>();

        /// <summary>False (default): an open computer is safe. True: they can catch Donnie while he's using one.</summary>
        public static bool CatchAtComputers = false;

        /// <summary>The cart counts if Donnie is pushing it or it's parked within this many metres of him.</summary>
        private const float CartNearby = 2.4f;

        private static readonly string[] RefuseLines = { "Can I help you?", "That's not yours.", "Hands off, please." };

        /// <summary>Raised when a guard catches Donnie.</summary>
        public static event Action<Guard> CaughtPlayer;

        public static bool AnySeesPlayer
        {
            get { foreach (var g in active) if (g.Sees()) return true; return false; }
        }

        /// <summary>How close anyone is to catching Donnie (0..1). The computer screen shows a warning when this rises.</summary>
        public static float Alarm
        {
            get { float a = 0f; foreach (var g in active) a = Mathf.Max(a, g.suspicion); return a; }
        }

        private Npc npc;
        private SpriteRenderer cone;
        private int leg;
        private float wait, suspicion, cooldown;
        private int lineIndex;
        private bool warned;

        public void Configure(Vector3[] points, Vector2[] lookAtStops, float[] pauses, Vector3 shooPosition, float sightRange, params string[] lines)
        {
            route = points;
            lookAtStop = lookAtStops;
            pauseAtStop = pauses;
            shooTo = shooPosition;
            range = sightRange;
            if (lines != null && lines.Length > 0) caughtLines = lines;
        }

        public void SetCaughtLines(string[] lines) { if (lines != null && lines.Length > 0) caughtLines = lines; }

        public void SetKickOut(string roomId, string spawnId)
        {
            kickRoom = roomId;
            kickSpawn = spawnId;
        }

        private void Awake()
        {
            npc = GetComponent<Npc>();
            var go = new GameObject("SightCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            cone = go.AddComponent<SpriteRenderer>();
            cone.sprite = MakeConeSprite(halfAngle);
            cone.sortingOrder = -9500; // on the floor
        }

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() { active.Remove(this); suspicion = 0f; }

        private void Update()
        {
            DrawCone();
            // the world holds still while a text box, the pause menu or a scene is up: no walking, no noticing
            if (Frozen) { npc.Drive(npc.Facing, false); return; }
            Patrol();
            Watch();
        }

        /// <summary>Dialogue, a choice, a cutscene, the pause menu or the Vibe book is on screen (computers have their own rule).</summary>
        public static bool Frozen =>
            (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) ||
            (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying) ||
            (PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen) ||
            (TheoryBookUI.Instance != null && TheoryBookUI.Instance.IsOpen) ||
            (GameOverUI.Instance != null && GameOverUI.Instance.IsOpen) ||
            (PhoneUI.Instance != null && PhoneUI.Instance.IsOpen);

        private void Patrol()
        {
            if (route.Length == 0 || cooldown > 0f) { npc.Drive(npc.Facing, false); return; }
            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                npc.Drive(leg < lookAtStop.Length && lookAtStop[leg] != Vector2.zero ? lookAtStop[leg] : npc.Facing, false);
                if (wait <= 0f) leg = (leg + 1) % route.Length;
                return;
            }
            Vector3 to = route[leg] - transform.position;
            to.z = 0f;
            float step = speed * transform.localScale.x * Time.deltaTime;
            if (to.magnitude <= step)
            {
                transform.position = new Vector3(route[leg].x, route[leg].y, transform.position.z);
                wait = leg < pauseAtStop.Length && pauseAtStop[leg] > 0f ? pauseAtStop[leg] : pause;
                return;
            }
            Vector2 dir = Mathf.Abs(to.x) > Mathf.Abs(to.y) ? new Vector2(Mathf.Sign(to.x), 0) : new Vector2(0, Mathf.Sign(to.y));
            npc.Drive(dir, true);
            transform.position += to.normalized * step;
        }

        /// <summary>Is Donnie in this person's sight cone right now (ignoring the cart)?</summary>
        public bool InSight()
        {
            var p = PlayerController.Instance;
            if (p == null || !isActiveAndEnabled) return false;
            Vector2 eye = (Vector2)transform.position + Vector2.up * 0.4f * transform.localScale.y;
            Vector2 d = (Vector2)p.transform.position + Vector2.up * 0.4f * p.transform.localScale.y - eye;
            float r = range * transform.localScale.x;
            if (d.magnitude > r) return false;
            return Vector2.Angle(npc.Facing, d) <= halfAngle || d.magnitude < 0.45f * transform.localScale.x;
        }

        /// <summary>Is the cart with Donnie (pushed, or parked right beside him in this room)?</summary>
        public static bool HasCart()
        {
            if (JanitorCart.Holding) return true;
            var cart = JanitorCart.Find();
            var p = PlayerController.Instance;
            return cart != null && p != null && cart.Spawned && cart.gameObject.activeInHierarchy
                   && Vector2.Distance(cart.transform.position, p.transform.position) <= CartNearby * p.transform.localScale.x;
        }

        /// <summary>Would this person throw Donnie out right now? (In sight, without his cart.)</summary>
        public bool Sees() => cooldown <= 0f && !HasCart() && InSight();

        private void Watch()
        {
            if (cooldown > 0f) { cooldown -= Time.deltaTime; suspicion = 0f; return; }
            var p = PlayerController.Instance;
            bool busy = p == null || p.IsAutoWalking || PlayerController.Frozen
                        || (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying)
                        || (RoomManager.Instance != null && RoomManager.Instance.IsTransitioning);
            bool atComputer = TerminalUI.Instance != null && TerminalUI.Instance.IsOpen;
            if (atComputer && !CatchAtComputers) busy = true;   // once a computer is open, he's safe
            if (!busy && (Sees() || (atComputer && InSight())))
            {
                // about two seconds in plain sight before they act (time to grab the cart or leave)
                suspicion += Time.deltaTime * (atComputer ? 0.95f : 0.55f);
                if (!warned && suspicion > 0.2f) { warned = true; npc.Bark("Hm?", 1.2f); }
                if (suspicion >= 1f) Catch();
            }
            else
            {
                suspicion = Mathf.Max(0f, suspicion - Time.deltaTime * 0.8f);
                if (suspicion <= 0f) warned = false;
            }
        }

        /// <summary>Caught: a remark, out of any computer, and Donnie is put out of the office into the corridor.</summary>
        public void Catch()
        {
            suspicion = 0f;
            warned = false;
            cooldown = 3.5f;
            var p = PlayerController.Instance;
            if (p == null) return;
            Vector2 d = p.transform.position - transform.position;
            npc.Drive(Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y)), false);
            if (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen) TerminalUI.Instance.Close();
            npc.Bark(caughtLines[lineIndex++ % caughtLines.Length], 3.2f);
            HudUI.Instance?.Toast("Thrown out. Bring your cart to blend in.");
            if (!string.IsNullOrEmpty(kickRoom) && RoomManager.Instance != null) RoomManager.Instance.GoTo(kickRoom, kickSpawn);
            else p.WalkTo(shooTo, null, 3f);
            CaughtPlayer?.Invoke(this);
        }

        /// <summary>
        /// Donnie reaches for a computer or drawer: if anyone is looking (cart or not), they stop him and he steps back.
        /// Returns true if he was stopped.
        /// </summary>
        public static bool RefuseIfWatched()
        {
            foreach (var g in active)
            {
                if (g.cooldown > 0f || !g.InSight()) continue;
                if (!HasCart()) { g.Catch(); return true; }   // no cart: he shouldn't be in here at all
                g.npc.Bark(RefuseLines[g.lineIndex++ % RefuseLines.Length], 2.5f);
                var p = PlayerController.Instance;
                if (p != null) p.WalkTo(p.transform.position - (Vector3)(p.Facing * 0.6f * p.transform.localScale.x), null, 0.6f);
                return true;
            }
            return false;
        }

        private void DrawCone()
        {
            cone.transform.localScale = new Vector3(range, range, 1f); // a child: already scales with the NPC
            cone.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(npc.Facing.y, npc.Facing.x) * Mathf.Rad2Deg);
            var calm = new Color(1f, 0.85f, 0.35f, 0.16f);
            var alarm = new Color(1f, 0.25f, 0.2f, 0.32f);
            cone.color = cooldown > 0f ? new Color(1f, 0.25f, 0.2f, 0.12f) : Color.Lerp(calm, alarm, suspicion);
        }

        /// <summary>A soft wedge pointing right, apex at the pivot, 1 unit long.</summary>
        private static Sprite MakeConeSprite(float halfAngleDeg)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            float half = halfAngleDeg * Mathf.Deg2Rad;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f, dy = y + 0.5f - size / 2f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / size;
                    float ang = Mathf.Abs(Mathf.Atan2(dy, dx));
                    bool inside = dist <= 1f && ang <= half;
                    float a = inside ? Mathf.Lerp(1f, 0.35f, dist) : 0f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 0.5f), size);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            active.Clear();
            CaughtPlayer = null;
        }
    }
}

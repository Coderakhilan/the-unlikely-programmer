using System;
using System.Collections.Generic;
using Unlikely.Player;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;

namespace Unlikely.NPC
{
    /// <summary>
    /// Someone who comes after Donnie across a corridor map, finding their way round furniture (A* on a NavGrid).
    ///   Hr:       walks his rounds. Sees Donnie without his cart: chases him (slower than Donnie). Catches him: game over.
    ///   Ryker:    in the dark, walks a beat (the right-hand side, the long hallway, the top). The moment he sees
    ///             Donnie he runs at him (faster than Donnie). A torch flash he can see (he's on screen) draws him to
    ///             that spot; he looks around, and goes back to his beat unless there's another flash.
    ///   Security: in the dark, walks rounds with a torch. Sees Donnie: comes after him for a bit (slower than Donnie).
    ///             A torch flash he can see sends him off to check that spot, then back to his rounds.
    ///   Liam:     coffee in his face, so he just comes straight at Donnie wherever he is (a little slower than him).
    ///             Only a hiding place shakes him off.
    /// The CEO (Floor 3's blackout) uses the Ryker mind with his own taunts.
    /// </summary>
    [RequireComponent(typeof(Npc))]
    public class Hunter : MonoBehaviour
    {
        public enum Mode { Hr, Ryker, Security, Liam }

        [SerializeField] private Mode mode;
        [SerializeField] private Vector3[] route = new Vector3[0];
        [SerializeField] private float[] stopWaits = new float[0];   // seconds at each route stop (default 1.2)
        [SerializeField] private float walkSpeed = 1.5f;     // m/s
        [SerializeField] private float chaseSpeed = 3.2f;    // m/s (Donnie walks at 4)
        [SerializeField] private float sightRange = 4.5f;
        [SerializeField] private float halfAngle = 50f;
        [SerializeField] private float catchDistance = 0.55f;
        [SerializeField] private string[] spotLines = { "Hey! You!" };
        [SerializeField] private Material unlit;   // so the torch cone shows in the dark
        [SerializeField] private string[] taunts;   // Ryker mind: muttered on his beat (defaults to Ryker's)
        [SerializeField] private string lureLine;   // said when a torch flash draws him

        /// <summary>Raised when this hunter gets hold of Donnie.</summary>
        public static event Action<Hunter> Caught;

        public Mode Kind => mode;
        /// <summary>The beat this person walks (world positions).</summary>
        public Vector3[] Route => route;
        public bool Chasing => state == State.Chase;
        public bool Investigating => state == State.Investigate;
        public Vector3 LurePoint { get; private set; }
        public float SinceLure { get; private set; } = 99f;

        private enum State { Route, Chase, Investigate }
        private State state;
        private Npc npc;
        private NavGrid grid;
        private List<Vector3> path = new List<Vector3>();
        private Vector3 target, home;
        private int leg;
        private float repath, lostFor, wait, barkCool, chaseFor;
        private SpriteRenderer cone;
        [SerializeField] private Vector3 startPos;   // recorded when the scene is built (the room may be hidden at runtime)

        public void Configure(Mode kind, Vector3[] points, float walk, float chase, float range, params string[] lines)
        {
            mode = kind;
            route = points;
            startPos = transform.position;
            walkSpeed = walk;
            chaseSpeed = chase;
            sightRange = range;
            if (lines != null && lines.Length > 0) spotLines = lines;
        }

        public void SetUnlit(Material m) => unlit = m;

        public void SetStopWaits(params float[] waits) => stopWaits = waits;

        public void SetSpotLines(string[] lines) { if (lines != null && lines.Length > 0) spotLines = lines; }

        public void SetTaunts(string[] lines, string lure = null)
        {
            if (lines != null && lines.Length > 0) taunts = lines;
            if (!string.IsNullOrEmpty(lure)) lureLine = lure;
        }

        private void Awake()
        {
            npc = GetComponent<Npc>();
            var go = new GameObject("SightCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            cone = go.AddComponent<SpriteRenderer>();
            cone.sprite = MakeCone(halfAngle);
            cone.sortingOrder = -9500;
            if (unlit != null) cone.sharedMaterial = unlit;
            cone.enabled = enabled;
        }

        private void OnEnable()
        {
            if (cone != null) cone.enabled = true;
            Darkness.Flashed += OnFlash;
            HideSpot.Entered += OnHide;
            rejoin = true;   // first Update: Donnie may have just stepped back out of a room
        }

        private void OnDisable()
        {
            if (cone != null) cone.enabled = false;   // off duty: no sight cone on the floor
            Darkness.Flashed -= OnFlash;
            HideSpot.Entered -= OnHide;
            wasHunting = state != State.Route;
        }

        // While Donnie is inside a room the corridor (and everyone in it) is switched off, so a chaser would freeze
        // at the door forever and catch him the moment he stepped out, however long he waited.
        private bool rejoin, wasHunting;

        /// <summary>
        /// Back from a room: a chaser has lost him (he hid), and anyone standing at the door starts again from the far
        /// end of his round instead of catching Donnie the instant he steps out.
        /// </summary>
        private void Rejoin()
        {
            rejoin = false;
            var p = PlayerController.Instance;
            if (p == null) return;
            Vector3 pp = p.transform.position;
            float d = Vector2.Distance(transform.position, pp);
            float near = Camera.main != null ? Camera.main.orthographicSize : 8f;   // half a screen
            bool tooClose = d < (wasHunting ? near : catchDistance * transform.localScale.x * 4f);
            if (tooClose && route != null && route.Length > 0)
            {
                Vector3 far = route[0];
                foreach (var r in route) if (Vector2.Distance(r, pp) > Vector2.Distance(far, pp)) far = r;
                ResetTo(grid != null ? grid.Nearest(far) : far);
            }
            else if (wasHunting) ResetTo(transform.position);   // lost him: back to his rounds from where he stands
            wasHunting = false;
        }

        /// <summary>Back to the start, calm (after a retry).</summary>
        public void ResetTo(Vector3 position)
        {
            transform.position = position;
            state = State.Route;
            path.Clear();
            leg = 0;
            stuck = 0f;
            wait = mode == Mode.Ryker ? 2.5f : mode == Mode.Liam ? 1.6f : 0f;   // a moment's head start
            lostFor = chaseFor = 0f;
            SinceLure = 99f;
        }

        public void ResetToStart() => ResetTo(startPos);

        private bool Paused
        {
            get
            {
                var rm = RoomManager.Instance;
                return UiKit.AnyModalOpen || (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)
                       || (rm != null && rm.IsTransitioning) || PlayerController.Instance == null
                       || (PlayerController.Instance.IsAutoWalking);
            }
        }

        private void Update()
        {
            if (grid == null)
            {
                grid = NavGrid.For(GetComponentInParent<Room>());
                // route stops that land on furniture would never be reached: move them onto the floor
                if (grid != null) for (int i = 0; i < route.Length; i++) route[i] = grid.Nearest(route[i]);
            }
            DrawCone();
            if (rejoin && !Paused && PlayerController.Instance != null) Rejoin();
            if (Paused || grid == null) { npc.Drive(npc.Facing, false); return; }
            SinceLure += Time.deltaTime;
            barkCool -= Time.deltaTime;

            var p = PlayerController.Instance;
            Vector3 pp = p.transform.position;
            bool sees = CanSee();

            switch (mode)
            {
                case Mode.Hr: ThinkHr(sees, pp); break;
                case Mode.Security: ThinkSecurity(sees, pp); break;
                case Mode.Liam: ThinkLiam(pp); break;
                default: ThinkRyker(sees, pp); break;
            }

            // caught?
            if (!HideSpot.IsHidden && Vector2.Distance(transform.position, pp) <= catchDistance * transform.localScale.x
                && (mode != Mode.Hr || !Guard.HasCart()))
            {
                npc.Drive(npc.Facing, false);
                Caught?.Invoke(this);
            }
        }

        // ------------------------------------------------------------------ the three minds

        private void ThinkHr(bool sees, Vector3 pp)
        {
            if (sees && !Guard.HasCart())
            {
                if (state != State.Chase) Bark();
                state = State.Chase;
                lostFor = 0f;
                target = pp;
            }
            else if (state == State.Chase)
            {
                lostFor += Time.deltaTime;
                if (lostFor > 2.5f || Guard.HasCart()) { state = State.Route; path.Clear(); }
            }
            if (state == State.Chase) MoveTo(target, chaseSpeed);
            else Rounds();
        }

        private void ThinkSecurity(bool sees, Vector3 pp)
        {
            if (sees)
            {
                if (state != State.Chase) { Bark(); chaseFor = 0f; }
                state = State.Chase;
                lostFor = 0f;
                target = pp;
            }
            if (state == State.Chase)
            {
                chaseFor += Time.deltaTime;
                if (!sees) lostFor += Time.deltaTime;
                if (lostFor > 1.5f || chaseFor > 6f) { state = State.Route; path.Clear(); }
                else MoveTo(target, chaseSpeed);
                return;
            }
            if (state == State.Investigate) { Investigate(); return; }
            Rounds();
        }

        private void ThinkLiam(Vector3 pp)
        {
            if (wait > 0f) { wait -= Time.deltaTime; npc.Drive(npc.Facing, false); return; }   // still wiping his face
            if (HideSpot.IsHidden) { LookAround(); return; }
            if (state != State.Chase) { Bark(); state = State.Chase; }
            MoveTo(pp, chaseSpeed);
        }

        private void ThinkRyker(bool sees, Vector3 pp)
        {
            if (sees)
            {
                if (state != State.Chase) Bark();
                state = State.Chase;
                target = pp;
                lostFor = 0f;
            }
            if (state == State.Chase)
            {
                if (!sees) lostFor += Time.deltaTime;
                if (lostFor > 1.2f)
                {
                    // lost him: check where he was last seen, then back to the beat
                    state = State.Investigate;
                    wait = 3f;
                    path.Clear();
                }
                else { MoveTo(target, chaseSpeed); return; }
            }
            if (state == State.Investigate) { Investigate(); return; }
            var lines = taunts != null && taunts.Length > 0 ? taunts : RykerTaunts;
            if (barkCool <= 0f && UnityEngine.Random.value < 0.003f) { npc.Bark(lines[UnityEngine.Random.Range(0, lines.Length)], 2.5f); barkCool = 8f; }
            Rounds();
        }

        private static readonly string[] RykerTaunts =
        {
            "Donnie! Come on, mate. Let's talk.", "I can hear you breathing, Donnie.", "Nobody's coming. They all ran.",
            "You mop like a man with secrets.", "Lights out, Donnie. Your idea, remember?"
        };

        // ------------------------------------------------------------------ shared moves

        private void Rounds()
        {
            if (route.Length == 0) { npc.Drive(npc.Facing, false); return; }
            if (wait > 0f) { wait -= Time.deltaTime; LookAround(); return; }
            if (Vector2.Distance(transform.position, route[leg]) < 0.25f || stuck > 2f)
            {
                wait = leg < stopWaits.Length && stopWaits[leg] > 0f ? stopWaits[leg] : 1.2f;
                leg = (leg + 1) % route.Length;
                stuck = 0f;
                path.Clear();
                return;
            }
            MoveTo(route[leg], walkSpeed);
        }

        private void Investigate()
        {
            if (Vector2.Distance(transform.position, target) > 0.3f) { MoveTo(target, mode == Mode.Ryker ? chaseSpeed * 0.9f : walkSpeed * 1.4f); return; }
            wait -= Time.deltaTime;
            LookAround();
            if (wait <= 0f) { state = State.Route; path.Clear(); }
        }

        private void OnFlash(Vector3 point)
        {
            if (!isActiveAndEnabled || mode == Mode.Hr || mode == Mode.Liam || grid == null) return;
            if (state == State.Chase && CanSee()) return;   // he's got eyes on Donnie; a torch won't distract him
            if (!OnScreen(transform.position)) return;       // too far away to notice it
            LurePoint = grid.Nearest(point);
            SinceLure = 0f;
            target = LurePoint;
            state = State.Investigate;
            wait = mode == Mode.Ryker ? 4f : 3f;   // stands there looking around: time for the next flash
            path.Clear();
            if (barkCool <= 0f) { npc.Bark(!string.IsNullOrEmpty(lureLine) ? lureLine : mode == Mode.Ryker ? "There you are..." : "Who's there?", 1.8f); barkCool = 3f; }
        }

        private void OnHide(HideSpot spot)
        {
            // hiding right under his nose doesn't work
            if (!isActiveAndEnabled || mode == Mode.Hr) return;
            if (CanSeePoint(spot.transform.position, 0.6f) && Vector2.Distance(transform.position, spot.transform.position) < 3.2f * transform.localScale.x)
            {
                npc.Bark(mode == Mode.Ryker ? "Saw that, Donnie." : "Out of there. Now.", 2f);
                spot.ComeOut();
                Caught?.Invoke(this);
            }
        }

        private void MoveTo(Vector3 to, float speed)
        {
            repath -= Time.deltaTime;
            if (path.Count == 0 || repath <= 0f || Vector2.Distance(path[path.Count - 1], to) > 0.6f)
            {
                path = grid.Path(transform.position, to);
                repath = 0.5f;
            }
            if (path.Count == 0) { stuck += Time.deltaTime; npc.Drive(npc.Facing, false); return; }
            stuck = 0f;
            Vector3 next = path[0];
            Vector3 d = next - transform.position;
            d.z = 0f;
            float step = speed * transform.localScale.x * Time.deltaTime;
            if (d.magnitude <= step) { transform.position = new Vector3(next.x, next.y, transform.position.z); path.RemoveAt(0); }
            else transform.position += d.normalized * step;
            if (d.sqrMagnitude > 0.0001f)
                npc.Drive(Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y)), true);
        }

        private float stuck;   // seconds with no way to the current stop (then skip it)
        private float lookClock;
        private void LookAround()
        {
            lookClock += Time.deltaTime;
            if (lookClock > 0.8f)
            {
                lookClock = 0f;
                var dirs = new[] { Vector2.left, Vector2.up, Vector2.right, Vector2.down };
                npc.Drive(dirs[UnityEngine.Random.Range(0, 4)], false);
            }
            else npc.Drive(npc.Facing, false);
        }

        private void Bark()
        {
            if (barkCool > 0f) return;
            npc.Bark(spotLines[UnityEngine.Random.Range(0, spotLines.Length)], 2.2f);
            barkCool = 3f;
        }

        // ------------------------------------------------------------------ eyes

        /// <summary>Is this point on the player's screen (so whoever stands there could see the torch)?</summary>
        public static bool OnScreen(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null) return true;
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            Vector3 c = cam.transform.position;
            return Mathf.Abs(p.x - c.x) <= halfW && Mathf.Abs(p.y - c.y) <= halfH;
        }

        public bool CanSee()
        {
            if (HideSpot.IsHidden) return false;
            var p = PlayerController.Instance;
            return p != null && CanSeePoint(p.transform.position, 1.2f);
        }

        private bool CanSeePoint(Vector3 point, float closeAnyway)
        {
            float s = transform.localScale.x;
            Vector2 d = point - transform.position;
            float r = sightRange * s * (Darkness.IsDark && mode == Mode.Hr ? 0.5f : 1f);
            if (d.magnitude > r) return false;
            if (d.magnitude > closeAnyway * s && Vector2.Angle(npc.Facing, d) > halfAngle) return false;
            return grid == null || grid.Clear(grid.Nearest(transform.position), grid.Nearest(point));
        }

        private void DrawCone()
        {
            float r = sightRange * (Darkness.IsDark && mode == Mode.Hr ? 0.5f : 1f);
            cone.transform.localScale = new Vector3(r, r, 1f);
            cone.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(npc.Facing.y, npc.Facing.x) * Mathf.Rad2Deg);
            // in the dark the cone is their torch beam, so it stays visible
            cone.color = state == State.Chase ? new Color(1f, 0.3f, 0.2f, 0.32f)
                : Darkness.IsDark ? new Color(1f, 0.95f, 0.7f, 0.22f) : new Color(1f, 0.85f, 0.35f, 0.14f);
        }

        /// <summary>The sight-cone sprite (pivot at the eye, 1 unit long), for people the story moves by hand.</summary>
        public static Sprite ConeSprite(float halfAngleDeg) => MakeCone(halfAngleDeg);

        private static Sprite MakeCone(float halfAngleDeg)
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
                    float a = dist <= 1f && ang <= half ? Mathf.Lerp(1f, 0.3f, dist) : 0f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 0.5f), size);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Caught = null;
    }
}

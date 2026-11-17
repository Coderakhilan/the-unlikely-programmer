using Unlikely.Interaction;
using Unlikely.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unlikely.Player
{
    /// <summary>
    /// Top-down movement + interaction for Donnie. Input is defined in code (keyboard + gamepad)
    /// so it stays platform-neutral and needs no input asset.
    /// 1 world unit = 1 tile (16 px).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour, IWalker
    {
        public static PlayerController Instance { get; private set; }

        /// <summary>Story scenes can freeze Donnie (e.g. while he is being marched away).</summary>
        public static bool Frozen;

        /// <summary>Speed multiplier (dragging the cart is a bit slower).</summary>
        public float SpeedFactor { get; set; } = 1f;

        [SerializeField] private float moveSpeed = 4f;          // tiles per second
        [SerializeField] private float interactReach = 0.7f;    // how far in front of the player we look
        [SerializeField] private float interactRadius = 0.5f;

        public Vector2 Facing { get; private set; } = Vector2.down;
        public bool IsMoving => moveInput.sqrMagnitude > 0.01f;

        private Rigidbody2D rb;
        private InputAction moveAction;
        private InputAction interactAction;
        private Vector2 moveInput;
        private Interactable currentTarget;
        private static readonly Collider2D[] hitBuffer = new Collider2D[16];
        private Transform marker;          // the little arrow over whatever E would use
        private Vector3 lastStepAt;
        private float stepDistance;
        private bool stepLeft;

        private void Awake()
        {
            Instance = this;
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/a").With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d").With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");

            interactAction = new InputAction("Interact", InputActionType.Button);
            interactAction.AddBinding("<Keyboard>/e");
            interactAction.AddBinding("<Keyboard>/space");
            interactAction.AddBinding("<Gamepad>/buttonSouth");
        }

        private void OnEnable()
        {
            moveAction.Enable();
            interactAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable();
            interactAction.Disable();
        }

        private void Update()
        {
            var ui = DialogueUI.Instance;
            bool dialogueOpen = ui != null && ui.IsOpen;

            // Frozen while the screen fades between rooms.
            if (Unlikely.World.RoomManager.Instance != null && Unlikely.World.RoomManager.Instance.IsTransitioning)
            {
                moveInput = Vector2.zero;
                return;
            }

            // While a computer, the phone, the elevator panel or a cutscene is open, that screen owns the keyboard.
            if (UiKit.AnyModalOpen)
            {
                moveInput = Vector2.zero;
                if (ui != null) ui.SetPrompt(null);
                return;
            }

            if (walkTarget.HasValue && !dialogueOpen)
            {
                // scripted walk (e.g. being shooed away from the front desk)
                Vector2 to = walkTarget.Value - (Vector2)transform.position;
                walkTime -= Time.deltaTime;
                if (to.magnitude < 0.12f * transform.localScale.x || walkTime <= 0f)
                {
                    walkTarget = null;
                    moveInput = Vector2.zero;
                    var done = walkDone;
                    walkDone = null;
                    done?.Invoke();
                }
                else moveInput = to.normalized * 0.8f;
            }
            else moveInput = dialogueOpen || Frozen ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

            if (moveInput.sqrMagnitude > 0.01f)
            {
                // Snap facing to the dominant axis so interaction is predictable.
                Facing = Mathf.Abs(moveInput.x) >= Mathf.Abs(moveInput.y)
                    ? new Vector2(Mathf.Sign(moveInput.x), 0f)
                    : new Vector2(0f, Mathf.Sign(moveInput.y));
            }

            if (dialogueOpen)
            {
                if (interactAction.WasPressedThisFrame()) ui.Advance();
                return;
            }

            if (Frozen || walkTarget.HasValue) { if (ui != null) ui.SetPrompt(null); return; }

            currentTarget = FindTarget();
            ShowMarker(currentTarget);
            if (ui != null) ui.SetPrompt(currentTarget != null ? currentTarget.Prompt : null, currentTarget != null ? currentTarget.Key : "E");

            if (currentTarget != null && interactAction.WasPressedThisFrame())
                currentTarget.Interact(this);
        }

        private Vector2? walkTarget;
        private float walkTime;
        private System.Action walkDone;

        public bool IsAutoWalking => walkTarget.HasValue;

        /// <summary>Walk Donnie to a point by himself (player input ignored), then call done.</summary>
        public void WalkTo(Vector3 target, System.Action done = null, float timeout = 4f)
        {
            walkTarget = target;
            walkTime = timeout;
            walkDone = done;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Frozen = false;

        private void FixedUpdate()
        {
            // Scaled up inside enlarged rooms: move proportionally faster so walking feels the same.
            Vector2 want = moveInput * moveSpeed * SpeedFactor * transform.localScale.x;
            // a touch of weight: up to speed in about 0.07 s, stopped in about 0.05 s (not instant, never slidey)
            float full = moveSpeed * transform.localScale.x;
            float rate = (want.sqrMagnitude > 0.01f ? full / 0.07f : full / 0.05f) * Time.fixedDeltaTime;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, want, rate);
        }

        private void LateUpdate()
        {
            // footsteps: one soft tap per stride actually walked (not while walking into a wall)
            float s = transform.localScale.x;
            float moved = Vector2.Distance(transform.position, lastStepAt);
            lastStepAt = transform.position;
            if (moved > 2f * s) stepDistance = 0f;   // a room change, not a step
            else stepDistance += moved / Mathf.Max(0.01f, s);
            if (stepDistance >= 0.62f)
            {
                stepDistance = 0f;
                stepLeft = !stepLeft;
                Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Step, 0.22f, stepLeft ? 0.92f : 1.06f);
            }
            if (marker != null && marker.gameObject.activeSelf && (currentTarget == null || UiKit.AnyModalOpen || Frozen
                || (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)))
                marker.gameObject.SetActive(false);
        }

        /// <summary>A small lime arrow bobbing over the thing E would use right now.</summary>
        private void ShowMarker(Interactable target)
        {
            if (target == null || string.IsNullOrEmpty(target.Prompt))
            {
                if (marker != null) marker.gameObject.SetActive(false);
                return;
            }
            if (marker == null)
            {
                var go = new GameObject("InteractMarker");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = MarkerSprite();
                sr.color = UiKit.Lime;
                sr.sortingOrder = 30000;
                marker = go.transform;
            }
            var col = target.GetComponentInChildren<Collider2D>();
            Bounds b = col != null ? col.bounds : new Bounds(target.transform.position, Vector3.one * 0.5f);
            float s = transform.localScale.x;
            float bob = Mathf.Sin(Time.unscaledTime * 5f) * 0.05f * s;
            marker.position = new Vector3(b.center.x, b.max.y + 0.28f * s + bob, 0f);
            marker.localScale = Vector3.one * s;
            marker.gameObject.SetActive(true);
        }

        private static Sprite markerSprite;
        private static Sprite MarkerSprite()
        {
            if (markerSprite != null) return markerSprite;
            // a small downward arrow with a dark outline
            const int w = 14, h = 10;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float half = (y - 1) * 0.75f;                      // widens towards the top
                    float d = Mathf.Abs(x - (w - 1) / 2f);
                    bool inside = y >= 1 && y <= h - 2 && d <= half;
                    bool edge = !inside && d <= half + 1.2f;
                    px[y * w + x] = inside ? Color.white : edge ? new Color(0.05f, 0.08f, 0.03f, 0.9f) : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply();
            markerSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 48f);
            return markerSprite;
        }

        private Interactable FindTarget()
        {
            float s = transform.localScale.x;
            Vector2 center = (Vector2)transform.position + Vector2.up * 0.3f * s + Facing * interactReach * s;
            // (a reused buffer: this runs every frame, and a fresh array each time is garbage for no reason)
            int count = Physics2D.OverlapCircle(center, interactRadius * s, new ContactFilter2D().NoFilter(), hitBuffer);
            Interactable best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var interactable = hit.GetComponentInParent<Interactable>();
                if (interactable == null) continue;
                float d = ((Vector2)hit.transform.position - center).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = interactable; }
            }
            return best;
        }
    }
}

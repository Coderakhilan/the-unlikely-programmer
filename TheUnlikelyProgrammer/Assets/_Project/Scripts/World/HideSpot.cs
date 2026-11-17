using System;
using System.Collections.Generic;
using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unlikely.World
{
    /// <summary>
    /// Somewhere to hide: a locker (no sign he's in there) or a big pot plant (his eyes peek out between the leaves).
    /// Only usable while the story allows hiding (<see cref="Enabled"/>). E (or moving) to come out.
    /// </summary>
    public class HideSpot : Interactable
    {
        public enum Kind { Locker, Plant }

        [SerializeField] private Kind kind;
        [SerializeField] private string id;
        [SerializeField] private GameObject eyes;   // plants only

        private static readonly List<HideSpot> all = new List<HideSpot>();
        private static bool enabledAll;

        /// <summary>The spot Donnie is hiding in, or null.</summary>
        public static HideSpot Current { get; private set; }
        public static bool IsHidden => Current != null;
        public static event Action<HideSpot> Entered, Left;

        public string Id => id;
        public Kind Type => kind;

        public static bool Enabled
        {
            get => enabledAll;
            set
            {
                enabledAll = value;
                foreach (var h in all) if (h != null) foreach (var c in h.GetComponents<Collider2D>()) c.enabled = value;
                if (!value && Current != null) Current.ComeOut();
            }
        }

        public void Configure(string spotId, Kind spotKind, GameObject eyesObject)
        {
            id = spotId;
            kind = spotKind;
            eyes = eyesObject;
        }

        public override string Prompt => kind == Kind.Locker ? "Hide in locker" : "Hide in plant";

        private float enteredAt;

        private void Awake()
        {
            all.Add(this);
            foreach (var c in GetComponents<Collider2D>()) c.enabled = enabledAll;
            if (eyes != null) eyes.SetActive(false);
        }

        private void OnDestroy() => all.Remove(this);

        public override void Interact(PlayerController player)
        {
            if (Current != null || !enabledAll) return;
            if (World.JanitorCart.Holding) { DialogueUI.Instance.Show("", new[] { "Not with the cart. (Q to let go.)" }); return; }
            Current = this;
            enteredAt = Time.time;
            PlayerController.Frozen = true;
            SetPlayerVisible(player, false);
            if (eyes != null) eyes.SetActive(true);
            Entered?.Invoke(this);
        }

        public void ComeOut()
        {
            if (Current != this) return;
            Current = null;
            PlayerController.Frozen = false;
            var p = PlayerController.Instance;
            if (p != null) SetPlayerVisible(p, true);
            if (eyes != null) eyes.SetActive(false);
            Left?.Invoke(this);
        }

        private void Update()
        {
            if (Current != this || Time.time - enteredAt < 0.3f || UiKit.AnyModalOpen) return;
            if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool e = (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (e) ComeOut();
        }

        private static void SetPlayerVisible(PlayerController p, bool on)
        {
            foreach (var r in p.GetComponentsInChildren<SpriteRenderer>()) r.enabled = on;
        }

        /// <summary>Little eyes peeking out of a plant (generated, so no art is needed).</summary>
        public static Sprite MakeEyesSprite()
        {
            const int w = 14, h = 5;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);
            void Eye(int ox)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < 5; x++)
                    {
                        bool corner = (x == 0 || x == 4) && (y == 0 || y == h - 1);
                        if (!corner) tex.SetPixel(ox + x, y, new Color(0.95f, 0.95f, 0.9f));
                    }
                tex.SetPixel(ox + 2, 2, Color.black);
                tex.SetPixel(ox + 2, 1, Color.black);
                tex.SetPixel(ox + 3, 2, Color.black);
            }
            Eye(1);
            Eye(8);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            all.Clear();
            enabledAll = false;
            Current = null;
            Entered = null;
            Left = null;
        }
    }
}

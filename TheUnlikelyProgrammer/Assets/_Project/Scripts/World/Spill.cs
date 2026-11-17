using System;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unlikely.World
{
    /// <summary>A mess on the floor. With the mop in hand, hold F (gamepad: Y/Triangle) next to it to clean it.</summary>
    public class Spill : Interactable
    {
        private const float Reach = 1.2f;
        private const float SecondsToClean = 2.5f;

        [SerializeField] private string id;
        public string Id => id;
        public bool Clean { get; private set; }
        public event Action Cleaned;

        private float progress;
        private SpriteRenderer sr;

        // the mop lives on the cart: Donnie can only mop while he's pushing it
        public override string Prompt => JanitorCart.Holding ? "Hold to mop" : "Bring your cart (Q) to mop";
        public override string Key => JanitorCart.Holding ? "F" : "E";

        public void Configure(string spillId) => id = spillId;

        private void Awake()
        {
            sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite == null) sr.sprite = MakePuddleSprite();
        }

        public override void Interact(PlayerController player)
        {
            if (!JanitorCart.Holding) DialogueUI.Instance.Show("", new[] { "My mop's on the cart. I need the cart here first. (Q to push it.)" });
            else DialogueUI.Instance.Show("", new[] { "Hold F to mop." });
        }

        private void Update()
        {
            if (Clean) return;
            var p = PlayerController.Instance;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool held = (kb != null && kb.fKey.isPressed) || (pad != null && pad.buttonNorth.isPressed);
            bool near = p != null && Vector2.Distance(p.transform.position, transform.position) <= Reach * p.transform.localScale.x;
            bool working = held && near && JanitorCart.Holding && !UiKit.AnyModalOpen && !PlayerController.Frozen
                           && !(DialogueUI.Instance != null && DialogueUI.Instance.IsOpen);

            if (working)
            {
                progress += Time.deltaTime / SecondsToClean;
                HudUI.Instance?.ShowProgress("Mopping", progress);
                if (sr != null) { var c = sr.color; c.a = Mathf.Lerp(0.85f, 0.1f, progress); sr.color = c; }
                if (progress >= 1f)
                {
                    Clean = true;
                    HudUI.Instance?.ShowProgress(null, 0);
                    Cleaned?.Invoke();
                    gameObject.SetActive(false);
                }
            }
            else if (near && progress > 0f)
            {
                HudUI.Instance?.ShowProgress(null, 0);
            }
        }

        /// <summary>Tests: clean it instantly.</summary>
        public void DevClean()
        {
            Clean = true;
            Cleaned?.Invoke();
            gameObject.SetActive(false);
        }

        /// <summary>A small generated puddle sprite (no art needed).</summary>
        public static Sprite MakePuddleSprite()
        {
            const int w = 56, h = 26;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            var body = new Color(0.36f, 0.30f, 0.20f, 0.85f);   // coffee
            var shine = new Color(0.62f, 0.55f, 0.42f, 0.9f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - w / 2f) / (w / 2f), dy = (y - h / 2f) / (h / 2f);
                    float wobble = 0.12f * Mathf.Sin(x * 0.7f) + 0.1f * Mathf.Cos(y * 1.3f);
                    float d = dx * dx + dy * dy;
                    Color c = d < 0.8f + wobble ? body : clear;
                    if (c.a > 0 && dx < -0.2f && dx > -0.5f && dy > 0.1f && dy < 0.35f) c = shine;
                    tex.SetPixel(x, y, c);
                }
            // a couple of drops
            tex.SetPixel(w - 4, h / 2, body); tex.SetPixel(w - 3, h / 2, body); tex.SetPixel(3, 4, body);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 64f);
        }
    }
}

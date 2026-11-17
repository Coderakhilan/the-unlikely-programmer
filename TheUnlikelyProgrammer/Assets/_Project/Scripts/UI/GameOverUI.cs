using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>"CAUGHT." A full-screen card with who got him and a retry (E / Enter / gamepad A).</summary>
    public class GameOverUI : MonoBehaviour
    {
        public static GameOverUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        private GameObject root;
        private Text title, line, hint;
        private Action retry;
        private float openedAt;

        private void Awake()
        {
            Instance = this;
            var canvas = UiKit.MakeCanvas(transform, "GameOverCanvas", 400);
            var bg = UiKit.Box("GameOver", canvas.transform, new Color(0.03f, 0.02f, 0.02f, 0.93f), Vector2.zero, Vector2.one);
            root = bg.gameObject;
            title = UiKit.Label("Title", bg.transform, UiKit.UiFont, 54, new Color(0.9f, 0.25f, 0.2f), new Vector2(0, 0.55f), new Vector2(1, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold);
            line = UiKit.Label("Line", bg.transform, UiKit.UiFont, 20, UiKit.Paper, new Vector2(0.15f, 0.4f), new Vector2(0.85f, 0.55f), TextAnchor.MiddleCenter, FontStyle.Italic);
            hint = UiKit.Label("Hint", bg.transform, UiKit.UiFont, 15, UiKit.Lime, new Vector2(0, 0.22f), new Vector2(1, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold);
            hint.text = "Press E to try again";
            root.SetActive(false);
        }

        public void Show(string heading, string text, Action onRetry)
        {
            title.text = heading;
            line.text = text;
            retry = onRetry;
            openedAt = Time.unscaledTime;
            Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Caught);
            Unlikely.Core.CameraFollow.Shake(0.35f, 0.18f);
            root.SetActive(true);
        }

        /// <summary>Close and run the retry (tests call this directly).</summary>
        public void Retry()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            var r = retry;
            retry = null;
            r?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen) return;
            // the heading lands with a little punch
            float pop = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.22f);
            title.transform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, 1f - (1f - pop) * (1f - pop));
            hint.color = new Color(hint.color.r, hint.color.g, hint.color.b, Time.unscaledTime - openedAt < 0.8f ? 0f : 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f));
            if (Time.unscaledTime - openedAt < 0.8f) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if ((kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (gp != null && gp.buttonSouth.wasPressedThisFrame))
                Retry();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Speech bubbles that pop up over a character's head without stopping the game
    /// (NPC chatter, Dan teaching Ryker in the background). One bubble per speaker at a time.
    /// </summary>
    public class BarkUI : MonoBehaviour
    {
        public static BarkUI Instance { get; private set; }

        private sealed class Bubble
        {
            public Transform target;
            public float height, until;
            public GameObject go;
            public RectTransform rect;
            public Text text;
        }

        private RectTransform layer;
        private Canvas canvas;
        private readonly List<Bubble> bubbles = new List<Bubble>();

        private void Awake()
        {
            Instance = this;
            canvas = UiKit.MakeCanvas(transform, "BarkCanvas", 90);
            var go = new GameObject("Bubbles", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            layer = UiKit.Rect(go, Vector2.zero, Vector2.one);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Show text over target. height = world units above the target's feet (scaled by its size).</summary>
        public void Show(Transform target, string text, float seconds = 3f, float height = 2.0f)
        {
            if (target == null || string.IsNullOrEmpty(text)) return;
            var b = bubbles.Find(x => x.target == target) ?? Create(target);
            b.height = height;
            b.until = Time.time + seconds;
            b.text.text = text;
            b.go.SetActive(true);
        }

        public void Hide(Transform target)
        {
            var b = bubbles.Find(x => x.target == target);
            if (b != null) b.go.SetActive(false);
        }

        public void HideAll()
        {
            foreach (var b in bubbles) b.go.SetActive(false);
        }

        private Bubble Create(Transform target)
        {
            var outer = UiKit.RoundBox("Bubble", layer, UiKit.Edge, new Vector2(0, 0), new Vector2(0, 0), default, default, 2f);
            var fill = UiKit.RoundBox("Fill", outer.transform, new Color32(0xF4, 0xF2, 0xE8, 0xF8), Vector2.zero, Vector2.one, new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f), 2f);
            var text = UiKit.Label("Text", fill.transform, UiKit.UiFont, 13, new Color32(0x22, 0x24, 0x2B, 0xFF), Vector2.zero, Vector2.one,
                TextAnchor.MiddleCenter, FontStyle.Normal, new Vector2(8, 4), new Vector2(-8, -4));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Box("Tail", outer.transform, UiKit.Edge, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-3, -6), new Vector2(3, 0));
            var b = new Bubble { target = target, go = outer.gameObject, rect = outer.rectTransform, text = text };
            b.rect.pivot = new Vector2(0.5f, 0f);
            bubbles.Add(b);
            return b;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float scale = canvas.transform.localScale.x;
            foreach (var b in bubbles)
            {
                if (!b.go.activeSelf) continue;
                if (b.target == null || !b.target.gameObject.activeInHierarchy || Time.time > b.until) { b.go.SetActive(false); continue; }

                // size the bubble to the text (max 220 px wide)
                float w = Mathf.Min(220f, b.text.preferredWidth + 18f);
                b.rect.sizeDelta = new Vector2(w, 40f);
                b.rect.sizeDelta = new Vector2(w, b.text.preferredHeight + 11f);

                Vector3 world = b.target.position + Vector3.up * b.height * b.target.lossyScale.y;
                Vector3 screen = cam.WorldToScreenPoint(world);
                b.rect.anchoredPosition = new Vector2(screen.x / scale, screen.y / scale);
            }
        }
    }
}

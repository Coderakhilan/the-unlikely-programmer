using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Small helpers for building uGUI screens in code. Every screen uses a 960x540 reference canvas
    /// (2x on a 1080p display) so sizes below are in "game pixels".
    /// </summary>
    public static class UiKit
    {
        // Shared palette: dry office colours, brass accents.
        public static readonly Color Ink = new Color32(0x14, 0x15, 0x19, 0xF2);
        public static readonly Color InkLight = new Color32(0x22, 0x24, 0x2B, 0xFF);
        public static readonly Color Brass = new Color32(0xB0, 0x8D, 0x57, 0xFF);
        public static readonly Color Paper = new Color32(0xEC, 0xE6, 0xD8, 0xFF);
        public static readonly Color Muted = new Color32(0x9A, 0x96, 0x8C, 0xFF);
        public static readonly Color Amber = new Color32(0xFF, 0xC8, 0x5A, 0xFF);

        // The game's look (from the title art): dark glass panels, the logo's lime as the accent.
        public static readonly Color Lime = new Color32(0xD4, 0xF0, 0x3C, 0xFF);
        public static readonly Color LimeDim = new Color32(0xD4, 0xF0, 0x3C, 0x55);
        public static readonly Color Glass = new Color32(0x0C, 0x10, 0x12, 0xE8);
        public static readonly Color GlassLight = new Color32(0x1A, 0x21, 0x23, 0xF2);
        public static readonly Color Edge = new Color32(0x3B, 0x47, 0x42, 0xFF);
        public static readonly Color OnLime = new Color32(0x10, 0x14, 0x0E, 0xFF);

        private static Sprite rounded, ring;

        /// <summary>A white rounded rectangle, 9-sliced: any size keeps the same corner.</summary>
        public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded(40, 10));

        /// <summary>The same shape's outline only (borders over see-through fills).</summary>
        public static Sprite Ring => ring != null ? ring : (ring = MakeRounded(40, 10, 3f));

        private static Sprite MakeRounded(int size, int radius, float thickness = 0f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // distance outside the rounded corner, anti-aliased over one pixel
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (thickness > 0f) a = Mathf.Min(a, Mathf.Clamp01(d + thickness + 0.5f));   // hollow inside
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        /// <summary>A rounded box (corner radius scales with `corner`: 1 = 10 px, 2 = 5 px...).</summary>
        public static Image RoundBox(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default, float corner = 2f)
        {
            var img = Box(name, parent, color, anchorMin, anchorMax, offsetMin, offsetMax);
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = corner;
            return img;
        }

        /// <summary>
        /// The standard panel: soft shadow, a thin border, a rounded glass fill. Returns the fill (put content in it);
        /// fill.transform.parent is the panel's root (show/hide that).
        /// </summary>
        public static Image Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
            Color? fill = null, Color? border = null, bool shadow = true, float corner = 2f)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Rect(root, anchorMin, anchorMax, offsetMin, offsetMax);
            if (shadow) RoundBox("Shadow", root.transform, new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.one, new Vector2(1, -4), new Vector2(3, -1), corner);
            var f = RoundBox("Fill", root.transform, fill ?? Glass, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f), corner);
            var b = RoundBox("Border", root.transform, border ?? Edge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, corner);
            b.sprite = Ring;   // an outline drawn over the fill's edge, so a see-through fill isn't tinted by it
            f.transform.SetAsLastSibling();
            return f;
        }

        /// <summary>A key cap ("E", "Tab", "1"): lime outline, the key in lime. Returns its root.</summary>
        public static RectTransform KeyCap(Transform parent, string key, Vector2 anchor, Vector2 offsetMin, Vector2 offsetMax, int size = 11)
        {
            var fill = Panel("Key " + key, parent, anchor, anchor, offsetMin, offsetMax, new Color32(0x16, 0x1C, 0x14, 0xFF), Lime, false, 3f);
            Label("Key", fill.transform, UiFont, size, Lime, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold).text = key;
            return (RectTransform)fill.transform.parent;
        }

        /// <summary>Text width at its font size (for panels that fit their text).</summary>
        public static float TextWidth(Text t, string s)
        {
            var settings = t.GetGenerationSettings(new Vector2(4000, 200));
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(s, settings) / t.pixelsPerUnit;
        }

        /// <summary>Text height when wrapped at this width.</summary>
        public static float TextHeight(Text t, string s, float width)
        {
            var settings = t.GetGenerationSettings(new Vector2(width, 4000));
            return t.cachedTextGeneratorForLayout.GetPreferredHeight(s, settings) / t.pixelsPerUnit;
        }

        private static Font ui, mono;
        // the game's own fonts (Resources/Fonts, SIL Open Font License): the same look on Windows, Linux and Mac.
        // Barlow Semi Condensed for the UI (arrows and shapes fall back to JetBrains Mono, see FontImporter); JetBrains Mono for code.
        public static Font UiFont => ui != null ? ui : (ui = Bundled("BarlowSemiCondensed-Regular", "Bahnschrift", "Segoe UI", "Arial"));
        public static Font Mono => mono != null ? mono : (mono = Bundled("JetBrainsMono-Regular", "Consolas", "Courier New", "DejaVu Sans Mono"));

        private static Font Bundled(string file, params string[] osFallback)
        {
            var f = Resources.Load<Font>("Fonts/" + file);
            return f != null ? f : Font.CreateDynamicFontFromOSFont(osFallback, 16);
        }

        public static Canvas MakeCanvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = false;   // pixel-snapping every element each frame is costly and makes slides stutter
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            // never smaller than 960 x 540 in either direction: on a 16:10 laptop (or 4:3) the UI shrinks a touch instead of
            // running off the sides; on an ultrawide it gets extra room at the sides. 16:9 screens look exactly the same.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        public static RectTransform Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var r = go.GetComponent<RectTransform>();
            if (r == null) r = go.AddComponent<RectTransform>();
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
            return r;
        }

        public static Image Box(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Rect(go, anchorMin, anchorMax, offsetMin, offsetMax);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A filled panel with a 2px border (border colour behind, fill inset).</summary>
        public static Image Framed(string name, Transform parent, Color fill, Color border, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default, float thickness = 2f)
        {
            // rounded like everything else (the old square brass frames are gone); brass/ink map to the new theme
            if (border == Brass) border = Edge;
            if (fill == Ink) fill = Glass;
            var outer = RoundBox(name, parent, border, anchorMin, anchorMax, offsetMin, offsetMax, 2.5f);
            if (fill.a < 0.99f)
            {
                // see-through fill: an outline only, or the border colour would tint the whole panel
                outer.sprite = Ring;
                thickness = 1f;
            }
            var inner = RoundBox("Fill", outer.transform, fill, Vector2.zero, Vector2.one, new Vector2(thickness, thickness), new Vector2(-thickness, -thickness), 2.5f);
            return inner;
        }

        public static Text Label(string name, Transform parent, Font font, int size, Color color, Vector2 anchorMin, Vector2 anchorMax,
            TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Rect(go, anchorMin, anchorMax, offsetMin, offsetMax);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        /// <summary>Horizontal scanlines for CRT screens (a tiny generated texture, tiled).</summary>
        public static RawImage Scanlines(Transform parent, float alpha)
        {
            var tex = new Texture2D(1, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(new[] { new Color(0, 0, 0, alpha), Color.clear, Color.clear });
            tex.Apply();
            var go = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            Rect(go, Vector2.zero, Vector2.one);
            var raw = go.GetComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0, 0, 1, 540f / 3f);
            return raw;
        }

        /// <summary>True while any full-screen or modal UI owns the keyboard (the player should not move).</summary>
        public static bool AnyModalOpen =>
            (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen) ||
            (ElevatorUI.Instance != null && ElevatorUI.Instance.IsOpen) ||
            (PhoneUI.Instance != null && PhoneUI.Instance.IsOpen) ||
            (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying) ||
            (KeypadUI.Instance != null && KeypadUI.Instance.IsOpen) ||
            (LinkStationUI.Instance != null && LinkStationUI.Instance.IsOpen) ||
            (GyaraDbUI.Instance != null && GyaraDbUI.Instance.IsOpen) ||
            (DevMenuUI.Instance != null && DevMenuUI.Instance.IsOpen) ||
            (GameOverUI.Instance != null && GameOverUI.Instance.IsOpen) ||
            (PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen) ||
            (TheoryBookUI.Instance != null && TheoryBookUI.Instance.IsOpen);
    }
}

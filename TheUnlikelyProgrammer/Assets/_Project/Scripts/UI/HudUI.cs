using Unlikely.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Always-on HUD, in the title screen's style (dark glass, lime accents):
    ///   objective card (top left; slides in and glows when it changes) with an optional controls hint under it,
    ///   item cards (bottom centre; number key on each, the one in hand lifts and says IN HAND),
    ///   phone button (bottom right; red dot = unread), a progress bar, and toasts that drop in at the top.
    /// </summary>
    public class HudUI : MonoBehaviour
    {
        private const int MaxSlots = 8;
        private const float ObjectiveWidth = 300f;

        public static HudUI Instance { get; private set; }
        public string Objective => objective != null ? objective.text : "";
        public string Hint => hint != null ? hint.text : "";
        public string LastToast => toast != null ? toast.text : "";

        private GameObject root, progressBox;
        private RectTransform objectiveRoot, hintRoot, toastRoot;
        private CanvasGroup objectiveGroup, toastGroup;
        private Image objectiveBorder, objectiveBar, progressFill, phoneDot;
        private Text objective, hint, progressLabel, toast;
        private readonly RectTransform[] slots = new RectTransform[MaxSlots];
        private readonly Image[] slotBorders = new Image[MaxSlots];
        private readonly Image[] slotFills = new Image[MaxSlots];
        private readonly Text[] slotNames = new Text[MaxSlots];
        private readonly GameObject[] slotTags = new GameObject[MaxSlots];
        private readonly RectTransform[] slotTagRects = new RectTransform[MaxSlots];
        private readonly Text[] slotTagTexts = new Text[MaxSlots];
        private readonly Image[] slotIcons = new Image[MaxSlots];
        private readonly Text[] slotWriting = new Text[MaxSlots];
        private float toastShownAt = -10f, toastUntil, objectiveChangedAt = -10f;
        private CanvasGroup savedGroup;
        private float savedSeen = -100f;
        private float objectiveHeight = 54f;

        private void Awake()
        {
            Instance = this;
            Build();
            Inventory.Changed += RefreshItems;
            RefreshItems();
            SetObjective(null);
            SetHint(null);
            ShowProgress(null, 0);
        }

        private void OnDestroy()
        {
            Inventory.Changed -= RefreshItems;
            if (Instance == this) Instance = null;
        }

        public void SetObjective(string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            objectiveRoot.gameObject.SetActive(on);
            if (on && objective.text != text)
            {
                objectiveChangedAt = Time.unscaledTime;
                if (Time.timeSinceLevelLoad > 1f) Sfx.Play(Sfx.Id.Objective, 0.8f, 1f, 0.5f);
            }
            objective.text = text ?? "";
            if (!on) { LayoutHint(); return; }
            // measured at the label's real width (a little narrower, to be safe), plus room for the title and padding
            float h = UiKit.TextHeight(objective, objective.text, ObjectiveWidth - 30f);
            objectiveHeight = Mathf.Max(54f, 36f + Mathf.Ceil(h));
            objectiveRoot.offsetMin = new Vector2(objectiveRoot.offsetMin.x, -12f - objectiveHeight);
            LayoutHint();
        }

        public void SetHint(string text)
        {
            hintRoot.gameObject.SetActive(!string.IsNullOrEmpty(text));
            hint.text = text ?? "";
            LayoutHint();
        }

        private void LayoutHint()
        {
            if (hint == null) return;
            float top = objectiveRoot.gameObject.activeSelf ? -12f - objectiveHeight - 6f : -12f;
            float h = string.IsNullOrEmpty(hint.text) ? 22f : Mathf.Max(22f, UiKit.TextHeight(hint, hint.text, ObjectiveWidth - 22f) + 8f);
            hintRoot.offsetMax = new Vector2(12f + ObjectiveWidth, top);
            hintRoot.offsetMin = new Vector2(12f, top - h);
        }

        /// <summary>label null hides the bar.</summary>
        public void ShowProgress(string label, float t)
        {
            progressBox.SetActive(!string.IsNullOrEmpty(label));
            if (string.IsNullOrEmpty(label)) return;
            progressLabel.text = label;
            progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(t), 1);
        }

        /// <summary>A short notice that drops in at the top of the screen ("Got: USB-C cable").</summary>
        public void Toast(string text, float seconds = 2.5f)
        {
            toast.text = text;
            Sfx.Play(text.StartsWith("Got") ? Sfx.Id.Pickup : Sfx.Id.Toast, 0.8f, 1f, 0.15f);
            // one line up to 560 wide; longer ones wrap and the toast grows taller
            float w = Mathf.Clamp(UiKit.TextWidth(toast, text) + 44f, 160f, 560f);
            float th = Mathf.Max(28f, Mathf.Ceil(UiKit.TextHeight(toast, text, w - 30f)) + 12f);
            toastRoot.sizeDelta = new Vector2(w, th);
            toastRoot.gameObject.SetActive(true);
            toastShownAt = Time.unscaledTime;
            toastUntil = Time.unscaledTime + seconds;
        }

        public void SetPhoneUnread(bool unread) => phoneDot.enabled = unread;

        // ---- first steps: "W A S D  move" until Donnie has walked a little (once per run: the flag is saved)
        private const string MovedFlag = "tut_moved";
        private GameObject movePrompt;
        private CanvasGroup movePromptGroup;
        private float walked;

        /// <summary>The move prompt is up (tests).</summary>
        public bool MovePromptShown => movePrompt != null && movePrompt.activeSelf;

        private void UpdateMovePrompt()
        {
            if (movePrompt == null) return;
            var p = Unlikely.Player.PlayerController.Instance;
            var rm = Unlikely.World.RoomManager.Instance;
            bool want = p != null && !GameState.Has(MovedFlag) && !Unlikely.Player.PlayerController.Frozen && !UiKit.AnyModalOpen
                        && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen) && (rm == null || !rm.IsTransitioning);
            if (want && p.IsMoving)
            {
                walked += Time.deltaTime;
                if (walked > 1.2f) { GameState.Set(MovedFlag); want = false; }
            }
            if (want && !movePrompt.activeSelf) { movePrompt.SetActive(true); movePromptGroup.alpha = 0f; }
            if (!movePrompt.activeSelf) return;
            movePromptGroup.alpha = Mathf.MoveTowards(movePromptGroup.alpha, want ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            if (!want && movePromptGroup.alpha <= 0f) movePrompt.SetActive(false);
        }

        private void Update()
        {
            bool hide = (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying) || (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen);
            root.SetActive(!hide);
            float now = Time.unscaledTime;
            if (!hide) UpdateMovePrompt();

            // toast: drop in, hold, fade out
            if (toastRoot.gameObject.activeSelf)
            {
                float inT = Mathf.Clamp01((now - toastShownAt) / 0.18f);
                float outT = Mathf.Clamp01((toastUntil - now) / 0.3f);
                toastGroup.alpha = Mathf.Min(inT, outT);
                toastRoot.anchoredPosition = new Vector2(0, -12f + 14f * (1f - Ease(inT)));
                if (now > toastUntil) toastRoot.gameObject.SetActive(false);
            }

            // objective: slides in from the left and the border glows lime for a moment
            // "Saved": fades in and out for a moment after each autosave
            if (SaveSystem.LastAutosave != savedSeen)
            {
                savedSeen = SaveSystem.LastAutosave;
                if (now - savedSeen < 1f) Sfx.Play(Sfx.Id.Save, 0.6f);
            }
            float sinceSave = now - savedSeen;
            float savedAlpha = sinceSave < 0f || sinceSave > 2.2f ? 0f : Mathf.Min(Mathf.Clamp01(sinceSave / 0.2f), Mathf.Clamp01((2.2f - sinceSave) / 0.5f));
            if (savedGroup.alpha != savedAlpha) savedGroup.alpha = savedAlpha;

            float age = now - objectiveChangedAt;
            if (age < 2f) AnimateObjective(age);   // only while it's moving: re-laying out the HUD every frame is wasted work

            if (phoneDot.enabled) phoneDot.color = new Color(1f, 0.35f, 0.3f, 0.6f + 0.4f * Mathf.Sin(now * 5f));
        }

        private void AnimateObjective(float age)
        {
            float slide = Ease(Mathf.Clamp01(age / 0.3f));
            objectiveGroup.alpha = slide;
            float x = 12f + Mathf.Lerp(-24f, 0f, slide);
            objectiveRoot.offsetMin = new Vector2(x, objectiveRoot.offsetMin.y);
            objectiveRoot.offsetMax = new Vector2(x + ObjectiveWidth, objectiveRoot.offsetMax.y);
            float glow = Mathf.Clamp01(1.6f - age);
            objectiveBorder.color = Color.Lerp(UiKit.Edge, UiKit.Lime, glow);
            objectiveBar.color = Color.Lerp(UiKit.LimeDim, UiKit.Lime, 0.5f + 0.5f * glow);
        }

        private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        private void RefreshItems()
        {
            var items = Inventory.Items;
            int count = Mathf.Min(items.Count, MaxSlots);
            float gap = 6f;
            float w = count > 0 ? Mathf.Min(112f, (680f - (count - 1) * gap) / count) : 112f;   // a full pocket squeezes, never under the phone button
            float total = count * w + Mathf.Max(0, count - 1) * gap;
            for (int i = 0; i < MaxSlots; i++)
            {
                bool used = i < count;
                slots[i].gameObject.SetActive(used);
                if (!used) continue;
                bool eq = i == Inventory.EquippedIndex;
                float x0 = -total / 2f + i * (w + gap);
                float lift = eq ? 6f : 0f;
                slots[i].offsetMin = new Vector2(x0, 10f + lift);
                slots[i].offsetMax = new Vector2(x0 + w, 54f + lift);
                slotBorders[i].color = eq ? UiKit.Lime : UiKit.Edge;
                slotFills[i].color = eq ? new Color32(0x1C, 0x24, 0x14, 0xF2) : UiKit.Glass;
                slotNames[i].color = eq ? Color.white : UiKit.Paper;
                slotNames[i].text = ItemIcons.ShortName(items[i].name);

                // the icon; notes get their code written on them
                var icon = ArtLibrary.Get(ItemIcons.For(items[i].id));
                slotIcons[i].enabled = icon != null;
                slotIcons[i].sprite = icon;
                slotIcons[i].color = ItemIcons.Tint(items[i].id);
                string writing = ItemIcons.Writing(items[i].id, items[i].name);
                slotWriting[i].text = writing ?? "";
                slotWriting[i].fontSize = writing == null || writing.Length <= 4 ? 10 : writing.Length <= 6 ? 8 : 6;
                slotNames[i].rectTransform.offsetMin = new Vector2(icon != null ? 44 : 20, 2);

                // in hand: the tag above the card carries the full name, so nothing on it is ever cut off
                slotTags[i].SetActive(eq);
                if (eq)
                {
                    string full = "IN HAND  ·  " + items[i].name;
                    slotTagTexts[i].text = full;
                    float tw = UiKit.TextWidth(slotTagTexts[i], full) + 26f;
                    slotTagRects[i].offsetMin = new Vector2(-tw / 2f, 6);   // above the card, clear of its number key
                    slotTagRects[i].offsetMax = new Vector2(tw / 2f, 20);
                }
            }
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "HudCanvas", 80);
            root = new GameObject("Hud", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            UiKit.Rect(root, Vector2.zero, Vector2.one);

            // ---- objective card, top left
            var obj = UiKit.Panel("Objective", root.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -66), new Vector2(12 + ObjectiveWidth, -12));
            objectiveRoot = (RectTransform)obj.transform.parent;
            objectiveGroup = objectiveRoot.gameObject.AddComponent<CanvasGroup>();
            objectiveBorder = objectiveRoot.Find("Border").GetComponent<Image>();
            objectiveBar = UiKit.RoundBox("Accent", obj.transform, UiKit.Lime, new Vector2(0, 0), new Vector2(0, 1), new Vector2(5, 7), new Vector2(8, -7), 6f);
            UiKit.Label("Title", obj.transform, UiKit.UiFont, 9, UiKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(16, -20), new Vector2(-8, -5)).text = ">_  OBJECTIVE";
            objective = UiKit.Label("Text", obj.transform, UiKit.UiFont, 14, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, FontStyle.Bold,
                new Vector2(16, 6), new Vector2(-10, -22));

            // ---- controls hint, under the objective
            var h = UiKit.Panel("Hint", root.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -94), new Vector2(12 + ObjectiveWidth, -72),
                new Color(0.03f, 0.04f, 0.04f, 0.72f), new Color(1, 1, 1, 0.06f), false);
            hintRoot = (RectTransform)h.transform.parent;
            hint = UiKit.Label("Text", h.transform, UiKit.UiFont, 11, new Color32(0xE6, 0xF5, 0x9A, 0xFF), Vector2.zero, Vector2.one, TextAnchor.MiddleLeft,
                FontStyle.Italic, new Vector2(10, 2), new Vector2(-8, -2));

            // ---- item cards, bottom centre
            for (int i = 0; i < MaxSlots; i++)
            {
                var fill = UiKit.Panel("Slot" + (i + 1), root.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(112, 44));
                slots[i] = (RectTransform)fill.transform.parent;
                slotFills[i] = fill;
                slotBorders[i] = slots[i].Find("Border").GetComponent<Image>();
                UiKit.KeyCap(fill.transform, (i + 1).ToString(), new Vector2(0, 1), new Vector2(-4, -10), new Vector2(10, 4), 9);   // number key on the corner
                slotIcons[i] = UiKit.Box("Icon", fill.transform, Color.white, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, -18), new Vector2(42, 18));
                slotIcons[i].preserveAspect = true;
                slotWriting[i] = UiKit.Label("Writing", slotIcons[i].transform, UiKit.UiFont, 10, new Color32(0x2A, 0x2A, 0x5A, 0xFF), Vector2.zero, Vector2.one,
                    TextAnchor.MiddleCenter, FontStyle.Bold, new Vector2(4, 4), new Vector2(-4, -4));
                slotWriting[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                slotNames[i] = UiKit.Label("Name", fill.transform, UiKit.UiFont, 10, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold,
                    new Vector2(44, 2), new Vector2(-5, -2));
                fill.transform.Find("Key " + (i + 1)).SetAsLastSibling();   // the number key sits over the icon
                slotNames[i].lineSpacing = 0.9f;
                var tag = UiKit.RoundBox("InHand", slots[i], UiKit.Lime, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-26, -2), new Vector2(26, 12), 4f);
                slotTagTexts[i] = UiKit.Label("Text", tag.transform, UiKit.UiFont, 9, UiKit.OnLime, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
                slotTagTexts[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                slotTags[i] = tag.gameObject;
                slotTagRects[i] = tag.rectTransform;
            }

            // ---- phone button, bottom right
            var phone = UiKit.Panel("Phone", root.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-112, 10), new Vector2(-12, 42));
            UiKit.KeyCap(phone.transform, "Tab", new Vector2(0, 0.5f), new Vector2(6, -9), new Vector2(38, 9), 9);
            UiKit.Label("Label", phone.transform, UiKit.UiFont, 13, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold,
                new Vector2(40, 0), Vector2.zero).text = "Phone";
            phoneDot = UiKit.RoundBox("Unread", phone.transform.parent, new Color(1f, 0.35f, 0.3f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-7, -7), new Vector2(5, 5), 1f);
            phoneDot.enabled = false;

            // ---- progress bar, lower middle
            var pb = UiKit.Panel("Progress", root.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-110, 100), new Vector2(110, 120));
            progressBox = pb.transform.parent.gameObject;
            progressFill = UiKit.RoundBox("Fill", pb.transform, UiKit.Lime, Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(-2, -2), 4f);
            progressLabel = UiKit.Label("Label", pb.transform, UiKit.UiFont, 10, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            progressLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

            // ---- "Saved", bottom left
            var sv = UiKit.Panel("Saved", root.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 12), new Vector2(84, 34),
                new Color(0.03f, 0.04f, 0.04f, 0.8f), new Color(1, 1, 1, 0.08f), false, 3f);
            savedGroup = sv.transform.parent.gameObject.AddComponent<CanvasGroup>();
            savedGroup.alpha = 0f;
            UiKit.RoundBox("Dot", sv.transform, UiKit.Lime, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(9, -3), new Vector2(15, 3), 1f);
            UiKit.Label("Text", sv.transform, UiKit.UiFont, 11, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(22, 0), Vector2.zero).text = "Saved";

            // ---- first steps: how to move (bottom centre, above the item cards)
            var mp = UiKit.Panel("MovePrompt", root.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 96), new Vector2(150, 140),
                new Color(0.03f, 0.04f, 0.04f, 0.82f), UiKit.LimeDim);
            movePrompt = mp.transform.parent.gameObject;
            movePromptGroup = movePrompt.AddComponent<CanvasGroup>();
            movePromptGroup.blocksRaycasts = false;
            string[] keys = { "W", "A", "S", "D" };
            for (int k = 0; k < keys.Length; k++)
                UiKit.KeyCap(mp.transform, keys[k], new Vector2(0, 0.5f), new Vector2(14 + k * 30, -13), new Vector2(40 + k * 30, 13), 13);
            UiKit.Label("Text", mp.transform, UiKit.UiFont, 14, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(140, 0), new Vector2(-10, 0)).text = "or arrow keys to move";
            movePrompt.SetActive(false);

            // ---- toast, top centre
            var t = UiKit.Panel("Toast", root.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-140, -40), new Vector2(140, -12), null, UiKit.Lime);
            toastRoot = (RectTransform)t.transform.parent;
            toastRoot.pivot = new Vector2(0.5f, 1);
            toastRoot.sizeDelta = new Vector2(280, 28);
            toastGroup = toastRoot.gameObject.AddComponent<CanvasGroup>();
            UiKit.RoundBox("Dot", t.transform, UiKit.Lime, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, -3), new Vector2(16, 3), 1f);
            toast = UiKit.Label("Text", t.transform, UiKit.UiFont, 13, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold,
                new Vector2(18, 0), new Vector2(-8, 0));
            toastRoot.gameObject.SetActive(false);
        }
    }
}

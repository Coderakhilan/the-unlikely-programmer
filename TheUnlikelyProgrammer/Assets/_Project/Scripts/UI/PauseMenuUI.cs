using Unlikely.Core;
using Unlikely.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Esc while Donnie is free (no computer, phone, dialogue or cutscene open): Resume, Options, Quit to title.
    /// The game stops while it's open. Quitting saves where Donnie is, when it's a safe moment.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        public static PauseMenuUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        private GameObject root, main;
        private MenuList list;
        private OptionsPanel options;
        private Text info;
        private bool busyLastFrame;
        private float savedScale = 1f;

        private void Awake()
        {
            Instance = this;
            var canvas = UiKit.MakeCanvas(transform, "PauseCanvas", 450);
            var dim = UiKit.Box("Pause", canvas.transform, new Color(0, 0, 0, 0.7f), Vector2.zero, Vector2.one);
            dim.raycastTarget = true;
            root = dim.gameObject;

            var fill = UiKit.Framed("Main", dim.transform, MenuKit.Panel, MenuKit.Lime, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-180, -130), new Vector2(180, 130));
            main = fill.transform.parent.gameObject;
            UiKit.Label("Title", fill.transform, UiKit.UiFont, 26, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleCenter, FontStyle.Bold,
                new Vector2(0, -48), new Vector2(0, -10)).text = "PAUSED";
            info = UiKit.Label("Info", fill.transform, UiKit.UiFont, 11, MenuKit.Dim, new Vector2(0, 0), new Vector2(1, 0), TextAnchor.MiddleCenter, FontStyle.Italic,
                new Vector2(14, 8), new Vector2(-14, 48));
            list = new MenuList(fill.transform, Vector2.zero, Vector2.one, 32f, 16);
            list.Root.offsetMin = new Vector2(40, 52);
            list.Root.offsetMax = new Vector2(-40, -58);
            list.Add("Resume", Close);
            list.Add("Options", () => { main.SetActive(false); options.Open(); });
            list.Add("Save and quit to title", StoryDirector.SaveAndQuitToTitle);
            options = new OptionsPanel(dim.transform, () => { main.SetActive(true); list.Select(1); });
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsOpen) Time.timeScale = savedScale;
        }

        /// <summary>Something else owns Esc right now (it closes that instead).</summary>
        private static bool Busy =>
            UiKit.AnyModalOpen ||
            (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) ||
            (TheoryBookUI.Instance != null && TheoryBookUI.Instance.IsOpen) ||
            (RoomManager() != null && RoomManager().IsTransitioning);

        private static Unlikely.World.RoomManager RoomManager() => Unlikely.World.RoomManager.Instance;

        public void Open()
        {
            if (IsOpen) return;
            savedScale = Time.timeScale;
            Time.timeScale = 0f;
            root.SetActive(true);
            main.SetActive(true);
            list.Select(0);
            string slot = SaveSystem.ActiveSlot >= 0 ? $"Slot {SaveSystem.ActiveSlot + 1}  ·  " : "Not saving (dev run)  ·  ";
            bool safe = StoryDirector.Instance != null && StoryDirector.Instance.CanAutosave(true);
            info.text = slot + DifficultyInfo.Name(GameState.Difficulty)
                + (SaveSystem.ActiveSlot < 0 ? ""
                   : safe ? "\nQuitting saves right here. Continue picks up where you left off."
                   : "\nNot a safe moment to save (a chase or a scene is on). Quitting goes back to your last autosave.");
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            Time.timeScale = savedScale;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!IsOpen)
            {
                // only when nothing was open last frame either: Esc that closed a computer shouldn't also pause
                bool busy = Busy;
                if (!busy && !busyLastFrame && kb.escapeKey.wasPressedThisFrame) Open();
                busyLastFrame = busy;
                return;
            }
            busyLastFrame = true;
            if (options.IsOpen) { options.Tick(); return; }
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            list.Tick();
        }
    }
}

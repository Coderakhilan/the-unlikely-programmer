using Unlikely.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// F10 on the title screen: "jump to" menu of test points (see DevJump). ↑↓ choose, Enter go, Esc / F10 close, or click.
    /// </summary>
    public class DevMenuUI : MonoBehaviour
    {
        public static DevMenuUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        private GameObject root;
        private Text[] rows;
        private int selected;

        public static DevMenuUI Ensure()
        {
            if (Instance == null) new GameObject("DevMenuUI").AddComponent<DevMenuUI>();
            return Instance;
        }

        private void Awake()
        {
            Instance = this;
            var canvas = UiKit.MakeCanvas(transform, "DevMenuCanvas", 500);
            var bg = UiKit.Box("DevMenu", canvas.transform, new Color(0, 0, 0, 0.82f), Vector2.zero, Vector2.one);
            root = bg.gameObject;
            var panel = UiKit.Framed("Panel", bg.transform, UiKit.Ink, UiKit.Brass, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-340, -260), new Vector2(340, 260), 2f);
            UiKit.Label("Title", panel.transform, UiKit.UiFont, 18, UiKit.Amber, new Vector2(0, 1), Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(16, -40), new Vector2(-16, -8)).text = "DEV  ·  JUMP TO";
            UiKit.Label("Help", panel.transform, UiKit.UiFont, 11, UiKit.Muted, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleLeft, FontStyle.Italic,
                new Vector2(16, 6), new Vector2(-16, 28)).text = "↑↓ choose    Enter go (restarts the game at that point)    Esc / F10 close    or click";
            // one clickable row per jump point
            rows = new Text[DevJump.Points.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = UiKit.Label("Row" + i, panel.transform, UiKit.UiFont, 14, UiKit.Paper, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft,
                    FontStyle.Normal, new Vector2(16, -44 - 26 * (i + 1)), new Vector2(-16, -44 - 26 * i));
                rows[i].raycastTarget = true;
                int idx = i;
                rows[i].gameObject.AddComponent<Button>().onClick.AddListener(() => { selected = idx; Go(); });
            }
            root.SetActive(false);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Toggle()
        {
            root.SetActive(!IsOpen);
            if (IsOpen) { UiKit.EnsureEventSystem(); Refresh(); }
        }

        private void Go()
        {
            root.SetActive(false);
            DevJump.Go(DevJump.Points[selected].id);
        }

        private void Refresh()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                string label = DevJump.Points[i].label;
                rows[i].text = i == selected ? "<color=#FFC85A><b>›  " + label + "</b></color>" : "     " + label;
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !IsOpen) return;
            if (kb.escapeKey.wasPressedThisFrame) { root.SetActive(false); return; }
            int move = (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame ? 1 : 0);
            if (move != 0) { selected = (selected + move + DevJump.Points.Length) % DevJump.Points.Length; Refresh(); }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Go();
        }
    }
}

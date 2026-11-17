using System.Collections;
using Unlikely.Core;
using Unlikely.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Inside the elevator (full-screen illustration). The button of the floor Donnie is on glows white;
    /// W/S (or arrows) move between buttons, the display above them names the floor; E/Enter rides there.
    /// Floors his badge doesn't cover are refused with the reason. Esc steps back out.
    /// </summary>
    public class ElevatorUI : MonoBehaviour
    {
        // button rectangles in the source image (1672 x 796), floors 0..3
        private const float ImgW = 1672f, ImgH = 796f;
        private static readonly Rect[] Buttons =
        {
            new Rect(1236, 606, 94, 62),   // 0
            new Rect(1236, 536, 94, 62),   // 1
            new Rect(1236, 466, 94, 62),   // 2
            new Rect(1236, 396, 94, 62),   // 3
        };
        private static readonly Rect Display = new Rect(1182, 296, 204, 72);

        public static ElevatorUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        private GameObject root;
        private RectTransform picture;
        private readonly Image[] glow = new Image[4];
        private readonly Image[] ring = new Image[4];
        private Text display, caption;
        private int currentFloor, selected;
        private bool riding;
        private int openedFrame;

        private void Awake()
        {
            Instance = this;
            Build();
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Open(string currentRoomId)
        {
            currentFloor = currentRoomId != null && currentRoomId.StartsWith("floor") && int.TryParse(currentRoomId.Substring(5), out int f) ? f : 0;
            selected = currentFloor;
            riding = false;
            openedFrame = Time.frameCount;   // the E that opened the panel must not also press a button
            var img = picture.GetComponent<Image>();
            if (img.sprite == null) img.sprite = ArtLibrary.Get("elevator_interior");
            caption.text = "W / S  choose      E  go      Esc  step out";
            root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (!riding) root.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen || riding || Time.frameCount == openedFrame) return;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Close(); return; }

            bool up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || (pad != null && pad.dpad.down.wasPressedThisFrame);
            if (up && selected < 3) { selected++; Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move); Refresh(); }
            if (down && selected > 0) { selected--; Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Move); Refresh(); }

            bool go = kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame
                      || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (go) { Unlikely.Core.Sfx.Play(Unlikely.Core.Sfx.Id.Key, 0.8f, 0.7f); Press(); }
        }

        /// <summary>Highlight a floor's button and press it (same as W/S then E).</summary>
        public void Choose(int floor)
        {
            selected = Mathf.Clamp(floor, 0, 3);
            Refresh();
            Press();
        }

        public string Caption => caption.text;

        /// <summary>Story override: a line refusing a floor the badge would allow (the job on this floor isn't done), or null.</summary>
        public static System.Func<int, string> StoryBlock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStoryBlock() => StoryBlock = null;

        private void Press()
        {
            if (selected == currentFloor) { caption.text = "Already on " + Access.FloorNames[selected] + "."; return; }
            var block = Access.Allowed(selected) ? StoryBlock?.Invoke(selected) : null;
            if (block != null)
            {
                // the badge would let him; Donnie himself decides not to go yet. The panel says nothing: he steps out and thinks it.
                Close();
                DialogueUI.Instance?.Show("", new[] { block });
                return;
            }
            if (!Access.Allowed(selected))
            {
                display.text = "<color=#FF6A5A>ACCESS DENIED</color>";
                caption.text = Access.DeniedLine(selected);
                return;
            }
            StartCoroutine(Ride(selected));
        }

        private IEnumerator Ride(int floor)
        {
            riding = true;
            string arrow = floor > currentFloor ? "▲" : "▼";
            caption.text = "";
            for (int i = 0; i < 3; i++)
            {
                display.text = arrow + "  " + (i % 2 == 0 ? Access.FloorNames[floor].ToUpperInvariant() : "");
                yield return new WaitForSecondsRealtime(0.35f);
            }
            riding = false;
            root.SetActive(false);
            RoomManager.Instance.GoTo("floor" + floor, "elevator");
        }

        private void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                glow[i].enabled = i == currentFloor;      // the floor you're on is lit white
                ring[i].enabled = i == selected;          // the one you're about to press
            }
            bool ok = Access.Allowed(selected);
            display.text = selected == currentFloor
                ? Access.FloorNames[selected].ToUpperInvariant() + "\n<size=9>YOU ARE HERE</size>"
                : Access.FloorNames[selected].ToUpperInvariant() + (ok ? "" : "\n<size=9><color=#FF6A5A>NO ACCESS</color></size>");
            caption.text = ok || selected == currentFloor
                ? "W / S  choose      E  go      Esc  step out"
                : Access.DeniedLine(selected);
        }

        // ---------------------------------------------------------------- construction

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "ElevatorCanvas", 120);
            canvas.pixelPerfect = false;
            root = UiKit.Box("Elevator", canvas.transform, Color.black, Vector2.zero, Vector2.one).gameObject;
            root.GetComponent<Image>().raycastTarget = true;

            // the illustration fills the screen (sides cropped as needed); overlays are placed in its own pixel space
            var pic = UiKit.Box("Interior", root.transform, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            picture = pic.rectTransform;
            var fitter = pic.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = ImgW / ImgH;

            for (int i = 0; i < 4; i++)
            {
                var r = Buttons[i];
                glow[i] = Overlay("Lit" + i, r, new Color(1f, 1f, 1f, 0.55f), 0f);
                ring[i] = Overlay("Choice" + i, r, UiKit.Amber, 3f);
                // ring: a frame, not a filled box
                ring[i].color = Color.clear;
                var frame = ring[i].gameObject;
                foreach (var (a0, a1) in new[] { (new Vector2(0, 0), new Vector2(1, 0)), (new Vector2(0, 1), new Vector2(1, 1)), (new Vector2(0, 0), new Vector2(0, 1)), (new Vector2(1, 0), new Vector2(1, 1)) })
                {
                    var edge = UiKit.Box("Edge", frame.transform, UiKit.Amber, a0, a1, new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));
                    edge.raycastTarget = false;
                }
            }

            var disp = Overlay("Display", Display, new Color32(0x0C, 0x0E, 0x12, 0xFF), -2f);
            display = UiKit.Label("Text", disp.transform, UiKit.UiFont, 13, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);

            var band = UiKit.Box("Band", root.transform, new Color(0, 0, 0, 0.7f), Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 44));
            caption = UiKit.Label("Caption", band.transform, UiKit.UiFont, 14, UiKit.Paper, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Normal, new Vector2(20, 0), new Vector2(-20, 0));
        }

        /// <summary>A box over part of the illustration, given in source-image pixels (top-left origin).</summary>
        private Image Overlay(string name, Rect r, Color color, float grow)
        {
            var img = UiKit.Box(name, picture, color,
                new Vector2(r.xMin / ImgW, 1f - r.yMax / ImgH), new Vector2(r.xMax / ImgW, 1f - r.yMin / ImgH),
                new Vector2(-grow, -grow), new Vector2(grow, grow));
            img.raycastTarget = false;
            return img;
        }
    }
}

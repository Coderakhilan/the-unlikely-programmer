using Unlikely.Core;
using Unlikely.World;
using UnityEngine;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Likely / Leet: computers aren't safe. While Donnie is on one (an office PC or the link station; not the floor
    /// terminal's lessons, not the GyaraDB console) a bar fills over DifficultyInfo.ComputerSeconds. Full: somebody notices,
    /// CAUGHT, and he starts again from the floor's elevator. Away from computers the bar drains at half speed, so
    /// stepping off for a second doesn't reset it. Time doesn't run while somebody is talking.
    /// </summary>
    public class ComputerWatchUI : MonoBehaviour
    {
        public static ComputerWatchUI Instance { get; private set; }

        /// <summary>Seconds spent on computers that haven't drained away yet.</summary>
        public float Heat { get; set; }
        public float Limit => DifficultyInfo.ComputerSeconds;
        public int CaughtCount { get; private set; }

        private static readonly string[] Lines =
        {
            "\"Excuse me. That's not your computer.\"",
            "\"Since when does the janitor do spreadsheets?\"",
            "\"Hey! You! Step away from the desk.\"",
            "\"Is that... SQL? Security! SECURITY!\"",
            "\"I don't know what you're doing, but you're doing it on my login.\"",
        };

        private GameObject bar;
        private Image fill;
        private Text label;

        private void Awake()
        {
            Instance = this;
            var canvas = UiKit.MakeCanvas(transform, "ComputerWatchCanvas", 260);
            var frame = UiKit.Framed("WatchBar", canvas.transform, new Color(0.05f, 0.05f, 0.06f, 0.92f), new Color(0.9f, 0.3f, 0.2f, 0.9f),
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-170, -30), new Vector2(170, -8));
            bar = frame.transform.parent.gameObject;
            fill = UiKit.Box("Fill", frame.transform, new Color(0.9f, 0.3f, 0.2f), Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(0, -2));
            label = UiKit.Label("Label", frame.transform, UiKit.UiFont, 11, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
            bar.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static bool AtComputer =>
            (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen && !TerminalUI.Instance.InLessons) ||
            (LinkStationUI.Instance != null && LinkStationUI.Instance.IsOpen);

        private static bool Paused =>
            (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) ||
            (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying) ||
            (GameOverUI.Instance != null && GameOverUI.Instance.IsOpen) ||
            (PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen);

        private void Update()
        {
            if (!DifficultyInfo.ComputersWatched()) { Heat = 0f; bar.SetActive(false); return; }
            bool on = AtComputer;
            if (!Paused)
            {
                if (on) Heat += Time.deltaTime;
                else Heat = Mathf.Max(0f, Heat - Time.deltaTime * 0.5f);
            }
            bar.SetActive(on);
            if (on)
            {
                float t = Mathf.Clamp01(Heat / Limit);
                fill.rectTransform.anchorMax = new Vector2(t, 1);
                fill.color = Color.Lerp(new Color(0.85f, 0.75f, 0.25f), new Color(0.95f, 0.2f, 0.15f), t);
                int left = Mathf.CeilToInt(Mathf.Max(0f, Limit - Heat));
                label.text = t < 0.75f ? $"SOMEBODY COULD WALK IN   ·   {left}s" : $"SOMEBODY'S COMING. GET OFF IT.   ·   {left}s";
            }
            if (on && Heat >= Limit) Caught();
        }

        /// <summary>Somebody noticed: off the computer, CAUGHT, and back to this floor's elevator.</summary>
        public void Caught()
        {
            Heat = 0f;
            bar.SetActive(false);
            if (TerminalUI.Instance != null && TerminalUI.Instance.IsOpen) TerminalUI.Instance.Close();
            if (LinkStationUI.Instance != null && LinkStationUI.Instance.IsOpen) LinkStationUI.Instance.Close();
            string line = Lines[CaughtCount++ % Lines.Length];
            string room = FloorRoom(RoomManager.Instance != null && RoomManager.Instance.Current != null ? RoomManager.Instance.Current.roomId : null);
            GameOverUI.Instance?.Show("CAUGHT.", line + "\n\nToo long on one computer. Find what you need, then get off it.", () =>
            {
                if (room != null && RoomManager.Instance != null) RoomManager.Instance.GoTo(room, "elevator");
            });
        }

        /// <summary>The floor a room belongs to ("floor2", "f3_exec_a" -> floor3, the server room -> floor3).</summary>
        public static string FloorRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (roomId.StartsWith("floor")) return roomId;
            if (roomId.Length > 1 && roomId[0] == 'f' && char.IsDigit(roomId[1])) return "floor" + roomId[1];
            return "floor3";
        }
    }
}

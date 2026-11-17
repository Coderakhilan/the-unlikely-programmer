using Unlikely.Core;
using Unlikely.UI;
using System.Collections;
using Unlikely.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Unlikely.Story
{
    /// <summary>
    /// Runs the story: the opening, then Floor 0.
    /// A loaded save (SaveSystem.Pending) skips the opening and puts Donnie at that floor's elevator; reaching a new floor
    /// saves a checkpoint. On Vibe, The Theory of Everything goes in his pockets and stays there.
    /// Dev tools (see DevTools): F10 on the title screen only opens the "jump to" menu (DevMenuUI / DevJump).
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        [SerializeField] private bool playIntro = true;
        [SerializeField] private Floor0Story floor0;
        [SerializeField] private Floor1Story floor1;
        [SerializeField] private Floor2Story floor2;
        [SerializeField] private Floor3Story floor3;
        [SerializeField] private EndingStory ending;
        [SerializeField] private TextAsset cutscenes;

        public void Configure(Floor0Story floor0Story, Floor1Story floor1Story, Floor2Story floor2Story, Floor3Story floor3Story,
            EndingStory endingStory, TextAsset cutsceneJson)
        {
            ending = endingStory;
            floor0 = floor0Story;
            floor1 = floor1Story;
            floor2 = floor2Story;
            floor3 = floor3Story;
            cutscenes = cutsceneJson;
        }

        /// <summary>Dev tools (F5-F10, the jump menu) stay available in every build until testing is over.</summary>
        public static readonly bool DevTools = true;

        public const string TitleScene = "Title";

        public static StoryDirector Instance { get; private set; }

        /// <summary>A resume save is being put back (nothing autosaves meanwhile).</summary>
        public bool Resuming { get; private set; }

        private const float AutosaveEvery = 30f;
        private float nextAutosave;

        private void Awake() => Instance = this;

        private void Start()
        {
            StoryScenes.Load(cutscenes);
            StoryBeats.Reset();
            Sfx.Ring(false);
            Music.ClearStory();
            UpdateMusic();
            SaveSystem.WorldCapture = WorldState.Capture;
            bool jumping = DevJump.Pending != null;
            var loaded = jumping ? null : SaveSystem.TakePending();   // a saved checkpoint: flags, pockets, phone
            DevJump.BeforeBegin();   // a dev jump: earlier floors already done (sets intro_seen too)
            GiveTheory();
            if (loaded != null && loaded.IsResume) StartCoroutine(Resume(loaded));   // Continue: right where it was left
            else
            {
                if (loaded == null && playIntro && !GameState.Has("intro_seen"))
                    CutsceneUI.Instance.PlayFromBlack(StoryScenes.Get(StoryScenes.Intro), () => { GameState.Set("intro_seen"); Music.SetScene(null); BeginFloor0(); });
                else BeginFloor0();
                if (jumping) StartCoroutine(DevJump.AfterBegin());
                if (loaded != null) StartCoroutine(PlaceAt(loaded.floor));
            }
            if (RoomManager.Instance != null) RoomManager.Instance.RoomChanged += OnRoomChanged;
            nextAutosave = Time.unscaledTime + AutosaveEvery;
        }

        private void OnDestroy()
        {
            if (RoomManager.Instance != null) RoomManager.Instance.RoomChanged -= OnRoomChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Continue: Donnie goes straight back to the room and spot he was in, the floors set themselves up from the saved
        /// flags (skipping every scripted beat already played), then the world is put back: the cart, the mopped spills,
        /// where everyone stands, the objective.
        /// </summary>
        private IEnumerator Resume(SaveSystem.Checkpoint cp)
        {
            Resuming = true;
            yield return null;   // RoomManager has entered its start room
            if (!RoomManager.Instance.Place(cp.room, new Vector3(cp.x, cp.y, 0f)))
                RoomManager.Instance.Place("floor" + Mathf.Clamp(cp.floor, 0, 3), RoomManager.Instance.MarkerPosition("floor" + Mathf.Clamp(cp.floor, 0, 3), "elevator"));
            BeginFloor0();
            yield return null;   // the floors' first frame
            WorldState.Apply(cp);
            if (floor0 != null) floor0.Resume();
            if (floor1 != null) floor1.Resume();
            if (floor2 != null) floor2.Resume();
            if (floor3 != null) floor3.Resume();
            if (ending != null) ending.Resume();
            yield return null;
            Resuming = false;
            HudUI.Instance?.Toast("Continued  ·  " + SaveSystem.FloorLabel(cp.floor));
        }

        /// <summary>The floor a room belongs to (the server room counts as Floor 3).</summary>
        public static int FloorOfRoom(string roomId)
        {
            if (roomId == "server") return 3;
            string f = Darkness.FloorOf(roomId);
            return f != null && f.Length == 1 && char.IsDigit(f[0]) ? f[0] - '0' : 0;
        }

        // ------------------------------------------------------------------ autosave

        /// <summary>
        /// Donnie is free and nothing is half-done: no dialogue, call, computer, cutscene, chase or scripted beat,
        /// not hiding, not frozen. Only then is a resume save written (anything else would come back broken).
        /// </summary>
        public bool CanAutosave(bool allowPause = false)
        {
            var rooms = RoomManager.Instance;
            var p = Unlikely.Player.PlayerController.Instance;
            if (SaveSystem.ActiveSlot < 0 || Resuming || rooms == null || rooms.Current == null || rooms.IsTransitioning || p == null) return false;
            if (!GameState.Has("intro_seen") || StoryBeats.Playing > 0) return false;
            if (Unlikely.Player.PlayerController.Frozen || p.IsAutoWalking || HideSpot.IsHidden) return false;
            if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) return false;
            if (PhoneUI.Instance != null && (PhoneUI.Instance.IsOpen || PhoneUI.Instance.IsRinging)) return false;
            if ((TerminalUI.Instance != null && TerminalUI.Instance.IsOpen) || (ElevatorUI.Instance != null && ElevatorUI.Instance.IsOpen)
                || (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying) || (KeypadUI.Instance != null && KeypadUI.Instance.IsOpen)
                || (LinkStationUI.Instance != null && LinkStationUI.Instance.IsOpen) || (GyaraDbUI.Instance != null && GyaraDbUI.Instance.IsOpen)
                || (GameOverUI.Instance != null && GameOverUI.Instance.IsOpen) || (DevMenuUI.Instance != null && DevMenuUI.Instance.IsOpen))
                return false;
            if (!allowPause && PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen) return false;
            if (floor0 != null && !floor0.CanSave) return false;
            if (floor2 != null && !floor2.CanSave) return false;
            if (floor3 != null && !floor3.CanSave) return false;
            if (ending != null && !ending.CanSave) return false;
            return true;
        }

        /// <summary>Write the resume save now, if it's a safe moment. Returns whether it saved.</summary>
        public bool Autosave(bool allowPause = false)
        {
            if (!CanAutosave(allowPause)) return false;
            SaveSystem.SaveResume(FloorOfRoom(RoomManager.Instance.Current.roomId));
            nextAutosave = Time.unscaledTime + AutosaveEvery;
            return true;
        }

        private IEnumerator AutosaveAfterTransition()
        {
            yield return new WaitUntil(() => RoomManager.Instance == null || !RoomManager.Instance.IsTransitioning);
            yield return null;   // let the floors react to the new room first (calls, chases...)
            Autosave();
        }

        private void OnApplicationQuit() => Autosave(true);

        /// <summary>Vibe: future Donnie's book, in his pockets from the start and after anything that empties them.</summary>
        public static void GiveTheory()
        {
            if (DifficultyInfo.HasTheory() && !Inventory.Has(TheoryBookUI.ItemId))
                Inventory.Add(TheoryBookUI.ItemId, TheoryBookUI.ItemName, false);
        }

        /// <summary>
        /// Every room change: the resume save (once the fade is over and Donnie is free).
        /// The first time this run steps onto a floor (from its elevator) it's also that floor's checkpoint.
        /// </summary>
        private void OnRoomChanged(Room from, Room to)
        {
            if (to == null || Resuming) return;
            StartCoroutine(AutosaveAfterTransition());
            // the server room is a checkpoint of its own (Load Game: replay the endings without Floor 3 again)
            if (to.roomId == "server" && GameState.Has("f3_complete")) { SaveSystem.ReachedFloor(SaveSystem.ServerRoom); return; }
            if (!to.roomId.StartsWith("floor") || to.roomId.Length != 6) return;
            int floor = to.roomId[5] - '0';
            if (floor <= 0 || !GameState.Has("f" + (floor - 1) + "_complete")) return;
            SaveSystem.ReachedFloor(floor);
        }

        /// <summary>A loaded checkpoint: Donnie steps out of that floor's elevator.</summary>
        private IEnumerator PlaceAt(int floor)
        {
            yield return null;
            if (floor <= 0) yield break;
            if (floor >= 3) Floor2Story.Instance?.BackToNormal();
            if (floor >= SaveSystem.ServerRoom) RoomManager.Instance.GoTo("server", "from_floor3");
            else RoomManager.Instance.GoTo("floor" + floor, "elevator");
            HudUI.Instance?.Toast("Loaded: " + SaveSystem.FloorLabel(floor));
        }
        private void BeginFloor0()
        {
            if (floor0 != null) floor0.Begin();
            if (floor1 != null) floor1.Begin();   // waits until Donnie first steps onto Administration
            if (floor2 != null) floor2.Begin();   // waits until Human Resources
            if (floor3 != null) floor3.Begin();   // waits until the Executive floor (after Floor 2, whose hooks it chains)
            if (ending != null) ending.Begin();   // the server room and the four endings (chains Floor 3's door hook)
        }

        /// <summary>After an ending: back to the title screen.</summary>
        public static void Restart()
        {
            TitleSequences.PendingCredits = true;   // the title opens with "A game by INFI", the endings count and the credits
            QuitToTitle();
        }

        /// <summary>Pause menu: save where Donnie is (if it's a safe moment), then the title.</summary>
        public static void SaveAndQuitToTitle()
        {
            Instance?.Autosave(true);
            QuitToTitle();
        }

        public static void QuitToTitle()
        {
            Time.timeScale = 1f;
            HideSpot.Enabled = false;
            Unlikely.Player.PlayerController.Frozen = false;
            SaveSystem.Stop();
            GameState.ResetAll();
            Inventory.Clear();
            PhoneData.Clear();
            SceneManager.LoadScene(TitleScene);
        }

        /// <summary>Each floor's own track, and the sequences that change it (polled: it follows saves and loads for free).</summary>
        private static readonly string[] FloorTracks = { "another_day_at_work", "the_paper_trail", "corporate_intrigue", "the_people_upstairs" };

        private void UpdateMusic()
        {
            var rooms = RoomManager.Instance;
            if (rooms == null || rooms.Current == null) return;
            Music.SetBase(FloorTracks[Mathf.Clamp(FloorOfRoom(rooms.Current.roomId), 0, 3)]);
            string phase = null;
            if (rooms.Current.roomId == "server") phase = "honest_days_work";                                  // the server room
            else if (floor2 != null && (floor2.Phase == "security")) phase = "framed";                                   // truth: security in the dark
            else if (floor2 != null && (floor2.Phase == "cut" || floor2.Phase == "ryker")) phase = "the_intern";   // lie: the Ryker fight
            else if (floor3 != null && floor3.Phase == "dark") phase = "framed";                  // the CEO's blackout (Framed starts on the dialogue cue at "REVOKED")
            Music.SetPhase(phase);
        }

        private void Update()
        {
            UpdateMusic();
            // a quiet autosave every half minute while Donnie is free (long puzzles in one room aren't lost)
            if (Time.unscaledTime >= nextAutosave)
            {
                nextAutosave = Time.unscaledTime + 5f;   // not a safe moment: try again shortly
                Autosave();
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unlikely.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// The home screen over the title art: Continue / New Game / Load Game (once a save exists), Options, Extras, Exit Game.
    /// New Game: difficulty, then one of three slots. Load Game: a slot, then any floor that run reached.
    /// Extras: Artwork (pictures seen in play; the rest stay locked) and Achievements (the 8 stars).
    /// Keyboard (W/S, Enter, Esc) or mouse.
    /// </summary>
    public class TitleMenuUI : MonoBehaviour
    {
        public const string GameScene = "Floors";

        [SerializeField] private Sprite background;
        [SerializeField] private Sprite[] art = new Sprite[0];
        [SerializeField] private TextAsset cutscenes;

        public static TitleMenuUI Instance { get; private set; }
        public string Page { get; private set; } = "main";
        public IReadOnlyList<MenuList.Entry> MainEntries => mainList.Entries;
        public IReadOnlyList<MenuList.Entry> PanelEntries => panelList.Entries;

        public void Configure(Sprite title, Sprite[] artwork, TextAsset cutsceneJson)
        {
            background = title;
            art = artwork;
            cutscenes = cutsceneJson;
        }

        private sealed class Picture { public string id, caption, where; public Sprite sprite; }

        private GameObject mainGo, panelGo, galleryGo, viewerGo, achievementsGo;
        private MenuList mainList, panelList;
        private Text panelTitle, panelNote, starsLabel, viewerCaption, galleryCount;
        private Image viewerImage;
        private OptionsPanel options;
        private readonly List<Picture> pictures = new List<Picture>();
        private readonly List<Image> galleryFrames = new List<Image>();
        private int galleryIndex;
        private Difficulty chosen = Difficulty.Unlikely;
        private bool newGameFlow;
        private int chosenSlot;
        private Transform galleryGrid;
        private Transform achievementsList;
        private Sprite star;

        // ================================================================== setup

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            SaveSystem.Stop();
            GameState.ResetAll();
            Inventory.Clear();
            PhoneData.Clear();
            UiKit.EnsureEventSystem();
            Sfx.Ring(false);
            Music.ClearStory();
            Music.SetBase("the_unlikely_programmer");
            star = MakeStar(64);
            CollectPictures();
            Build();
            sequences = gameObject.AddComponent<TitleSequences>();
            // shown on the very first launch only; after that, here in Options
            options.AddLink("Terms of use · Privacy", () => Play(sequences.Show(true), ShowOptions), "What you agreed to on first launch, and what the game does (and doesn't) do with your data.");
            options.AddLink("A note from infi", () => Play(sequences.Show(false), ShowOptions), "The note from the first launch.");
            options.AddLink("Hard reset", () => { options.Root.SetActive(false); ShowHardReset(); },
                "Erase every save, star and picture and start completely fresh. Asks first.");
            // start-up (A game by INFI, the terms, infi's note) the first time this launch; the credits after an ending
            if (!TitleSequences.SkipForTests && (TitleSequences.PendingCredits || !TitleSequences.BootDone))
                Play(TitleSequences.PendingCredits ? sequences.Credits(true) : sequences.Boot(), ShowMain);
            else ShowMain();
            TitleSequences.BootDone = true;
            TitleSequences.PendingCredits = false;
        }

        private TitleSequences sequences;

        /// <summary>Play one of the full-screen sequences, then go to a screen.</summary>
        private void Play(IEnumerator sequence, Action then)
        {
            HideAll();
            Page = "sequence";
            StartCoroutine(Run());
            IEnumerator Run()
            {
                yield return sequence;
                then();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "TitleCanvas", 10);
            var bg = UiKit.Box("Background", canvas.transform, Color.black, Vector2.zero, Vector2.one);
            var pic = UiKit.Box("Art", bg.transform, Color.white, Vector2.zero, Vector2.one);
            pic.sprite = background;
            if (background != null)
            {
                var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = background.rect.width / background.rect.height;
            }
            else pic.color = new Color(0.08f, 0.1f, 0.1f);

            // a soft dark band on the left so the menu reads over the art
            var shade = UiKit.Box("Shade", canvas.transform, Color.white, Vector2.zero, new Vector2(0.42f, 1));
            shade.sprite = Gradient();
            shade.type = Image.Type.Simple;

            mainGo = UiKit.Box("Main", canvas.transform, Color.clear, new Vector2(0.035f, 0.07f), new Vector2(0.27f, 0.6f)).gameObject;
            mainList = new MenuList(mainGo.transform, Vector2.zero, Vector2.one, 34f, 19);
            mainList.Note = UiKit.Label("Note", mainGo.transform, UiKit.UiFont, 11, UiKit.Paper, new Vector2(0, 1), new Vector2(1.4f, 1),
                TextAnchor.UpperLeft, FontStyle.Italic, new Vector2(12, -250), new Vector2(0, -212));

            starsLabel = UiKit.Label("Stars", canvas.transform, UiKit.UiFont, 13, MenuKit.Lime, new Vector2(1, 0), new Vector2(1, 0), TextAnchor.LowerRight,
                FontStyle.Bold, new Vector2(-300, 10), new Vector2(-14, 34));

            // the panel every sub-screen uses
            var fill = UiKit.Framed("Panel", canvas.transform, MenuKit.Panel, MenuKit.Lime, new Vector2(0.6f, 0.5f), new Vector2(0.6f, 0.5f),
                new Vector2(-280, -190), new Vector2(280, 190));
            panelGo = fill.transform.parent.gameObject;
            panelTitle = UiKit.Label("Title", fill.transform, UiKit.UiFont, 22, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft,
                FontStyle.Bold, new Vector2(20, -46), new Vector2(-20, -8));
            panelList = new MenuList(fill.transform, Vector2.zero, Vector2.one, 32f, 14);
            panelList.Root.offsetMin = new Vector2(16, 110);
            panelList.Root.offsetMax = new Vector2(-16, -54);
            panelNote = UiKit.Label("Note", fill.transform, UiKit.UiFont, 12, UiKit.Paper, Vector2.zero, new Vector2(1, 0), TextAnchor.UpperLeft,
                FontStyle.Normal, new Vector2(22, 10), new Vector2(-22, 104));
            panelList.Note = panelNote;

            options = new OptionsPanel(canvas.transform, ShowMain);
            var orect = options.Root.GetComponent<RectTransform>();
            orect.anchorMin = orect.anchorMax = new Vector2(0.6f, 0.5f);

            BuildGallery(canvas.transform);
            BuildViewer(canvas.transform);
            BuildAchievements(canvas.transform);
            soundtrack = new SoundtrackPage(canvas.transform, ShowExtras);
        }

        // ================================================================== screens

        private void HideAll()
        {
            mainGo.SetActive(false);
            panelGo.SetActive(false);
            galleryGo.SetActive(false);
            viewerGo.SetActive(false);
            achievementsGo.SetActive(false);
            if (soundtrack != null && soundtrack.IsOpen) soundtrack.Close();
            if (options.IsOpen) options.Root.SetActive(false);
        }

        public void ShowMain()
        {
            HideAll();
            Page = "main";
            mainGo.SetActive(true);
            int latest = SaveSystem.LatestSlot;
            mainList.Clear();
            if (latest >= 0)
            {
                var s = SaveSystem.Load(latest);
                mainList.Add("Continue", Continue, true, $"Slot {latest + 1}  ·  {SaveSystem.FloorLabel(s.ContinueFloor)}  ·  {DifficultyInfo.Name(s.Difficulty)}");
            }
            mainList.Add("New Game", ShowDifficulties, true, "Start from the very beginning.");
            if (latest >= 0) mainList.Add("Load Game", () => ShowSlots(false), true, "Any slot, any floor it reached.");
            mainList.Add("Options", ShowOptions, true, "Resolution, display, volume.");
            mainList.Add("Extras", ShowExtras, true, "Artwork and achievements.");
            mainList.Add("Exit Game", Quit, true, "Back to real life.");
            mainList.Select(0);
            starsLabel.text = "";
            ShowStarRow();
        }

        private void OpenPanel(string screen, string title)
        {
            HideAll();
            Page = screen;
            panelGo.SetActive(true);
            panelTitle.text = title;
            panelList.Clear();
        }

        private void ShowDifficulties()
        {
            newGameFlow = true;
            OpenPanel("difficulty", "NEW GAME  ·  DIFFICULTY");
            foreach (var d in DifficultyInfo.All)
            {
                var diff = d;
                panelList.Add(DifficultyInfo.Name(d), () => { chosen = diff; ShowSlots(true); }, true, DifficultyInfo.Blurb(d));
            }
            panelList.Add("Back", ShowMain, true, "");
            panelList.Select(1);   // Unlikely, the standard game
        }

        private void ShowSlots(bool forNewGame)
        {
            newGameFlow = forNewGame;
            OpenPanel("slots", forNewGame ? "NEW GAME  ·  " + DifficultyInfo.Name(chosen).ToUpperInvariant() + "  ·  SLOT" : "LOAD GAME  ·  SLOT");
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                int slot = i;
                var s = SaveSystem.Load(i);
                string label = s == null
                    ? $"Slot {i + 1}  ·  empty"
                    : $"Slot {i + 1}  ·  {SaveSystem.FloorLabel(s.ContinueFloor)}  ·  {DifficultyInfo.Name(s.Difficulty)}{(s.finished ? "  ·  finished" : "")}";
                string note = s == null ? (forNewGame ? "An empty slot." : "Nothing saved here.")
                    : "Last played " + new DateTime(s.lastPlayed).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)
                      + "\nFloors reached: " + string.Join(", ", s.checkpoints.ConvertAll(c => c.floor == SaveSystem.ServerRoom ? "server room" : c.floor.ToString()));
                if (forNewGame) panelList.Add(label, () => { chosenSlot = slot; if (SaveSystem.Load(slot) != null) ShowConfirm(); else StartNew(); }, true, note);
                else panelList.Add(label, () => { chosenSlot = slot; ShowCheckpoints(); }, s != null, note);
            }
            if (!forNewGame) panelList.Add("Delete a save...", ShowDeleteSlots, true, "Pick a slot to wipe. You'll be asked to confirm.");
            panelList.Add("Back", () => { if (newGameFlow) ShowDifficulties(); else ShowMain(); }, true, "");
            panelList.SelectFirstEnabled();
        }

        private void ShowConfirm()
        {
            var s = SaveSystem.Load(chosenSlot);
            OpenPanel("confirm", $"OVERWRITE SLOT {chosenSlot + 1}?");
            panelList.Add("No, keep it", () => ShowSlots(true), true, "");
            panelList.Add("Yes, start over", StartNew, true,
                $"Slot {chosenSlot + 1} has a {DifficultyInfo.Name(s.Difficulty)} run at {SaveSystem.FloorLabel(s.ContinueFloor)}. It will be gone.");
            panelList.Select(0);
        }

        private void ShowDeleteSlots()
        {
            OpenPanel("delete", "DELETE A SAVE");
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                int slot = i;
                var s = SaveSystem.Load(i);
                string label = s == null ? $"Slot {i + 1}  ·  empty"
                    : $"Slot {i + 1}  ·  {SaveSystem.FloorLabel(s.ContinueFloor)}  ·  {DifficultyInfo.Name(s.Difficulty)}";
                panelList.Add(label, () => { chosenSlot = slot; ShowDeleteConfirm(); }, s != null, s == null ? "Nothing to delete." : "Delete this slot.");
            }
            panelList.Add("Back", () => ShowSlots(false), true, "");
            panelList.SelectFirstEnabled();
        }

        private void ShowDeleteConfirm()
        {
            var s = SaveSystem.Load(chosenSlot);
            if (s == null) { ShowDeleteSlots(); return; }
            OpenPanel("deleteconfirm", $"DELETE SLOT {chosenSlot + 1}?");
            panelList.Add("No, keep it", ShowDeleteSlots, true, "");
            panelList.Add("Yes, delete it", () =>
            {
                SaveSystem.Delete(chosenSlot);
                if (SaveSystem.AnySave) ShowDeleteSlots(); else ShowMain();
            }, true, $"Slot {chosenSlot + 1}: a {DifficultyInfo.Name(s.Difficulty)} run at {SaveSystem.FloorLabel(s.ContinueFloor)}. "
                     + "Every floor it reached goes with it. This can't be undone. (Stars and artwork are kept.)");
            panelList.Select(0);
        }

        /// <summary>Options > Hard reset: are you sure?</summary>
        private void ShowHardReset()
        {
            OpenPanel("hardreset", "HARD RESET?");
            panelList.Add("No, keep everything", ShowOptions, true, "");
            panelList.Add("Yes, erase everything", () =>
            {
                for (int i = 0; i < SaveSystem.SlotCount; i++) SaveSystem.Delete(i);
                Profile.Erase();
                sequences.ForgetTerms();
                Sfx.Play(Sfx.Id.Confirm);
                // as if the game had never been played: the first-launch screens, then a bare title
                Play(sequences.Boot(), ShowMain);
            }, true, $"All {SaveSystem.SlotCount} save slots, every star and every unlocked picture are deleted, "
                     + "and the game starts again like a first launch. Your display and sound settings are kept. This can't be undone.");
            panelList.Select(0);
        }

        private void ShowCheckpoints()
        {
            var s = SaveSystem.Load(chosenSlot);
            if (s == null) { ShowSlots(false); return; }
            OpenPanel("checkpoints", $"LOAD SLOT {chosenSlot + 1}  ·  {DifficultyInfo.Name(s.Difficulty).ToUpperInvariant()}");
            if (s.HasResume)
                panelList.Add("Where you left off  ·  " + SaveSystem.FloorLabel(s.resume.floor), () =>
                {
                    if (SaveSystem.BeginResume(chosenSlot)) SceneManager.LoadScene(GameScene);
                }, true, "Pick up exactly where this run was saved: same room, same spot." + (string.IsNullOrEmpty(s.resume.savedAt) ? "" : "\nSaved " + s.resume.savedAt));
            foreach (var c in s.checkpoints)
            {
                int floor = c.floor;
                bool server = floor == SaveSystem.ServerRoom;
                panelList.Add((server ? "" : "Start of ") + SaveSystem.FloorLabel(floor), () => Load(chosenSlot, floor), true,
                    (server ? "Back in the server room, just before the ending. (For a different ending, load an earlier floor and answer the calls differently.)"
                            : "Start from the moment you first stepped onto this floor. (Anything since is replaced as you play on.)")
                    + (string.IsNullOrEmpty(c.savedAt) ? "" : "\nSaved " + c.savedAt));
            }
            panelList.Add("Back", () => ShowSlots(false), true, "");
            panelList.Select(0);
        }

        private void ShowOptions()
        {
            HideAll();
            Page = "options";
            options.Open();
        }

        private Transform starRow;

        /// <summary>The 8 stars on the title, bottom right: lit for each one earned, dark outlines for the rest.</summary>
        private void ShowStarRow()
        {
            // earned stars only, as a column in the bottom-right corner (no backing panel: each star has a dark shadow
            // so it reads over the bright art)
            const float size = 38f, gap = 6f;
            int n = Profile.Stars.Length;
            if (starRow == null)
            {
                var col = new GameObject("Stars", typeof(RectTransform));
                col.transform.SetParent(starsLabel.transform.parent, false);
                starRow = UiKit.Rect(col, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16 - size, 14), new Vector2(-16, 14 + n * size + (n - 1) * gap));
            }
            foreach (Transform c in starRow) Destroy(c.gameObject);
            // only the stars earned so far: none until the first one, then they stack up from the bottom
            int lit = Profile.StarCount;
            for (int i = 0; i < lit; i++)
            {
                bool on = true;   // i = 0 is the bottom star
                float y = i * (size + gap);
                var shadow = UiKit.Box("Shadow" + i, starRow, new Color(0, 0, 0, 0.6f), new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(2, y - 3), new Vector2(2, y + size - 3));
                shadow.sprite = star;
                shadow.preserveAspect = true;
                var s = UiKit.Box("Star" + i, starRow, on ? MenuKit.Lime : new Color(0.55f, 0.58f, 0.56f, 0.55f), new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(0, y), new Vector2(0, y + size));
                s.sprite = star;
                s.preserveAspect = true;
                if (on) s.gameObject.AddComponent<Outline>().effectColor = new Color(0.83f, 0.94f, 0.24f, 0.35f);   // a faint glow
            }
        }

        private void ShowExtras()
        {
            OpenPanel("extras", "EXTRAS");
            int seen = pictures.FindAll(p => Unlocked(p)).Count;
            panelList.Add("Artwork", ShowGallery, true, $"{seen} of {pictures.Count} pictures found. Endings unlock the rest.");
            int heard = Array.FindAll(Soundtrack.All, t => Profile.Heard(t.id)).Length;
            panelList.Add("Soundtrack", ShowSoundtrack, true, $"{heard} of {Soundtrack.All.Length} tracks found. Listen to any you've heard.");
            panelList.Add("Achievements", ShowAchievements, true, $"{Profile.StarCount} of {Profile.Stars.Length} stars.");
            panelList.Add("Credits", () => Play(sequences.Credits(false), ShowExtras), true, "Who made the game.");
            panelList.Add("Back", ShowMain, true, "");
            panelList.Select(0);
        }

        private SoundtrackPage soundtrack;

        /// <summary>Extras > Soundtrack (tests).</summary>
        public SoundtrackPage SoundtrackPage => soundtrack;

        private void ShowSoundtrack()
        {
            HideAll();
            Page = "soundtrack";
            soundtrack.Open();
        }

        // ================================================================== actions

        private void Continue()
        {
            int slot = SaveSystem.LatestSlot;
            var s = slot >= 0 ? SaveSystem.Load(slot) : null;
            if (s != null && SaveSystem.BeginResume(slot)) SceneManager.LoadScene(GameScene);   // right where it was left
        }

        private void StartNew()
        {
            SaveSystem.NewGame(chosenSlot, chosen);
            SceneManager.LoadScene(GameScene);
        }

        private void Load(int slot, int floor)
        {
            if (SaveSystem.Begin(slot, floor)) SceneManager.LoadScene(GameScene);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ================================================================== artwork

        private void CollectPictures()
        {
            pictures.Clear();
            var byName = new Dictionary<string, Sprite>();
            foreach (var s in art) if (s != null) byName[s.name.ToLowerInvariant()] = s;
            if (background != null) pictures.Add(new Picture { id = "title", caption = "The Unlikely Programmer", where = "Title screen", sprite = background });
            if (cutscenes == null) return;
            var file = JsonUtility.FromJson<CutsceneFile>(cutscenes.text);
            if (file?.sequences == null) return;
            foreach (var seq in file.sequences)
                foreach (var shot in seq.shots)
                {
                    if (string.IsNullOrEmpty(shot.image) || pictures.Exists(p => p.id == shot.image)) continue;
                    byName.TryGetValue(shot.image.ToLowerInvariant(), out var sprite);
                    pictures.Add(new Picture
                    {
                        id = shot.image,
                        caption = ArtTitles.TryGetValue(shot.image, out var named) ? named : Pretty(shot.image),
                        where = seq.id == "intro" ? "The opening" : Where(seq.title ?? seq.id),
                        sprite = sprite,
                    });
                }
        }

        [Serializable] private class CutsceneFile { public Sequence[] sequences; }

        private static bool Unlocked(Picture p) => p.id == "title" || Profile.Seen(p.id);

        /// <summary>The artwork's names in Extras (a picture not listed here falls back to its tidied-up file name).</summary>
        private static readonly Dictionary<string, string> ArtTitles = new Dictionary<string, string>
        {
            { "flashback_opening_of_donnie_s_family", "The Raymonds" },
            { "sam_s_funeral", "Goodbye, Sam" },
            { "betty_s_friends_make_fun_of_betty", "The Punchline" },
            { "betty_takes_an_alternate_route_home", "The Long Way Round" },
            { "ceo_celebration", "Another Record Year" },
            { "heated_phone_call_between_liam_and_ceo", "Father and Son" },
            { "betty_being_kidnapped", "Three People in a Van" },
            { "betty_returns_safely_accomplice_ending", "Home Safe" },
            { "the_accomplice_ending_donnie_is_arrested", "Somebody to Blame" },
            { "the_last_query_ending_donnie_is_devasted_betty_is_dead", "The Last Query" },
            { "the_last_query", "System Offline" },
            { "capitalist_ending", "Cashing Out" },
            { "good_samaritan_ending_betty_survives", "Calder Street" },
            { "good_samaritan_ending", "A Proper Job" },
        };

        /// <summary>"ENDING  ·  GOOD SAMARITAN" -> "Ending · Good Samaritan".</summary>
        private static string Where(string title)
        {
            var words = title.Replace("·", " · ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 1) words[i] = words[i][0] + words[i].Substring(1).ToLowerInvariant();
            return string.Join(" ", words);
        }

        /// <summary>"betty_s_friends_make_fun_of_betty" -> "Betty's friends make fun of Betty".</summary>
        public static string Pretty(string id)
        {
            string s = " " + id.Replace("_s_", "'s_").Replace('_', ' ') + " ";
            foreach (var name in new[] { "donnie", "betty", "sam", "liam", "ceo", "ryker" })
                s = s.Replace(" " + name + " ", " " + (name == "ceo" ? "CEO" : char.ToUpperInvariant(name[0]) + name.Substring(1)) + " ")
                     .Replace(" " + name + "'s ", " " + (name == "ceo" ? "CEO" : char.ToUpperInvariant(name[0]) + name.Substring(1)) + "'s ");
            s = s.Trim().Replace("devasted", "devastated");
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private void BuildGallery(Transform canvas)
        {
            var fill = UiKit.Framed("Gallery", canvas, MenuKit.Panel, MenuKit.Lime, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-440, -240), new Vector2(440, 240));
            galleryGo = fill.transform.parent.gameObject;
            UiKit.Label("Title", fill.transform, UiKit.UiFont, 22, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(20, -44), new Vector2(-20, -8)).text = "ARTWORK";
            galleryCount = UiKit.Label("Count", fill.transform, UiKit.UiFont, 12, MenuKit.Dim, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleRight,
                FontStyle.Normal, new Vector2(20, -44), new Vector2(-20, -8));
            UiKit.Label("Keys", fill.transform, UiKit.UiFont, 11, MenuKit.Dim, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleLeft, FontStyle.Normal,
                new Vector2(20, 6), new Vector2(-20, 28)).text = "Arrows / WASD choose    Enter view    Esc back    or click";

            var viewport = UiKit.Box("Viewport", fill.transform, Color.clear, Vector2.zero, Vector2.one, new Vector2(16, 32), new Vector2(-16, -50));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.raycastTarget = true;
            var content = new GameObject("Grid", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = Vector2.zero;
            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(160, 112);
            grid.spacing = new Vector2(8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            galleryGrid = content.transform;
            galleryGo.SetActive(false);
        }

        private void ShowGallery()
        {
            HideAll();
            Page = "gallery";
            galleryGo.SetActive(true);
            foreach (Transform c in galleryGrid) Destroy(c.gameObject);
            galleryFrames.Clear();
            int found = 0;
            for (int i = 0; i < pictures.Count; i++)
            {
                var p = pictures[i];
                bool open = Unlocked(p);
                if (open) found++;
                int idx = i;
                var frame = new GameObject("Pic" + i, typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(galleryGrid, false);
                var border = frame.GetComponent<Image>();
                border.color = new Color(0.25f, 0.28f, 0.27f);
                galleryFrames.Add(border);
                var inner = UiKit.Box("Inner", frame.transform, new Color(0.06f, 0.07f, 0.08f), Vector2.zero, Vector2.one, new Vector2(3, 20), new Vector2(-3, -3));
                if (open && p.sprite != null)
                {
                    var img = UiKit.Box("Image", inner.transform, Color.white, Vector2.zero, Vector2.one);
                    img.sprite = p.sprite;
                    img.preserveAspect = true;
                }
                else UiKit.Label("Locked", inner.transform, UiKit.UiFont, 30, MenuKit.Dim, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold).text = "?";
                UiKit.Label("Caption", frame.transform, UiKit.UiFont, 9, open ? UiKit.Paper : MenuKit.Dim, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleCenter,
                    FontStyle.Normal, new Vector2(3, 2), new Vector2(-3, 19)).text = open ? p.caption : "Not found yet";
                border.raycastTarget = true;
                var trigger = frame.AddComponent<EventTrigger>();
                var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                hover.callback.AddListener(_ => SelectPicture(idx));
                trigger.triggers.Add(hover);
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(_ => { SelectPicture(idx); OpenViewer(); });
                trigger.triggers.Add(click);
            }
            galleryCount.text = $"{found} / {pictures.Count} found";
            SelectPicture(Mathf.Clamp(galleryIndex, 0, pictures.Count - 1));
        }

        private void SelectPicture(int i)
        {
            if (pictures.Count == 0) return;
            galleryIndex = Mathf.Clamp(i, 0, pictures.Count - 1);
            for (int k = 0; k < galleryFrames.Count; k++)
                galleryFrames[k].color = k == galleryIndex ? MenuKit.Lime : new Color(0.25f, 0.28f, 0.27f);
        }

        private void BuildViewer(Transform canvas)
        {
            var bg = UiKit.Box("Viewer", canvas, new Color(0, 0, 0, 0.96f), Vector2.zero, Vector2.one);
            bg.raycastTarget = true;
            viewerGo = bg.gameObject;
            viewerImage = UiKit.Box("Image", bg.transform, Color.white, Vector2.zero, Vector2.one, new Vector2(20, 50), new Vector2(-20, -14));
            viewerImage.preserveAspect = true;
            viewerCaption = UiKit.Label("Caption", bg.transform, UiKit.UiFont, 14, UiKit.Paper, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleCenter,
                FontStyle.Normal, new Vector2(20, 8), new Vector2(-20, 46));
            var trigger = bg.gameObject.AddComponent<EventTrigger>();
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ => CloseViewer());
            trigger.triggers.Add(click);
            viewerGo.SetActive(false);
        }

        private void OpenViewer()
        {
            var p = pictures[galleryIndex];
            if (!Unlocked(p) || p.sprite == null) return;
            Page = "viewer";
            viewerGo.SetActive(true);
            viewerImage.sprite = p.sprite;
            viewerCaption.text = $"<b>{p.caption}</b>   ·   {p.where}\n<size=11><color=#7E8880>← →  more      Esc / click  back</color></size>";
        }

        private void StepViewer(int d)
        {
            for (int n = 0; n < pictures.Count; n++)
            {
                galleryIndex = (galleryIndex + d + pictures.Count) % pictures.Count;
                if (Unlocked(pictures[galleryIndex]) && pictures[galleryIndex].sprite != null) break;
            }
            SelectPicture(galleryIndex);
            OpenViewer();
        }

        private void CloseViewer()
        {
            viewerGo.SetActive(false);
            Page = "gallery";
        }

        // ================================================================== achievements

        private void BuildAchievements(Transform canvas)
        {
            var fill = UiKit.Framed("Achievements", canvas, MenuKit.Panel, MenuKit.Lime, new Vector2(0.6f, 0.5f), new Vector2(0.6f, 0.5f),
                new Vector2(-300, -235), new Vector2(300, 235));
            achievementsGo = fill.transform.parent.gameObject;
            UiKit.Label("Title", fill.transform, UiKit.UiFont, 22, MenuKit.Lime, new Vector2(0, 1), new Vector2(1, 1), TextAnchor.MiddleLeft, FontStyle.Bold,
                new Vector2(20, -44), new Vector2(-20, -8)).text = "ACHIEVEMENTS";
            UiKit.Label("Keys", fill.transform, UiKit.UiFont, 11, MenuKit.Dim, Vector2.zero, new Vector2(1, 0), TextAnchor.MiddleLeft, FontStyle.Normal,
                new Vector2(20, 6), new Vector2(-20, 26)).text = "Esc back";
            achievementsList = UiKit.Box("List", fill.transform, Color.clear, Vector2.zero, Vector2.one, new Vector2(20, 30), new Vector2(-20, -50)).transform;
            achievementsGo.SetActive(false);
        }

        private void ShowAchievements()
        {
            HideAll();
            Page = "achievements";
            achievementsGo.SetActive(true);
            foreach (Transform c in achievementsList) Destroy(c.gameObject);
            float row = 1f / (Profile.Stars.Length + 1);
            var total = UiKit.Label("Total", achievementsList, UiKit.UiFont, 15, MenuKit.Lime, new Vector2(0, 1 - row), Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold);
            total.text = $"{Profile.StarCount} of {Profile.Stars.Length} stars.  Finish on every difficulty; reach every ending.";
            for (int i = 0; i < Profile.Stars.Length; i++)
            {
                var s = Profile.Stars[i];
                bool got = Profile.HasStar(s.id);
                bool ending = s.id.StartsWith("ending_");
                float top = 1 - row * (i + 1);
                var line = UiKit.Box("Star" + i, achievementsList, i % 2 == 0 ? new Color(1, 1, 1, 0.03f) : Color.clear, new Vector2(0, top - row), new Vector2(1, top));
                var icon = UiKit.Box("Icon", line.transform, got ? MenuKit.Lime : new Color(0.22f, 0.25f, 0.24f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(6, -15), new Vector2(36, 15));
                icon.sprite = star;
                icon.preserveAspect = true;
                string title = got || !ending ? s.title : "???";
                UiKit.Label("Text", line.transform, UiKit.UiFont, 14, got ? UiKit.Paper : MenuKit.Dim, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Normal,
                    new Vector2(48, 0), Vector2.zero).text = $"<b>{title}</b>    <size=11>{(got ? (ending ? "Ending reached." : "Finished.") : s.hint)}</size>";
            }
        }

        // ================================================================== input

        private void Update()
        {
            if (Page == "soundtrack") { soundtrack.Tick(); return; }   // (keeps its time bar moving even without a keyboard)
            var kb = Keyboard.current;
            if (kb == null) return;
            if (Unlikely.Story.StoryDirector.DevTools && Page != "sequence")
            {
                if (kb.f10Key.wasPressedThisFrame) { DevMenuUI.Ensure().Toggle(); return; }
                if (DevMenuUI.Instance != null && DevMenuUI.Instance.IsOpen) return;   // it has the keyboard
            }
            switch (Page)
            {
                case "sequence":
                    return;   // the full-screen sequence has the keyboard
                case "main":
                    mainList.Tick();
                    return;
                case "options":
                    options.Tick();
                    return;
                case "gallery":
                    if (MenuKit.Back(kb)) { ShowExtras(); return; }
                    if (MenuKit.Left(kb)) SelectPicture(galleryIndex - 1);
                    if (MenuKit.Right(kb)) SelectPicture(galleryIndex + 1);
                    if (MenuKit.Up(kb)) SelectPicture(galleryIndex - 5);
                    if (MenuKit.Down(kb)) SelectPicture(galleryIndex + 5);
                    if (MenuKit.Confirm(kb)) OpenViewer();
                    return;
                case "viewer":
                    if (MenuKit.Back(kb) || MenuKit.Confirm(kb)) { CloseViewer(); return; }
                    if (MenuKit.Left(kb)) StepViewer(-1);
                    if (MenuKit.Right(kb)) StepViewer(1);
                    return;
                case "soundtrack":
                    soundtrack.Tick();
                    return;
                case "achievements":
                    if (MenuKit.Back(kb) || MenuKit.Confirm(kb)) ShowExtras();
                    return;
                default:   // the panel screens
                    if (kb.escapeKey.wasPressedThisFrame)
                    {
                        var back = panelList.Entries.Count > 0 ? panelList.Entries[panelList.Entries.Count - 1] : null;
                        if (Page == "confirm") ShowSlots(true);
                        else if (Page == "deleteconfirm") ShowDeleteSlots();
                        else if (Page == "hardreset") ShowOptions();
                        else back?.act?.Invoke();
                        return;
                    }
                    panelList.Tick();
                    return;
            }
        }

        // ================================================================== generated sprites

        private static Sprite MakeStar(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / 5;
                float r = (i % 2 == 0 ? 0.48f : 0.2f) * size;
                pts[i] = new Vector2(size / 2f + Mathf.Cos(a) * r, size / 2f + Mathf.Sin(a) * r - size * 0.03f);
            }
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;   // 2x2 supersampling for soft edges
                    for (int s = 0; s < 4; s++)
                        if (Inside(pts, new Vector2(x + 0.25f + 0.5f * (s % 2), y + 0.25f + 0.5f * (s / 2)))) hits++;
                    px[y * size + x] = new Color(1, 1, 1, hits / 4f);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        private static Sprite Gradient()
        {
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, new Color(0.02f, 0.03f, 0.03f, 0.72f * (1f - x / 63f)));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
        }
    }
}

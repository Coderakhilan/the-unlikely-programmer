#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unlikely.EditorTools
{
    /// <summary>
    /// Builds the game scene: Floors 0-3 (corridors), every room on them as its own enlarged interior, and the server room.
    /// Level data comes from tools/build_level.py (Data/Collision/floorN.json + Data/Collision/rooms/*.json).
    /// Menu: Unlikely > Build Floors.  Command line: -executeMethod Unlikely.EditorTools.FloorsBuilder.Build
    /// Hand-written coordinates below are pixels of the RAW map images in art_raw/.
    /// </summary>
    public static class FloorsBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Floors.unity";
        private const string TitlePath = "Assets/_Project/Scenes/Title.unity";
        private const string TheoryPath = "Assets/_Project/Data/Guide/theory_of_everything.json";

        /// <summary>The home screen: the title art, the menus, Extras' artwork. Built with the Floors scene (one command).</summary>
        private static void BuildTitle()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var title = ArtImporter.LoadFolder("ui");
            Sprite bg = System.Array.Find(title, t => t.name == "title_home");
            var menu = new GameObject("TitleMenuUI");
            menu.SetActive(false);
            menu.AddComponent<TitleMenuUI>().Configure(bg, ArtImporter.LoadFolder("cutscenes"), Asset("Assets/_Project/Data/Dialogue/cutscenes.json"));
            menu.SetActive(true);
            EditorSceneManager.SaveScene(scene, TitlePath);
        }
        private const string CollisionDir = "Assets/_Project/Data/Collision";
        private const float Ppu = ArtImporter.PixelsPerUnit;
        private const float FloorScale = 1.28f;  // must match FLOOR_MAP_SCALE in tools/process_art.py
        private const float ServerScale = 0.6f;  // must match FLOOR_MAP_SCALES["server_room"]

        [System.Serializable] private class FloorDoor { public string room; public int index; public string name; public int[] hotspot; public int[] spawn; public string locked; }
        [System.Serializable] private class RoomExit { public int index; public int[] hotspot; public int[] spawn; }

        [System.Serializable]
        private class LevelData
        {
            public string id;
            public string name;
            public string floor;
            public int cell;
            public int width;
            public int height;
            public float scale;
            public float actor;
            public int[] origin;   // rooms: top-left of the crop in raw floor-map pixels
            public int[] shade;    // floors: each room's floor area, darkened on the corridor map
            public int[] rects;
            public FloorDoor[] doors;
            public RoomExit[] exits;
        }

        [MenuItem("Unlikely/Build Floors")]
        public static void Build()
        {
            ArtImporter.ImportAll();
            AppIcon.Apply();
            Directory.CreateDirectory("Assets/_Project/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Global Light 2D").AddComponent<Light2D>().lightType = Light2D.LightType.Global;

            // ---------------- player ----------------
            var player = new GameObject("Donnie");
            var rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = player.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.4f, 0.25f);   // just the feet, so tight gaps between furniture are passable
            col.offset = new Vector2(0f, 0.125f);
            var controller = player.AddComponent<PlayerController>();
            var body = new GameObject("Sprite");
            body.transform.SetParent(player.transform, false);
            var sr = body.AddComponent<SpriteRenderer>();
            var frames = ArtImporter.LoadCharacterFrames("char_donnie_walk");
            sr.sprite = frames[0];
            var walkAnim = body.AddComponent<SpriteWalkAnimator>();
            walkAnim.Configure(frames, sr, controller);
            walkAnim.ConfigureAlt(ArtImporter.LoadCharacterFrames("char_donnie_tray")); // pushing the cart
            walkAnim.ConfigureAlt2(ArtImporter.LoadCharacterFrames("char_donnie_flash")); // holding the torch in the dark

            // the torch beam and a faint glow around Donnie (only switched on during power cuts)
            Light2D MakeLight(string name, Transform parent, float inner, float outer, float innerAngle, float outerAngle, float intensity, Color color)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var l = go.AddComponent<Light2D>();
                l.lightType = Light2D.LightType.Point;
                l.pointLightInnerRadius = inner;
                l.pointLightOuterRadius = outer;
                l.pointLightInnerAngle = innerAngle;
                l.pointLightOuterAngle = outerAngle;
                l.intensity = intensity;
                l.color = color;
                l.enabled = false;
                return l;
            }
            var beam = MakeLight("TorchBeam", player.transform, 1.5f, 6.5f, 34f, 62f, 1.25f, new Color(1f, 0.95f, 0.8f));
            var glow = MakeLight("TorchGlow", player.transform, 0.2f, 1.4f, 360f, 360f, 0.55f, new Color(1f, 0.9f, 0.75f));
            glow.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            player.AddComponent<YSorter>().isStatic = false;
            player.AddComponent<PlayerTools>();

            // ---------------- camera ----------------
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 540f / 2f / Ppu;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var ppc = camGo.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = (int)Ppu;
            ppc.refResolutionX = 960;
            ppc.refResolutionY = 540;
            // Always show exactly a 960x540 slice of the world, scaled up to fill the window. Without this the camera
            // picks a whole-number zoom, so odd window sizes (e.g. a maximised editor Game view) showed far more map.
            ppc.cropFrame = PixelPerfectCamera.CropFrame.StretchFill;
            var follow = camGo.AddComponent<CameraFollow>();
            follow.target = player.transform;

            var rooms = new List<Room>();

            // ---------------- floors (corridors) ----------------
            float[] liftX = { 1120, 1093, 1096, 1110 };
            float[] liftY = { 278, 258, 242, 240 };
            float[][] stairs =
            {
                new float[] { 1410, 362, 110, 24 },
                new float[] { 1408, 330, 110, 20 },
                new float[] { 1405, 310, 110, 14 },
                new float[] { 1405, 302, 110, 16 },
            };
            const string stairsLine = "The stairs. The elevator is faster, and nobody asks questions in an elevator.";
            var floors = new MapBuilder[4];

            for (int i = 0; i <= 3; i++)
            {
                var data = Load($"{CollisionDir}/floor{i}.json");
                var f = new MapBuilder("floor" + i, "floor" + i + ".png", data, FloorScale);
                f.ShadeRooms(data.shade);
                f.Spawn("elevator", liftX[i], liftY[i] + 50);
                f.Trigger("Elevator", liftX[i], liftY[i] + 4, 110, 24).AddComponent<Elevator>();
                var s = stairs[i];
                f.Trigger("Stairs", s[0], s[1], s[2], s[3]).AddComponent<DialogueInteractable>().Configure("Look", "", stairsLine);

                // a door hotspot in the corridor for every room on this floor
                if (data.doors != null)
                {
                    foreach (var d in data.doors)
                    {
                        string spawnId = $"{d.room}_door{d.index}";
                        var door = f.TriggerRect("Door_" + spawnId, d.hotspot).AddComponent<Door>();
                        if (!string.IsNullOrEmpty(d.locked))
                        {
                            door.ConfigureLocked("Look", d.locked);
                            continue;
                        }
                        f.Spawn(spawnId, d.spawn[0], d.spawn[1]);
                        door.Configure(d.room, "door" + d.index, "Enter " + d.name);
                        if (Locks.TryGetValue(d.room, out var lk)) door.SetLock(lk.kind, lk.value, lk.name, lk.line);
                    }
                }
                floors[i] = f;
            }

            // ---------------- Floor 0: story objects (raw floor-map pixels) ----------------
            var f0 = floors[0];
            f0.Spawn("start", 770, 740);                 // just inside the entrance
            f0.Spawn("caught", 965, 480);                // where Donnie walks off to after the receptionist shoos him
            f0.Spawn("stairs_zone", 1400, 385);
            f0.Spawn("terminal_zone", 890, 400);
            f0.Spawn("recep_desk", 660, 404);
            float[][] route = { new float[] { 600, 432 }, new float[] { 592, 702 }, new float[] { 980, 702 }, new float[] { 990, 452 }, new float[] { 992, 424 } };
            for (int i = 0; i < route.Length; i++) f0.Spawn("recep_p" + (i + 1), route[i][0], route[i][1]);

            // the beige employee terminal drawn on the right end of the reception desk
            var terminal = f0.Trigger("EmployeeTerminal", 890, 378, 64, 44).AddComponent<ScriptedInteractable>();
            terminal.Configure("employee_terminal", "Use employee terminal", "The employee terminal.");
            terminal.RequireFreeHands();
            f0.Person("receptionist", "Receptionist", "char_receptionist_walk", 660, 404, Vector2.up);
            f0.Person("hall_a", "Employee", "char_npc_walk", 520, 362, Vector2.down);
            f0.Person("hall_b", "Employee", "char_npc2_walk", 700, 704, Vector2.left);
            f0.Person("hall_c", "Employee", "char_npc3_walk", 1205, 322, Vector2.down);
            // the story changes what Donnie thinks here once he knows about Betty
            f0.Trigger("Entrance", 769, 780, 360, 20).AddComponent<ScriptedInteractable>()
                .Configure("entrance", "Look", "The main entrance.");

            // Floors 1-3: reception desks
            float[] deskY = { 0, 310, 322, 262 };
            floors[1].Trigger("Reception", 770, deskY[1], 220, 20).AddComponent<DialogueInteractable>().Configure("Look", "", "The reception desk. Empty.");

            // Floor 3: the server room (right-hand block); the story checks the code before it opens
            floors[3].Spawn("server_door", 1362, 742);
            // the door and the keypad beside it (right of the door) are one hotspot
            floors[3].Trigger("Door_server", 1372, 712, 112, 30).AddComponent<Door>().Configure("server", "from_floor3", "Enter server room");

            BuildFloor2Hall(floors[2]);
            BuildFloor3Hall(floors[3]);
            foreach (var f in floors) rooms.Add(f.Finish());

            // ---------------- room interiors ----------------
            var interiors = new Dictionary<string, MapBuilder>();
            foreach (var path in Directory.GetFiles($"{CollisionDir}/rooms", "*.json"))
            {
                var data = Load(path.Replace('\\', '/'));
                var r = new MapBuilder(data.id, "rooms/" + data.id + ".png", data, data.scale);
                r.SetActorScale(data.actor > 0 ? data.actor : data.scale / FloorScale); // Donnie is drawn bigger in zoomed-in rooms
                foreach (var e in data.exits)
                {
                    r.Spawn("door" + e.index, e.spawn[0], e.spawn[1]);
                    r.TriggerRect("Exit" + e.index, e.hotspot).AddComponent<Door>().Configure(data.floor, $"{data.id}_door{e.index}", "Leave");
                }
                interiors[data.id] = r;
            }

            // Floor 0 rooms (coordinates in raw floor-map pixels)
            var office = interiors["f0_office"];   // Dan's office
            var danPc = office.TriggerOnMap("DanPC", 1240, 646, 76, 26).AddComponent<ScriptedInteractable>();
            danPc.Configure("dan_pc", "Use computer", "Dan's computer.");
            danPc.RequireFreeHands();
            office.PersonOnMap("dan", "Dan", "char_dan_walk", 1102, 572, Vector2.right);
            office.PersonOnMap("ryker", "Ryker", "char_ryker_walk", 1092, 634, Vector2.up);
            office.SpawnOnMap("npc_mid", 1078, 592);
            office.SpawnOnMap("npc_exit", 1050, 567);
            office.SpillOnMap("dan_office_spill", 1396, 590);

            var closet = interiors["f0_closet"];
            closet.TriggerOnMap("Locker", 229, 272, 60, 24).AddComponent<ScriptedInteractable>()
                .Configure("locker", "Open locker", "My locker.");
            // the bin of janitor gear drawn in the closet: using it brings out Donnie's cart
            closet.TriggerOnMap("JanitorBin", 160, 283, 50, 24).AddComponent<ScriptedInteractable>()
                .Configure("closet_bin", "Get your gear", "Mops, buckets, bin bags.");
            closet.SpawnOnMap("cart_spawn", 168, 312);
            closet.CartOnMap(168, 312);
            var lounge = interiors["f0_lounge"];
            lounge.TriggerOnMap("VendingMachine", 276, 760, 70, 26).AddComponent<ScriptedInteractable>()
                .Configure("vending", "Use vending machine", "A vending machine.");
            lounge.PersonOnMap("lounge_a", "Employee", "char_npc2_walk", 160, 497, Vector2.down);
            lounge.PersonOnMap("lounge_b", "Employee", "char_npc3_walk", 274, 506, Vector2.down);

            BuildFloor1(floors[1], interiors);
            BuildFloor2Rooms(interiors);
            BuildFloor3Rooms(interiors);

            foreach (var r in interiors.Values) rooms.Add(r.Finish());

            // ---------------- server room ----------------
            var serverData = Load($"{CollisionDir}/server.json");
            var server = new MapBuilder("server", "server.png", serverData, ServerScale);
            server.Spawn("from_floor3", 1250, 720);
            server.Trigger("Exit", 1250, 648, 110, 30).AddComponent<Door>().Configure("floor3", "server_door", "Leave server room");
            // the GyaraDB console: the desk, top left. Every ending runs through it (EndingStory).
            var console = server.Trigger("srv_gyaradb", 503, 362, 170, 40).AddComponent<ScriptedInteractable>();
            console.Configure("srv_gyaradb", "Use GyaraDB console", "Nothing here.");
            console.RequireFreeHands();
            server.PropOnMap("GyaraDbPc", "props/props_computers/pc_front_0.png", 470, 262, 1.3f);
            // who walks in at the end: in through the door, round the racks, up to the desk
            server.Spawn("walk_in_0", 1250, 720);
            server.Spawn("walk_in_1", 1100, 720);
            server.Spawn("walk_in_2", 1100, 340);
            // where they stop: the open floor right of the desk, above the racks (guard, CEO, Liam, guard)
            server.Spawn("walk_stand_0", 695, 342);
            server.Spawn("walk_stand_1", 760, 334);
            server.Spawn("walk_stand_2", 825, 338);
            server.Spawn("walk_stand_3", 895, 344);
            server.Person("srv_ceo", "CEO", "char_ceo_walk", 1250, 720, Vector2.left).gameObject.SetActive(false);
            server.Person("srv_liam", "Liam", "char_liam_walk", 1250, 720, Vector2.left).gameObject.SetActive(false);
            server.Person("srv_guard_1", "Officer", "char_security_walk", 1250, 720, Vector2.left).gameObject.SetActive(false);
            server.Person("srv_guard_2", "Officer", "char_security_walk", 1250, 720, Vector2.left).gameObject.SetActive(false);
            rooms.Add(server.Finish());

            // ---------------- managers ----------------
            new GameObject("RoomManager").AddComponent<RoomManager>().Configure(rooms, "floor0", "start", player.transform, follow);
            var art = new List<Sprite>(ArtImporter.LoadFolder("portraits"));
            art.AddRange(ArtImporter.LoadFolder("cutscenes"));
            art.AddRange(ArtImporter.LoadFolder("icons"));   // item icons (ItemIcons)
            art.AddRange(System.Array.FindAll(ArtImporter.LoadFolder("ui"), s => s.name == "logo_g"));   // the GyaraCorp app's icon
            new GameObject("ArtLibrary").AddComponent<ArtLibrary>().Configure(art.ToArray());
            new GameObject("DialogueUI").AddComponent<DialogueUI>();
            new GameObject("TerminalUI").AddComponent<TerminalUI>();
            new GameObject("ElevatorUI").AddComponent<ElevatorUI>();
            new GameObject("PhoneUI").AddComponent<PhoneUI>();
            new GameObject("CutsceneUI").AddComponent<CutsceneUI>();
            new GameObject("KeypadUI").AddComponent<KeypadUI>();
            new GameObject("LinkStationUI").AddComponent<LinkStationUI>();
            new GameObject("GyaraDbUI").AddComponent<GyaraDbUI>();
            new GameObject("HudUI").AddComponent<HudUI>();
            new GameObject("BarkUI").AddComponent<BarkUI>();
            new GameObject("GameOverUI").AddComponent<GameOverUI>();
            new GameObject("PauseMenuUI").AddComponent<PauseMenuUI>();
            new GameObject("ComputerWatchUI").AddComponent<ComputerWatchUI>();
            var bookGo = new GameObject("TheoryBookUI");
            bookGo.SetActive(false);   // Awake reads the guide, so configure it first
            bookGo.AddComponent<TheoryBookUI>().Configure(Asset(TheoryPath));
            bookGo.SetActive(true);
            var darkness = new GameObject("Darkness").AddComponent<Darkness>();
            var spotGo = new GameObject("TorchSpot");
            spotGo.transform.SetParent(darkness.transform, false);
            var spot = spotGo.AddComponent<Light2D>();
            spot.lightType = Light2D.LightType.Point;
            spot.pointLightInnerRadius = 0.2f;
            spot.pointLightOuterRadius = 1.6f;
            spot.intensity = 1.6f;
            spot.color = new Color(1f, 0.95f, 0.75f);
            spot.enabled = false;
            darkness.Configure(beam, glow, spot);
            var story0 = new GameObject("Floor0Story").AddComponent<Floor0Story>();
            var tut = new TextAsset[5];
            for (int i = 0; i < tut.Length; i++) tut[i] = Puzzle("f0_tut_" + (i + 1));
            story0.Configure(Asset("Assets/_Project/Data/Dialogue/floor0.json"), Puzzle("f0_dan_pc"), tut);
            var story1 = new GameObject("Floor1Story").AddComponent<Floor1Story>();
            var f1Puzzles = new List<TextAsset>();
            foreach (var id in new[] { "f1_sw_bookings", "f1_se_tickets", "f1_a_doorcodes", "f1_b_inbox", "f1_c_invoices", "f1_d_budget", "f1_e_archive", "f1_f_keys", "f1_m_systems" })
                f1Puzzles.Add(Puzzle(id));
            var tut1 = new TextAsset[5];
            for (int i = 0; i < tut1.Length; i++) tut1[i] = Puzzle("f1_tut_" + (i + 1));
            story1.Configure(Asset("Assets/_Project/Data/Dialogue/floor1.json"), f1Puzzles.ToArray(), tut1);
            var story2 = new GameObject("Floor2Story").AddComponent<Floor2Story>();
            var f2Puzzles = new List<TextAsset>();
            foreach (var id in new[] { "f2_t1_northgate", "f2_t2_remote", "f2_t3_plants", "f2_t4_locks", "f2_iv_restructure", "f2_reception_logins" })
                f2Puzzles.Add(Puzzle(id));
            var tut2 = new TextAsset[5];
            for (int i = 0; i < tut2.Length; i++) tut2[i] = Puzzle("f2_tut_" + (i + 1));
            story2.Configure(Asset("Assets/_Project/Data/Dialogue/floor2.json"), f2Puzzles.ToArray(), tut2);
            var story3 = new GameObject("Floor3Story").AddComponent<Floor3Story>();
            var f3Puzzles = new List<TextAsset>();
            foreach (var id in new[] { "f3_p1_rota", "f3_p2_cover", "f3_p3_code" }) f3Puzzles.Add(Puzzle(id));
            var tut3 = new TextAsset[3];
            for (int i = 0; i < tut3.Length; i++) tut3[i] = Puzzle("f3_tut_" + (i + 1));
            story3.Configure(Asset("Assets/_Project/Data/Dialogue/floor3.json"), f3Puzzles.ToArray(), tut3);
            var ending = new GameObject("EndingStory").AddComponent<EndingStory>();
            ending.Configure(Asset("Assets/_Project/Data/Dialogue/endings.json"));
            new GameObject("StoryDirector").AddComponent<StoryDirector>().Configure(story0, story1, story2, story3, ending, Asset("Assets/_Project/Data/Dialogue/cutscenes.json"));
            Debug.Log($"Art library: {art.Count} portraits and illustrations");

            EditorSceneManager.SaveScene(scene, ScenePath);
            BuildTitle();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(TitlePath, true), new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"Floors built: {ScenePath} ({rooms.Count} rooms)");
        }

        // ---------------------------------------------------------------- Floor 1 (Administration)

        private struct LockInfo { public LockKind kind; public string value, name, line; }

        /// <summary>Locked rooms: by key item, keypad code, or a story flag. All coordinates below are raw map pixels.</summary>
        private static readonly Dictionary<string, LockInfo> Locks = new Dictionary<string, LockInfo>
        {
            { "f1_office_a", new LockInfo { kind = LockKind.Key, value = "key_f1_a", name = "Office A", line = "Locked. 'Office A - Grace Lee'. She's off sick today." } },
            { "f1_office_b", new LockInfo { kind = LockKind.Key, value = "code_f1_b", name = "Office B", line = "A keypad. 'Office B - Joey Marsh'. I don't know the code." } },
            { "f1_office_c", new LockInfo { kind = LockKind.Key, value = "key_f1_c", name = "Office C", line = "Locked. 'Office C - Omar Haddad, Accounts'." } },
            { "f1_office_d", new LockInfo { kind = LockKind.Key, value = "code_f1_d", name = "Office D", line = "A keypad. 'Office D - Priya Nair, Accounts'. I don't know the code." } },
            { "f1_office_e", new LockInfo { kind = LockKind.Key, value = "code_f1_e", name = "Office E", line = "A keypad. 'Office E - Ken Watts, Records'. I don't know the code." } },
            { "f1_office_f", new LockInfo { kind = LockKind.Key, value = "key_f1_f", name = "Office F", line = "Locked. 'Office F - Tomas Reyes, Procurement'." } },
            { "f1_manager", new LockInfo { kind = LockKind.Key, value = "key_f1_m", name = "Manager Linda's office", line = "Locked. 'Linda Park - Office Manager'. Even the door sounds disappointed in me." } },
            { "f1_meeting", new LockInfo { kind = LockKind.Story, value = "f1_meeting_open", name = "Meeting room", line = "Through the glass: Manager Linda and Ken, deep in the quarterly review. I'm not walking in there." } },
            { "f3_ceo", new LockInfo { kind = LockKind.Story, value = "f3_ceo_open", name = "CEO's office", line = "Locked. 'Chief Executive Officer'. Through the frosted glass he's pacing, phone to his ear, like it owes him money." } },
        };

        private static void BuildFloor1(MapBuilder hall, Dictionary<string, MapBuilder> rooms)
        {
            hall.Person("f1_hall_a", "Employee", "char_npc_walk", 640, 420, Vector2.down);
            hall.Person("f1_hall_b", "Employee", "char_npc2_walk", 900, 712, Vector2.left);

            // Linda and Ken, after the tape: they come out of the meeting room and wait outside the gents'
            hall.Person("linda_hall", "Linda", "char_npc2_walk", 380, 296, Vector2.left).gameObject.SetActive(false);
            hall.Person("ken_hall", "Ken", "char_npc3_walk", 384, 318, Vector2.left).gameObject.SetActive(false);
            hall.Spawn("meeting_door_out", 380, 300);
            hall.Spawn("hall_corner", 330, 352);
            hall.Spawn("hall_turn_a", 226, 352);
            hall.Spawn("hall_turn_b", 248, 352);
            hall.Spawn("wash_wait_a", 226, 478);
            hall.Spawn("wash_wait_b", 248, 502);

            // computers (big triggers over the desk, so any side of it works) and things to search
            void Pc(string room, string id, float x, float y, float w, float h)
            {
                var s = rooms[room].TriggerOnMap("PC_" + id, x, y, w, h).AddComponent<ScriptedInteractable>();
                s.Configure(id, "Use computer", "The screen's locked.");
                s.MarkSnooping();
            }
            void Spot(string room, string id, string prompt, float x, float y, float w, float h, bool snoop)
            {
                var s = rooms[room].TriggerOnMap(id, x, y, w, h).AddComponent<ScriptedInteractable>();
                s.Configure(id, prompt, "Nothing here.");
                if (snoop) s.MarkSnooping();
            }
            Pc("f1_office_sw", "f1_pc_sw", 321, 858, 96, 90);
            Pc("f1_office_se", "f1_pc_se", 1112, 858, 118, 90);
            Pc("f1_office_a", "f1_pc_a", 363, 466, 85, 80);
            Pc("f1_office_b", "f1_pc_b", 359, 610, 85, 70);
            Pc("f1_office_c", "f1_pc_c", 532, 466, 85, 80);
            Pc("f1_office_d", "f1_pc_d", 532, 604, 85, 80);
            Pc("f1_office_e", "f1_pc_e", 1029, 460, 112, 78);
            Pc("f1_office_f", "f1_pc_f", 1029, 608, 112, 74);
            Pc("f1_manager", "f1_pc_m", 1381, 476, 108, 80);
            Spot("f1_office_sw", "f1_sw_drawer", "Search cabinet", 244, 850, 16, 60, true);
            Spot("f1_office_se", "f1_se_drawer", "Search cabinet", 1030, 862, 14, 60, true);
            Spot("f1_office_b", "f1_b_drawer", "Search drawers", 298, 616, 16, 50, true);   // grey filing cabinet, left wall
            Spot("f1_office_d", "f1_d_drawer", "Search drawers", 482, 586, 12, 40, true);
            Spot("f1_office_e", "f1_e_shelf", "Search shelf", 1134, 438, 12, 50, true);
            Spot("f1_manager", "f1_m_shelf", "Search shelf", 1285, 467, 40, 12, true);   // Linda's bookshelf, left wall
            Spot("f1_lounge", "f1_locker", "Search shelf", 1283, 638, 40, 16, false);
            Spot("f1_closet", "f1_bin", "Get your gear", 73, 898, 44, 16, false);
            Spot("f1_wash_m", "f1_toilet", "End cubicle", 72, 460, 60, 14, false);
            rooms["f1_closet"].SpawnOnMap("cart_spawn", 76, 800);

            // the black employee PC sprite on every office desk (placed over the monitors drawn in the map),
            // so computers look the same on every floor. (x, y) = the monitor's base, raw map px.
            (string room, float x, float y)[] pcs =
            {
                ("f1_office_a", 361, 452), ("f1_office_b", 356, 603), ("f1_office_c", 529, 449), ("f1_office_d", 529, 591),
                ("f1_office_e", 1039, 450), ("f1_office_f", 1037, 603), ("f1_manager", 1381, 459),
                ("f1_office_sw", 312, 845), ("f1_office_sw", 429, 845), ("f1_office_se", 1121, 845), ("f1_office_se", 1251, 845),
            };
            foreach (var pc in pcs) rooms[pc.room].PropOnMap("PcProp", "props/props_computers/pc_front_0.png", pc.x, pc.y, 1.3f);

            // the employee terminal sits on the side desk in the meeting room
            var term = rooms["f1_meeting"].TriggerOnMap("EmployeeTerminal", 302, 164, 46, 14).AddComponent<ScriptedInteractable>();
            term.Configure("f1_terminal", "Use employee terminal", "The employee terminal.");
            term.RequireFreeHands();
            rooms["f1_meeting"].PropOnMap("TerminalProp", "props/props_computers/terminal_front_0.png", 312, 141);   // on the cabinet top, beside the plant
            rooms["f1_meeting"].PersonOnMap("linda", "Linda", "char_npc2_walk", 150, 290, Vector2.up);
            rooms["f1_meeting"].PersonOnMap("ken", "Ken", "char_npc3_walk", 250, 290, Vector2.up);

            // the lounge: Joey 'hot-desking', and a colleague
            rooms["f1_lounge"].PersonOnMap("joey", "Joey", "char_npc_walk", 1310, 664, Vector2.right);
            rooms["f1_lounge"].PersonOnMap("f1_lounge_b", "Employee", "char_npc2_walk", 1452, 652, Vector2.left);

            // patrols: route stops (raw px), which way they look there, and how long they stay.
            // Every patroller has one long stop facing away from the computers: that's the window to use them.
            Patrol("f1_office_sw", rooms["f1_office_sw"], "f1_guard_sw", "Employee", "char_npc_walk", 2.8f,   // turns to the right-hand shelf
                new[] { (510f, 908f, 1f, 0f, 9f), (372f, 908f, 0f, 1f, 2.2f), (240f, 908f, 0f, 1f, 2.2f), (372f, 908f, 0f, 1f, 2.2f) });
            Patrol("f1_office_se", rooms["f1_office_se"], "f1_guard_se", "Employee", "char_npc2_walk", 2.8f,  // turns to the right-hand bookshelf
                new[] { (1325f, 908f, 1f, 0f, 9f), (1180f, 908f, 0f, 1f, 2.2f), (1040f, 908f, 0f, 1f, 2.2f), (1180f, 908f, 0f, 1f, 2.2f) });
            Patrol("f1_office_c", rooms["f1_office_c"], "omar", "Omar", "char_npc3_walk", 2.2f,               // stares out of the window (up, right of the desk)
                new[] { (605f, 466f, 0f, 1f, 9f), (600f, 506f, -1f, 0f, 2.2f), (482f, 506f, 0f, 1f, 2.2f), (600f, 506f, -1f, 0f, 2.2f) });
            Patrol("f1_office_f", rooms["f1_office_f"], "tomas", "Tomas", "char_npc_walk", 2.2f,              // on a supplier call by the right wall
                new[] { (1150f, 600f, 1f, 0f, 9f), (1145f, 655f, -1f, 0f, 2.2f), (945f, 655f, 0f, 1f, 2.2f), (1145f, 655f, -1f, 0f, 2.2f) });
        }

        // ---------------------------------------------------------------- Floor 2 (Human Resources)

        private const string UnlitPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        /// <summary>The corridor: reception, the interview queue, Head of HR's rounds, the long hallway, hiding places, Ryker and security.</summary>
        private static void BuildFloor2Hall(MapBuilder hall)
        {
            var unlit = AssetDatabase.LoadAssetAtPath<Material>(UnlitPath);
            hall.Spawn("hr_start", 460, 300);
            hall.Spawn("ryker_start", 1096, 300);
            hall.Spawn("recep_shoo", 765, 345);
            hall.Spawn("stairs_top", 1405, 150);      // Ryker comes down the staircase right of the lift (lie branch)
            hall.Spawn("stairs_bottom", 1405, 330);
            hall.Spawn("ryker_stop", 1250, 335);

            // reception: the duty list lives on this computer
            var recep = hall.Trigger("PC_recep", 765, 302, 150, 26).AddComponent<ScriptedInteractable>();
            recep.Configure("f2_pc_recep", "Use reception computer", "The reception computer.");
            recep.MarkSnooping();
            hall.PropOnMap("PcProp", "props/props_computers/pc_back_0.png", 745, 268, 1.3f);
            var receptionist = hall.Person("f2_recep", "Receptionist", "char_receptionist_walk", 765, 238, Vector2.down);
            var rg = receptionist.gameObject.AddComponent<Guard>();
            rg.Configure(new[] { hall.WorldOnMap(765, 238), hall.WorldOnMap(690, 238) }, new[] { Vector2.down, Vector2.left },
                new[] { 3f, 9f }, hall.WorldOnMap(765, 345), 2.4f);

            // the long hallway: employee terminal on the centre table, the torch on the right-hand table
            var term = hall.Trigger("EmployeeTerminal", 639, 912, 70, 14).AddComponent<ScriptedInteractable>();
            term.Configure("f2_terminal", "Use employee terminal", "The employee terminal.");
            term.RequireFreeHands();
            hall.PropOnMap("TerminalProp", "props/props_computers/terminal_front_0.png", 639, 878);
            var torch = hall.Trigger("Torch", 912, 912, 80, 14).AddComponent<ScriptedInteractable>();
            torch.Configure("f2_flashlight", "Take torch", "A torch.");

            // hiding places (only usable in the dark): lockers, and the big pot plants (eyes peek out)
            (float x, float y)[] lockers = { (437, 912), (482, 912), (793, 912), (834, 912) };
            for (int i = 0; i < lockers.Length; i++)
            {
                var h = hall.Trigger("HideLocker" + i, lockers[i].x, lockers[i].y, 34, 16).AddComponent<HideSpot>();
                h.Configure("locker" + i, HideSpot.Kind.Locker, null);
            }
            (float x, float y, float ex, float ey)[] plants =
            {
                (272, 922, 272, 880), (739, 914, 739, 878), (996, 916, 996, 878),
                (565, 456, 565, 405), (771, 456, 771, 405), (565, 690, 565, 640), (771, 690, 771, 640),
                (632, 262, 632, 228), (905, 262, 905, 228), (998, 264, 998, 232), (1239, 268, 1239, 236), (1338, 324, 1338, 290),
            };
            var eyesSprite = SaveEyesSprite();
            for (int i = 0; i < plants.Length; i++)
            {
                var h = hall.Trigger("HidePlant" + i, plants[i].x, plants[i].y, 40, 14).AddComponent<HideSpot>();
                var eyes = new GameObject("Eyes");
                eyes.transform.SetParent(h.transform, false);
                eyes.transform.position = hall.WorldOnMap(plants[i].ex, plants[i].ey);
                var esr = eyes.AddComponent<SpriteRenderer>();
                esr.sprite = eyesSprite;
                esr.sortingOrder = 20000;
                if (unlit != null) esr.sharedMaterial = unlit;
                h.Configure("plant" + i, HideSpot.Kind.Plant, eyes);
            }

            // the interview queue on the sofas, and others about the floor
            // standing in front of the sofas (on the lawn side, where the seats face), not on them
            hall.Person("f2_candidate", "Candidate", "char_npc_walk", 557, 530, Vector2.right);
            hall.Person("f2_wait_a", "Candidate", "char_npc2_walk", 557, 595, Vector2.right);
            hall.Person("f2_wait_b", "Candidate", "char_npc3_walk", 645, 446, Vector2.down);
            hall.Person("f2_wait_c", "Candidate", "char_npc_walk", 695, 446, Vector2.down);
            hall.Person("f2_wait_d", "Candidate", "char_npc2_walk", 780, 545, Vector2.left);
            hall.Person("f2_hall", "Employee", "char_npc3_walk", 460, 935, Vector2.up);
            hall.Person("f2_lift", "Employee", "char_npc2_walk", 1160, 300, Vector2.up);

            Vector3[] R(params (float x, float y)[] pts)
            {
                var v = new Vector3[pts.Length];
                for (int i = 0; i < pts.Length; i++) v[i] = hall.WorldOnMap(pts[i].x, pts[i].y);
                return v;
            }

            // Head of HR: rounds of the whole floor; seen without the cart = chased; caught = game over
            var hr = hall.Person("f2_hr", "Head of HR", "char_hr_walk", 460, 300, Vector2.right);
            var hrh = hr.gameObject.AddComponent<Hunter>();
            hrh.Configure(Hunter.Mode.Hr, R((465, 325), (880, 325), (1240, 325), (880, 325), (870, 722), (1400, 722), (870, 722), (465, 722)),
                1.5f, 3.1f, 4.5f);
            hrh.enabled = false;

            // the lie: Ryker. Walks a beat that never goes near the interview room (right side, long hallway, top);
            // faster than Donnie once he sees him. Torch flashes he can see lead him somewhere else.
            var ryker = hall.Person("ryker_f2", "Ryker", "char_ryker_walk", 1200, 348, Vector2.left);
            var rh = ryker.gameObject.AddComponent<Hunter>();
            rh.Configure(Hunter.Mode.Ryker, R((900, 325), (1440, 330), (1440, 722), (1060, 722), (1060, 925), (300, 930), (1060, 925), (1060, 722), (880, 722)),
                2.2f, 4.35f, 6f);
            rh.SetUnlit(unlit);
            rh.enabled = false;
            ryker.Talkable = false;

            // the truth: security, with torches, walking rounds
            var routes = new[]
            {
                R((460, 320), (880, 320), (880, 720), (460, 720)),
                R((250, 770), (880, 770), (1060, 790), (1060, 925), (980, 930), (260, 930), (195, 925), (195, 775)),
                R((1100, 325), (560, 325)),   // the one by the elevator wanders off to the left now and then
            };
            for (int i = 0; i < routes.Length; i++)
            {
                var start = routes[i][0];
                var sec = hall.Person("f2_sec_" + (i + 1), "Security", "char_security_walk", 0, 0, Vector2.down);
                sec.transform.position = start;
                var sh = sec.gameObject.AddComponent<Hunter>();
                sh.Configure(Hunter.Mode.Security, routes[i], 1.4f, 3.3f, 3.6f);
                sh.SetUnlit(unlit);
                if (i == 2) sh.SetStopWaits(4f, 7f);
                sh.enabled = false;
                sec.Talkable = false;
            }
        }

        /// <summary>Floor 2's rooms: conference (projector, plant), interview room, printer room, closet, HR open office, breaker room.</summary>
        private static void BuildFloor2Rooms(Dictionary<string, MapBuilder> rooms)
        {
            ScriptedInteractable Spot(string room, string id, string prompt, float x, float y, float w, float h)
            {
                var s = rooms[room].TriggerOnMap(id, x, y, w, h).AddComponent<ScriptedInteractable>();
                s.Configure(id, prompt, "Nothing here.");
                return s;
            }

            // conference room: the onboarding video on the projector; something in the plant pot
            Spot("f2_conference", "f2_projector", "Watch the projector", 205, 170, 110, 18);
            Spot("f2_conference", "f2_plant", "Search plant", 81, 216, 36, 14);
            rooms["f2_conference"].PersonOnMap("f2_conf", "Employee", "char_npc3_walk", 315, 300, Vector2.left);

            // interview room: the interviewer fetches files from the bookcase (back turned): that's the window
            var iv = rooms["f2_interview"];
            Spot("f2_interview", "f2_pc_iv", "Use computer", 235, 482, 150, 70).MarkSnooping();
            iv.PropOnMap("PcProp", "props/props_computers/pc_back_0.png", 235, 487, 1.3f);
            iv.PersonOnMap("f2_iv_candidate", "Candidate", "char_npc_walk", 242, 528, Vector2.up);
            Patrol2("f2_interview", iv, "f2_interviewer", "Interviewer", "char_npc2_walk", 2.6f,
                new[] { (330f, 515f, -1f, 0f, 3f), (330f, 553f, 0f, -1f, 0.3f), (110f, 553f, 0f, 1f, 0.3f), (110f, 505f, 0f, 1f, 9f), (110f, 553f, 0f, 1f, 0.3f), (330f, 553f, 0f, -1f, 0.3f) });

            // printer room
            Spot("f2_printer", "f2_printer", "Check the printer", 142, 690, 70, 14);
            rooms["f2_printer"].PersonOnMap("f2_printer_guy", "Employee", "char_npc_walk", 236, 700, Vector2.left);

            // janitor's closet
            Spot("f2_closet", "f2_bin", "Get your gear", 88, 922, 50, 12);
            rooms["f2_closet"].SpawnOnMap("cart_spawn", 100, 934);

            // HR open office: four tables, four computers; someone walks the room
            var office = rooms["f2_office"];
            (string id, float x, float y)[] tables = { ("f2_pc_t1", 1025, 497), ("f2_pc_t2", 1190, 497), ("f2_pc_t3", 1025, 589), ("f2_pc_t4", 1190, 589) };
            foreach (var t in tables)
            {
                Spot("f2_office", t.id, "Use computer", t.x, t.y, 120, 92).MarkSnooping();
                office.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", t.x - 15, t.y - 8, 1.3f);
            }
            office.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 1412, 462, 1.3f);
            office.PersonOnMap("f2_office_a", "Employee", "char_npc3_walk", 1340, 560, Vector2.right);
            office.PersonOnMap("f2_office_b", "Employee", "char_npc_walk", 1484, 600, Vector2.left);
            Patrol2("f2_office", office, "f2_office_staff", "HR Officer", "char_npc2_walk", 2.8f,
                new[] { (1310f, 460f, 1f, 0f, 9f), (1310f, 650f, -1f, 0f, 2.2f), (1100f, 650f, 0f, 1f, 2.2f), (950f, 650f, 0f, 1f, 2.2f), (1100f, 650f, 0f, 1f, 2.2f), (1310f, 650f, -1f, 0f, 2.2f) });

            // breaker room
            var panel = Spot("f2_breaker", "f2_breaker_panel", "Use breaker panel", 1180, 922, 80, 14);
            panel.RequireFreeHands();
        }

        // ---------------------------------------------------------------- Floor 3 (Executive Offices)

        /// <summary>
        /// The corridor: reception (employee terminal), Liam by the windows, the open office's two computers, coin plants,
        /// hiding plants, security and an assistant (cart = fine), and the blackout's hunters (the CEO and three guards).
        /// </summary>
        private static void BuildFloor3Hall(MapBuilder hall)
        {
            var unlit = AssetDatabase.LoadAssetAtPath<Material>(UnlitPath);
            Vector3[] R(params (float x, float y)[] pts)
            {
                var v = new Vector3[pts.Length];
                for (int i = 0; i < pts.Length; i++) v[i] = hall.WorldOnMap(pts[i].x, pts[i].y);
                return v;
            }
            ScriptedInteractable Spot(string id, string prompt, float x, float y, float w, float h)
            {
                var s = hall.Trigger(id, x, y, w, h).AddComponent<ScriptedInteractable>();
                s.Configure(id, prompt, "Nothing here.");
                return s;
            }

            // the employee terminal: the light-blue spot on the reception desk
            var term = Spot("f3_terminal", "Use employee terminal", 770, 314, 80, 14);
            term.RequireFreeHands();
            hall.PropOnMap("TerminalProp", "props/props_computers/terminal_front_0.png", 770, 268);

            // the open office: two computers on the desks
            Spot("f3_pc_open_l", "Use computer", 715, 795, 108, 92).MarkSnooping();
            Spot("f3_pc_open_r", "Use computer", 853, 795, 108, 92).MarkSnooping();
            hall.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 722, 780, 1.3f);
            hall.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 847, 780, 1.3f);

            // coins in two of the plant pots out here (the third is in the lounge)
            Spot("f3_plant_coin_1", "Search plant", 1340, 331, 34, 12);   // by the stairs
            Spot("f3_plant_coin_2", "Search plant", 50, 867, 34, 12);     // Liam's corner

            // Liam, pacing by the windows
            hall.Spawn("liam_a", 92, 862);
            hall.Spawn("liam_b", 160, 862);
            var liam = hall.Person("f3_liam", "Liam", "char_liam_walk", 125, 862, Vector2.up);
            var lh = liam.gameObject.AddComponent<Hunter>();
            lh.Configure(Hunter.Mode.Liam, R((125, 862)), 1.5f, 3.5f, 4.5f);   // he follows Donnie anyway; the cone is just what he sees
            lh.SetUnlit(unlit);
            lh.enabled = false;

            // hiding places (only in the dark, or with Liam after him): plants, eyes peeking out
            (float x, float y, float ex, float ey)[] plants =
            {
                (637, 262, 637, 215), (905, 262, 905, 215), (1005, 262, 1005, 220), (1235, 262, 1235, 220),
                (645, 748, 645, 712), (770, 628, 770, 590), (1483, 868, 1483, 832),
            };
            var eyesSprite = SaveEyesSprite();
            for (int i = 0; i < plants.Length; i++)
            {
                var h = hall.Trigger("HidePlant" + i, plants[i].x, plants[i].y, 40, 14).AddComponent<HideSpot>();
                var eyes = new GameObject("Eyes");
                eyes.transform.SetParent(h.transform, false);
                eyes.transform.position = hall.WorldOnMap(plants[i].ex, plants[i].ey);
                var esr = eyes.AddComponent<SpriteRenderer>();
                esr.sprite = eyesSprite;
                esr.sortingOrder = 20000;
                if (unlit != null) esr.sharedMaterial = unlit;
                h.Configure("f3_plant" + i, HideSpot.Kind.Plant, eyes);
            }

            // before the blackout: security and an assistant. They only mind a janitor without his cart.
            Vector3 lift = hall.WorldOnMap(1110, 290);
            void HallGuard(string id, string name, string sheet, float range, Vector3 shoo, (float x, float y, float lx, float ly, float wait)[] stops)
            {
                var npc = hall.Person(id, name, sheet, stops[0].x, stops[0].y, new Vector2(stops[0].lx, stops[0].ly));
                var points = new Vector3[stops.Length];
                var looks = new Vector2[stops.Length];
                var waits = new float[stops.Length];
                for (int i = 0; i < stops.Length; i++)
                {
                    points[i] = hall.WorldOnMap(stops[i].x, stops[i].y);
                    looks[i] = new Vector2(stops[i].lx, stops[i].ly);
                    waits[i] = stops[i].wait;
                }
                npc.gameObject.AddComponent<Guard>().Configure(points, looks, waits, shoo, range);
            }
            HallGuard("f3_guard_a", "Security", "char_security_walk", 2.8f, lift,
                new[] { (440f, 335f, 0f, -1f, 3f), (1000f, 335f, 0f, -1f, 2.2f), (1440f, 335f, 0f, 1f, 9f), (1000f, 335f, 0f, -1f, 2.2f) });
            HallGuard("f3_guard_b", "Security", "char_security_walk", 2.8f, lift,
                new[] { (1207f, 420f, 0f, 1f, 2.2f), (1207f, 868f, -1f, 0f, 2.2f), (700f, 870f, 0f, 1f, 9f), (1207f, 868f, 0f, 1f, 2.2f) });
            HallGuard("f3_assistant", "Assistant", "char_npc2_walk", 2.6f, hall.WorldOnMap(560, 872),
                new[] { (650f, 868f, 1f, 0f, 2.2f), (930f, 868f, -1f, 0f, 2.2f), (790f, 735f, 0f, 1f, 9f) });

            // the blackout: the CEO (tracks Donnie like Ryker did) and three guards with torches
            var ceo = hall.Person("f3_ceo_hunt", "CEO", "char_ceo_walk", 380, 292, Vector2.right);
            var ch = ceo.gameObject.AddComponent<Hunter>();
            ch.Configure(Hunter.Mode.Ryker, R((440, 330), (1000, 330), (1440, 330), (1207, 420), (1207, 868), (1360, 868), (1360, 745),
                (1360, 868), (470, 870), (440, 640), (600, 640), (640, 420), (440, 330)), 2.0f, 4.1f, 5.5f);
            ch.SetUnlit(unlit);
            ch.enabled = false;
            ceo.Talkable = false;
            var routes = new[]
            {
                R((1440, 330), (440, 330)),
                R((640, 400), (900, 400), (900, 640), (640, 640)),
                R((1440, 868), (440, 870), (440, 640), (440, 870), (60, 870), (440, 870)),
            };
            (float x, float y)[] starts = { (1405, 330), (900, 640), (1360, 800) };
            for (int i = 0; i < routes.Length; i++)
            {
                var sec = hall.Person("f3_sec_" + (i + 1), "Security", "char_security_walk", starts[i].x, starts[i].y, Vector2.down);
                var sh = sec.gameObject.AddComponent<Hunter>();
                sh.Configure(Hunter.Mode.Security, routes[i], 1.4f, 3.3f, 3.6f);
                sh.SetUnlit(unlit);
                if (i == 0) sh.SetStopWaits(3f, 5f);
                sh.enabled = false;
                sec.Talkable = false;
            }
        }

        /// <summary>Floor 3's rooms: CEO's office, boardroom (two computers), small meeting room (locker), executive offices, closet, lounge.</summary>
        private static void BuildFloor3Rooms(Dictionary<string, MapBuilder> rooms)
        {
            ScriptedInteractable Spot(string room, string id, string prompt, float x, float y, float w, float h)
            {
                var s = rooms[room].TriggerOnMap(id, x, y, w, h).AddComponent<ScriptedInteractable>();
                s.Configure(id, prompt, "Nothing here.");
                return s;
            }

            // the CEO's office: locked until the blackout. Him pacing; his keycard and his laptop on the desk.
            var ceoRoom = rooms["f3_ceo"];
            ceoRoom.PersonOnMap("f3_ceo", "CEO", "char_ceo_walk", 305, 250, Vector2.down);
            ceoRoom.SpawnOnMap("ceo_a", 305, 192);
            ceoRoom.SpawnOnMap("ceo_b", 305, 300);
            Spot("f3_ceo", "f3_ceo_desk", "Search desk", 145, 262, 30, 12);
            Spot("f3_ceo", "f3_ceo_pc", "Read laptop", 270, 205, 14, 50);
            // the endings cut here: the CEO with Liam (and security, to take him away)
            ceoRoom.SpawnOnMap("scene_ceo", 300, 250);
            ceoRoom.SpawnOnMap("scene_liam", 215, 295);
            ceoRoom.SpawnOnMap("scene_sec", 255, 330);
            ceoRoom.PersonOnMap("f3_liam_ceo", "Liam", "char_liam_walk", 215, 295, Vector2.right).gameObject.SetActive(false);
            ceoRoom.PersonOnMap("f3_sec_ceo", "Security", "char_security_walk", 255, 330, Vector2.left).gameObject.SetActive(false);

            // boardroom: a computer on each table
            var board = rooms["f3_board"];
            Spot("f3_board", "f3_pc_board_long", "Use computer", 326, 494, 196, 116).MarkSnooping();
            Spot("f3_board", "f3_pc_board_side", "Use computer", 550, 490, 112, 150).MarkSnooping();
            board.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 325, 478, 1.3f);
            board.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 550, 470, 1.3f);

            // the small meeting room: the locker by the door, and where Liam ends up looking
            var round = rooms["f3_round"];
            round.TriggerOnMap("HideLocker", 396, 660, 30, 12).AddComponent<HideSpot>().Configure("f3_locker", HideSpot.Kind.Locker, null);
            round.SpawnOnMap("liam_far", 212, 690);
            var liamIn = round.PersonOnMap("f3_liam_in", "Liam", "char_liam_walk", 395, 690, Vector2.left);
            liamIn.gameObject.SetActive(false);

            // Executive office A (orange): the link station, the drawer with the pen drive, the emergency cabinet
            var a = rooms["f3_exec_a"];
            Spot("f3_exec_a", "f3_link", "Use link station", 1030, 462, 112, 80).RequireFreeHands();
            Spot("f3_exec_a", "f3_drawer", "Search drawer", 988, 511, 26, 10);
            Spot("f3_exec_a", "f3_torch", "Open cabinet", 1134, 445, 14, 50);
            a.PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 1040, 452, 1.3f);

            // Executive office B: the code parts
            Spot("f3_exec_b", "f3_pc_exec_b", "Use computer", 1030, 622, 112, 85).MarkSnooping();
            rooms["f3_exec_b"].PropOnMap("PcProp", "props/props_computers/pc_front_0.png", 1040, 605, 1.3f);

            // janitor's closet
            Spot("f3_closet", "f3_bin", "Get your gear", 530, 764, 50, 12);
            rooms["f3_closet"].SpawnOnMap("cart_spawn", 540, 785);

            // the lounge: hot coffee, a coin in the corner plant, an executive with opinions
            Spot("f3_lounge", "f3_vending", "Use vending machine", 1120, 754, 44, 14);
            Spot("f3_lounge", "f3_plant_coin_3", "Search plant", 1124, 804, 16, 34);
            rooms["f3_lounge"].PersonOnMap("f3_lounge", "Executive", "char_npc3_walk", 1018, 805, Vector2.right);
        }

        private static void Patrol2(string roomId, MapBuilder room, string id, string name, string sheet, float range, (float x, float y, float lx, float ly, float wait)[] stops)
        {
            var npc = room.PersonOnMap(id, name, sheet, stops[0].x, stops[0].y, new Vector2(stops[0].lx, stops[0].ly));
            var points = new Vector3[stops.Length];
            var looks = new Vector2[stops.Length];
            var waits = new float[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                points[i] = room.WorldOnMap(stops[i].x, stops[i].y);
                looks[i] = new Vector2(stops[i].lx, stops[i].ly);
                waits[i] = stops[i].wait;
            }
            var guard = npc.gameObject.AddComponent<Guard>();
            guard.Configure(points, looks, waits, room.SpawnWorld("door0"), range);
            guard.SetKickOut("floor2", roomId + "_door0");
        }

        /// <summary>The peeking-eyes sprite, saved as an asset so the scene can keep it.</summary>
        private static Sprite SaveEyesSprite()
        {
            const string path = "Assets/_Project/Art/Processed/hide_eyes.png";
            if (!File.Exists(path))
            {
                var s = HideSpot.MakeEyesSprite();
                File.WriteAllBytes(path, s.texture.EncodeToPNG());
            }
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp.textureType != TextureImporterType.Sprite || imp.spritePixelsPerUnit != 32f)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = 32f;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void Patrol(string roomId, MapBuilder room, string id, string name, string sheet, float range, (float x, float y, float lx, float ly, float wait)[] stops)
        {
            var npc = room.PersonOnMap(id, name, sheet, stops[0].x, stops[0].y, new Vector2(stops[0].lx, stops[0].ly));
            var points = new Vector3[stops.Length];
            var looks = new Vector2[stops.Length];
            var waits = new float[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                points[i] = room.WorldOnMap(stops[i].x, stops[i].y);
                looks[i] = new Vector2(stops[i].lx, stops[i].ly);
                waits[i] = stops[i].wait;
            }
            var guard = npc.gameObject.AddComponent<Guard>();
            guard.Configure(points, looks, waits, room.SpawnWorld("door0"), range);
            guard.SetKickOut("floor1", roomId + "_door0");   // thrown out: back in the corridor outside the office
        }

        private static TextAsset Puzzle(string id) => Asset($"Assets/_Project/Data/Puzzles/{id}.json");

        private static TextAsset Asset(string path)
        {
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) throw new FileNotFoundException("Missing data file: " + path);
            return asset;
        }

        private static LevelData Load(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null) throw new FileNotFoundException("Missing level data: " + assetPath);
            return JsonUtility.FromJson<LevelData>(asset.text);
        }

        /// <summary>One room (a floor corridor, an interior or the server room): background, walls, spawns and triggers.</summary>
        private sealed class MapBuilder
        {
            private readonly GameObject root;
            private readonly Transform spawns;
            private readonly Vector2 px;
            private readonly float scale;
            private readonly Vector2 origin;

            public MapBuilder(string id, string spriteFile, LevelData data, float mapScale)
            {
                scale = mapScale;
                origin = data.origin != null && data.origin.Length == 2 ? new Vector2(data.origin[0], data.origin[1]) : Vector2.zero;
                var sprite = ArtImporter.LoadSprite(spriteFile);
                if (sprite == null) throw new FileNotFoundException("Missing map art: " + spriteFile);
                px = new Vector2(sprite.rect.width, sprite.rect.height);

                root = new GameObject("Room_" + id);
                var bg = new GameObject("Background");
                bg.transform.SetParent(root.transform, false);
                var bgr = bg.AddComponent<SpriteRenderer>();
                bgr.sprite = sprite;
                bgr.sortingOrder = -10000;

                spawns = new GameObject("Spawns").transform;
                spawns.SetParent(root.transform, false);

                var room = root.AddComponent<Room>();
                room.roomId = id;
                room.bounds = new Rect(-px.x / 2f / Ppu, -px.y / 2f / Ppu, px.x / Ppu, px.y / Ppu);

                var walls = new GameObject("Walls");
                walls.transform.SetParent(root.transform, false);
                for (int i = 0; data.rects != null && i + 3 < data.rects.Length; i += 4)
                {
                    float x = data.rects[i], y = data.rects[i + 1], w = data.rects[i + 2], h = data.rects[i + 3];
                    var box = walls.AddComponent<BoxCollider2D>();
                    Vector3 c = P(x + w / 2f, y + h / 2f);
                    box.offset = new Vector2(c.x, c.y);
                    box.size = new Vector2(w * scale / Ppu + 0.02f, h * scale / Ppu + 0.02f);
                }
            }

            private Vector3 P(float x, float y) => new Vector3((x * scale - px.x / 2f) / Ppu, (px.y / 2f - y * scale) / Ppu, 0f);

            public void Spawn(string id, float x, float y)
            {
                var go = new GameObject(id);
                go.transform.SetParent(spawns, false);
                go.transform.position = P(x, y);
            }

            /// <summary>Trigger centred on (x, y) with size (w, h), all in raw pixels.</summary>
            public GameObject Trigger(string name, float x, float y, float w, float h)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = P(x, y);
                var box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(w * scale / Ppu, h * scale / Ppu);
                box.isTrigger = true;
                return go;
            }

            /// <summary>Trigger from a top-left [x, y, w, h] rectangle in raw pixels.</summary>
            public GameObject TriggerRect(string name, int[] r) => Trigger(name, r[0] + r[2] / 2f, r[1] + r[3] / 2f, r[2], r[3]);

            private float Actor => root.GetComponent<Room>().actorScale;

            /// <summary>
            /// Dims each room's interior on the corridor map, so you can't see much from outside; you see it
            /// (and the people in it) once you walk in. Rects are room floors; grown upward to cover the back wall.
            /// </summary>
            public void ShadeRooms(int[] rects)
            {
                if (rects == null) return;
                var px1 = ArtImporter.LoadSprite("room_shade_px.png");
                if (px1 == null) { Debug.LogWarning("room_shade_px.png missing"); return; }
                var parent = new GameObject("RoomShade").transform;
                parent.SetParent(root.transform, false);
                float unit = px1.bounds.size.x;
                for (int i = 0; i + 3 < rects.Length; i += 4)
                {
                    float x = rects[i] - 3, y = rects[i + 1] - 42, w = rects[i + 2] + 6, h = rects[i + 3] + 45;
                    var go = new GameObject("Shade");
                    go.transform.SetParent(parent, false);
                    go.transform.position = P(x + w / 2f, y + h / 2f);
                    go.transform.localScale = new Vector3(w * scale / Ppu / unit, h * scale / Ppu / unit, 1f);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = px1;
                    sr.color = new Color(0.02f, 0.02f, 0.05f, 0.62f);
                    sr.sortingOrder = -9990; // above the map, below people and props
                }
            }

            public void SpawnOnMap(string id, float x, float y) => Spawn(id, x - origin.x, y - origin.y);

            /// <summary>World position of a raw floor-map point inside this room.</summary>
            public Vector3 WorldOnMap(float x, float y) => P(x - origin.x, y - origin.y);

            /// <summary>World position of one of this room's spawns (e.g. "door0").</summary>
            public Vector3 SpawnWorld(string id)
            {
                var t = spawns.Find(id);
                return t != null ? t.position : root.transform.position;
            }

            /// <summary>A static prop sprite standing at (x, y) (raw map px), drawn at the room's character scale.</summary>
            public GameObject PropOnMap(string name, string sprite, float x, float y, float extraScale = 1f)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = WorldOnMap(x, y);
                go.transform.localScale = Vector3.one * Actor * extraScale;
                go.AddComponent<SpriteRenderer>().sprite = ArtImporter.LoadSprite(sprite);
                go.AddComponent<YSorter>();
                return go;
            }

            /// <summary>A person standing at (x, y): walk-cycle sprite, small solid feet collider, talkable.</summary>
            public Npc Person(string id, string displayName, string sheet, float x, float y, Vector2 facing)
            {
                var go = new GameObject("NPC_" + id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = P(x, y);
                go.transform.localScale = Vector3.one * Actor;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.45f, 0.3f);
                col.offset = new Vector2(0f, 0.15f);
                var body = new GameObject("Sprite");
                body.transform.SetParent(go.transform, false);
                var sr = body.AddComponent<SpriteRenderer>();
                var frames = ArtImporter.LoadCharacterFrames(sheet);
                int row = Mathf.Abs(facing.x) > Mathf.Abs(facing.y) ? (facing.x < 0 ? 2 : 3) : (facing.y > 0 ? 1 : 0);
                sr.sprite = frames[row * 4];
                var npc = go.AddComponent<Npc>();
                npc.Configure(id, displayName, facing, "...");
                body.AddComponent<SpriteWalkAnimator>().Configure(frames, sr, npc);
                go.AddComponent<YSorter>().isStatic = false;
                return npc;
            }

            public Npc PersonOnMap(string id, string displayName, string sheet, float x, float y, Vector2 facing) =>
                Person(id, displayName, sheet, x - origin.x, y - origin.y, facing);

            /// <summary>Donnie's janitor cart, hidden until the closet bin brings it out.</summary>
            public void CartOnMap(float x, float y)
            {
                var go = new GameObject("JanitorCart");
                go.transform.SetParent(root.transform, false);
                go.transform.position = P(x - origin.x, y - origin.y);
                go.transform.localScale = Vector3.one * Actor;
                var body = new GameObject("Sprite");
                body.transform.SetParent(go.transform, false);
                var sr = body.AddComponent<SpriteRenderer>();
                sr.sprite = ArtImporter.LoadSprite("props/props_janitor/cart_loaded.png");
                body.transform.localScale = Vector3.one * 0.65f; // the sheet drew it nearly as tall as Donnie; a cart is ~1 m
                var trigger = go.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.size = new Vector2(1.0f, 0.6f);
                trigger.offset = new Vector2(0f, 0.3f);
                go.AddComponent<JanitorCart>();
                go.AddComponent<YSorter>().isStatic = false;
                go.SetActive(false);
            }

            /// <summary>A puddle to mop (its sprite is generated at runtime).</summary>
            public void SpillOnMap(string id, float x, float y)
            {
                var go = new GameObject("Spill_" + id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = P(x - origin.x, y - origin.y);
                go.transform.localScale = Vector3.one * Actor;
                var body = new GameObject("Sprite");
                body.transform.SetParent(go.transform, false);
                body.AddComponent<SpriteRenderer>().sortingOrder = -9000; // on the floor, under everyone
                var trigger = go.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.size = new Vector2(0.9f, 0.45f);
                go.AddComponent<Spill>().Configure(id);
            }

            /// <summary>Like Trigger, but (x, y) are raw floor-map pixels; converted using the room's crop origin.</summary>
            public GameObject TriggerOnMap(string name, float x, float y, float w, float h) =>
                Trigger(name, x - origin.x, y - origin.y, w, h);

            public Transform Root => root.transform;

            public void SetActorScale(float s) => root.GetComponent<Room>().actorScale = Mathf.Round(s * 10f) / 10f;

            public Room Finish()
            {
                root.SetActive(false);
                return root.GetComponent<Room>();
            }
        }
    }
}
#endif

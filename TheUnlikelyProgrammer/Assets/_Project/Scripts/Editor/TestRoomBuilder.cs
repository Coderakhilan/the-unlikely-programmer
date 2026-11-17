#if UNITY_EDITOR
using System.IO;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Unlikely.EditorTools
{
    /// <summary>
    /// Builds a placeholder test room (colored boxes) so we can playtest movement and interaction.
    /// Menu: Unlikely > Build Test Room. Also callable from the command line with
    /// -executeMethod Unlikely.EditorTools.TestRoomBuilder.Build
    /// </summary>
    public static class TestRoomBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Placeholder/Square16.png";
        private const string ScenePath = "Assets/_Project/Scenes/Floor0_Test.unity";

        private const int RoomW = 24;   // interior size in tiles
        private const int RoomH = 14;

        [MenuItem("Unlikely/Build Test Room")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/_Project/Art/Placeholder");
            Directory.CreateDirectory("Assets/_Project/Scenes");

            var square = EnsureSquareSprite();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Global light so the lit 2D sprites are visible ---
            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            // --- Room ---
            var room = new GameObject("Room");
            MakeBox(room.transform, "Floor", square, new Color(0.22f, 0.25f, 0.32f), Vector2.zero, new Vector2(RoomW, RoomH), 0, false);
            float hx = RoomW / 2f + 0.5f, hy = RoomH / 2f + 0.5f;
            MakeBox(room.transform, "Wall_Top",    square, new Color(0.10f, 0.11f, 0.16f), new Vector2(0,  hy), new Vector2(RoomW + 2, 1), 1, true);
            MakeBox(room.transform, "Wall_Bottom", square, new Color(0.10f, 0.11f, 0.16f), new Vector2(0, -hy), new Vector2(RoomW + 2, 1), 1, true);
            MakeBox(room.transform, "Wall_Left",   square, new Color(0.10f, 0.11f, 0.16f), new Vector2(-hx, 0), new Vector2(1, RoomH), 1, true);
            MakeBox(room.transform, "Wall_Right",  square, new Color(0.10f, 0.11f, 0.16f), new Vector2( hx, 0), new Vector2(1, RoomH), 1, true);

            // --- Interactable props (placeholder boxes) ---
            var desk = MakeBox(room.transform, "Employee Computer", square, new Color(0.55f, 0.38f, 0.22f), new Vector2(4, 2), new Vector2(2, 1), 5, true);
            AssetDatabase.Refresh();
            var puzzleJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/Puzzles/f0_reception_roster.json");
            desk.AddComponent<ComputerTerminal>().Configure(puzzleJson, "Use computer");

            var bin = MakeBox(room.transform, "Trash Can", square, new Color(0.35f, 0.4f, 0.35f), new Vector2(-6, 3), new Vector2(1, 1), 5, true);
            bin.AddComponent<DialogueInteractable>().Configure("Inspect", "",
                "A trash can. Smells like old coffee.");

            var dan = MakeBox(room.transform, "Dan (placeholder NPC)", square, new Color(0.3f, 0.55f, 0.85f), new Vector2(-3, -3), new Vector2(0.9f, 1.4f), 5, true);
            dan.AddComponent<DialogueInteractable>().Configure("Talk", "Dan",
                "Oh, hey. Didn't see you there.",
                "Janitor, right? Don't touch my keyboard.");

            // --- Player ---
            var player = new GameObject("Donnie");
            player.transform.position = new Vector3(0, -1, 0);
            var rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = player.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.8f, 0.6f);
            col.offset = new Vector2(0, 0.3f);
            player.AddComponent<PlayerController>();
            var body = new GameObject("Sprite");
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0, 0.7f, 0);
            body.transform.localScale = new Vector3(0.9f, 1.4f, 1);
            var sr = body.AddComponent<SpriteRenderer>();
            sr.sprite = square;
            sr.color = new Color(0.95f, 0.6f, 0.15f);
            sr.sortingOrder = 10;

            // --- Camera (pixel perfect, 320x180 reference, 16 px per unit) ---
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 180f / 2f / 16f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.transform.position = new Vector3(0, 0, -10);
            var ppc = camGo.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = 16;
            ppc.refResolutionX = 320;
            ppc.refResolutionY = 180;
            camGo.AddComponent<CameraFollow>().target = player.transform;

            // --- UI ---
            new GameObject("DialogueUI").AddComponent<DialogueUI>();
            new GameObject("TerminalUI").AddComponent<TerminalUI>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Test room built: " + ScenePath);
        }

        private static GameObject MakeBox(Transform parent, string name, Sprite sprite, Color color,
            Vector2 pos, Vector2 size, int order, bool solid)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (solid) go.AddComponent<BoxCollider2D>();
            return go;
        }

        private static Sprite EnsureSquareSprite()
        {
            if (!File.Exists(SpritePath))
            {
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var px = new Color32[16 * 16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }
    }
}
#endif

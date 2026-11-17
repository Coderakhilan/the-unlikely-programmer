#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Unlikely.EditorTools
{
    /// <summary>
    /// Applies the right import settings to everything the art pipeline (tools/process_art.py) puts in Art/Processed:
    /// 32 pixels per unit, point filtering, no compression. Character sheets are sliced into a 4x4 grid of sprites
    /// named {sheet}_{down|up|left|right}_{0-3}; props pivot at their feet; rooms pivot at the centre.
    /// Menu: Unlikely > Import Art.
    /// </summary>
    public static class ArtImporter
    {
        public const string Root = "Assets/_Project/Art/Processed";
        public const int PixelsPerUnit = 64; // 1 unit = 1 metre = 64 px (960x540 reference screen)
        private static readonly string[] Directions = { "down", "up", "left", "right" };

        [MenuItem("Unlikely/Import Art")]
        public static void ImportAll()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { Debug.LogWarning("No processed art found at " + Root); return; }
            AssetDatabase.Refresh();
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            foreach (var path in paths) Configure(path);
            AssetDatabase.SaveAssets();
            Debug.Log($"Art import: configured {paths.Count} textures.");
        }

        private static void Configure(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            string file = Path.GetFileNameWithoutExtension(path);
            bool isCharacter = file.StartsWith("char_");
            bool isCutscene = path.Contains("/cutscenes/") || path.Contains("/ui/");   // painted art: smooth filtering
            bool isRoom = file.StartsWith("room_") || file.StartsWith("floor") || file == "server" || path.Contains("/rooms/")
                          || isCutscene || path.Contains("/portraits/") || path.Contains("/icons/");

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = isCutscene ? FilterMode.Bilinear : FilterMode.Point; // story art is painted, not pixel-snapped
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            importer.spriteImportMode = isCharacter ? SpriteImportMode.Multiple : SpriteImportMode.Single;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)(isRoom ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            if (isCharacter) SliceCharacterSheet(path, file, importer);
        }

        private static void SliceCharacterSheet(string path, string file, TextureImporter importer)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            int cellW = tex.width / 4, cellH = tex.height / 4;
            Object.DestroyImmediate(tex);

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            var rects = new List<SpriteRect>();
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    rects.Add(new SpriteRect
                    {
                        name = $"{file}_{Directions[row]}_{col}",
                        // Unity's origin is bottom-left; the sheet's first row is at the top.
                        rect = new Rect(col * cellW, (3 - row) * cellH, cellW, cellH),
                        alignment = SpriteAlignment.BottomCenter,
                        pivot = new Vector2(0.5f, 0f),
                        spriteID = GUID.Generate()
                    });
                }
            }
            provider.SetSpriteRects(rects.ToArray());
            provider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>All sprites in a character sheet in row-major order (down, up, left, right x 4 frames).</summary>
        public static Sprite[] LoadCharacterFrames(string sheetName)
        {
            string path = $"{Root}/{sheetName}.png";
            var all = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            var frames = new Sprite[16];
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    all.TryGetValue($"{sheetName}_{Directions[row]}_{col}", out frames[row * 4 + col]);
            return frames;
        }

        public static Sprite LoadSprite(string relativePath) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/{relativePath}");

        /// <summary>Every single-sprite texture in a processed sub-folder (e.g. "portraits"), named by file.</summary>
        public static Sprite[] LoadFolder(string folder) =>
            AssetDatabase.FindAssets("t:Sprite", new[] { $"{Root}/{folder}" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).OrderBy(s => s.name).ToArray();
    }
}
#endif

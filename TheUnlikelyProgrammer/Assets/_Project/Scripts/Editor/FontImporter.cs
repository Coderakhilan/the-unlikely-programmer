using UnityEditor;
using UnityEngine;

namespace Unlikely.EditorTools
{
    /// <summary>
    /// The bundled fonts (Resources/Fonts): dynamic, and Barlow (the UI font) falls back to JetBrains Mono
    /// for the arrows and shapes it doesn't have (↑ ↓ → ▼ ● ✓ ...), before trying the computer's own fonts.
    /// </summary>
    public class FontImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Resources/Fonts/";
        private const string Fallback = Folder + "JetBrainsMono-Regular.ttf";

        private void OnPreprocessAsset()
        {
            if (!assetPath.StartsWith(Folder) || !(assetImporter is TrueTypeFontImporter f)) return;
            f.fontTextureCase = FontTextureCase.Dynamic;
            f.includeFontData = true;
            if (assetPath.Contains("Barlow"))
            {
                f.fontNames = new[] { "JetBrains Mono", "Segoe UI Symbol", "DejaVu Sans" };
                var mono = AssetDatabase.LoadAssetAtPath<Font>(Fallback);
                if (mono != null) f.fontReferences = new[] { mono };
            }
        }

        /// <summary>Barlow may import before JetBrains Mono exists: import it again once it does.</summary>
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p == Fallback)
                {
                    foreach (var b in new[] { "Regular", "Bold", "Italic", "BoldItalic" })
                        AssetDatabase.ImportAsset(Folder + "BarlowSemiCondensed-" + b + ".ttf", ImportAssetOptions.ForceUpdate);
                    return;
                }
        }
    }
}

using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unlikely.EditorTools
{
    /// <summary>
    /// The game's .exe / window icon (Art/AppIcon/app_icon.png: Donnie's face, cropped from art_raw/icon.png), every size.
    /// Menu: Unlikely > Set App Icon (Build Floors does it too).
    /// </summary>
    public static class AppIcon
    {
        private const string Full = "Assets/_Project/Art/AppIcon/app_icon.png";

        [MenuItem("Unlikely/Set App Icon")]
        public static void Apply()
        {
            var full = AssetDatabase.LoadAssetAtPath<Texture2D>(Full);
            if (full == null) { Debug.LogWarning("AppIcon: " + Full + " missing"); return; }
            var target = NamedBuildTarget.Standalone;
            int[] sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) icons[i] = full;
            PlayerSettings.SetIcons(target, icons, IconKind.Application);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { full }, IconKind.Any);   // the default icon
            AssetDatabase.SaveAssets();
            Debug.Log("AppIcon: set (" + string.Join(",", sizes) + ")");
        }
    }
}

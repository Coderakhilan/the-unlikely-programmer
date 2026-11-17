#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Unlikely.EditorTools
{
    /// <summary>
    /// The soundtrack (Resources/Music): streamed from disk while it plays (not held in memory), compressed, in the background.
    /// Copy a new track in as Resources/Music/&lt;id&gt;.mp3 (lower case, underscores) and it's set up on import.
    /// </summary>
    public class MusicImporter : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Music/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.loadInBackground = true;
            var s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.preloadAudioData = false;
            importer.defaultSampleSettings = s;
        }
    }
}
#endif

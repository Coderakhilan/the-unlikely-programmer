using System.Collections.Generic;
using UnityEngine;

namespace Unlikely.UI
{
    /// <summary>
    /// Sprites the UI looks up by name at runtime: dialogue portraits ("donnie_worried", "yuvie")
    /// and story illustrations ("capitalist_ending"). Filled by the scene builder.
    /// </summary>
    public class ArtLibrary : MonoBehaviour
    {
        [SerializeField] private Sprite[] sprites = new Sprite[0];

        private static ArtLibrary instance;
        private Dictionary<string, Sprite> byName;

        public void Configure(Sprite[] all) => sprites = all;

        private void Awake() => instance = this;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public static Sprite Get(string name)
        {
            if (instance == null || string.IsNullOrEmpty(name)) return null;
            if (instance.byName == null)
            {
                instance.byName = new Dictionary<string, Sprite>();
                foreach (var s in instance.sprites)
                    if (s != null) instance.byName[s.name.ToLowerInvariant()] = s;
            }
            instance.byName.TryGetValue(name.ToLowerInvariant(), out var sprite);
            return sprite;
        }

        /// <summary>Portrait for a speaker and expression; falls back to the neutral face, then none.</summary>
        public static Sprite Portrait(string speaker, string expression)
        {
            if (string.IsNullOrEmpty(speaker)) return null;
            string id = speaker.Trim().ToLowerInvariant();
            if (id.StartsWith("the ")) id = id.Substring(4);
            id = id.Split(' ')[0];
            if (!string.IsNullOrEmpty(expression))
            {
                var s = Get(id + "_" + expression);   // e.g. donnie_worried, donnie_worried_side
                if (s != null) return s;
            }
            return Get(id + "_neutral") ?? Get(id);
        }
    }
}

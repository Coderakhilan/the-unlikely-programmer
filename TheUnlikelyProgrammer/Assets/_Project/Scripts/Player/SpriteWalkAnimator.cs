using UnityEngine;

namespace Unlikely.Player
{
    /// <summary>Anything that walks with a 4-direction sheet: the player and NPCs.</summary>
    public interface IWalker
    {
        Vector2 Facing { get; }
        bool IsMoving { get; }
    }

    /// <summary>
    /// Plays a 4-direction walk cycle from a sprite sheet laid out as 4 rows
    /// (down, up, left, right) x 4 columns (idle, walk1, idle, walk2). frames[] is row-major, 16 sprites.
    /// </summary>
    public class SpriteWalkAnimator : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames = new Sprite[16];
        [SerializeField] private SpriteRenderer target;
        [SerializeField] private MonoBehaviour walkerSource; // must implement IWalker
        [SerializeField] private float framesPerSecond = 8f;

        [SerializeField] private Sprite[] altFrames = new Sprite[0]; // e.g. Donnie pushing his cart
        [SerializeField] private Sprite[] alt2Frames = new Sprite[0]; // e.g. Donnie holding his flashlight

        private IWalker walker;
        private float clock;

        /// <summary>Use the alternate sheet (Donnie with the cart) instead of the normal walk.</summary>
        public bool UseAlt { get; set; }

        /// <summary>The second alternate sheet (Donnie with the flashlight). The cart sheet wins if both are on.</summary>
        public bool UseAlt2 { get; set; }

        public void ConfigureAlt2(Sprite[] sheet) => alt2Frames = sheet;

        public void Configure(Sprite[] sheetFrames, SpriteRenderer renderer, MonoBehaviour source)
        {
            frames = sheetFrames;
            target = renderer;
            walkerSource = source;
        }

        public void ConfigureAlt(Sprite[] sheet) => altFrames = sheet;

        private void LateUpdate()
        {
            if (walker == null) walker = walkerSource as IWalker;
            var frames = UseAlt && altFrames != null && altFrames.Length >= 16 ? altFrames
                : UseAlt2 && alt2Frames != null && alt2Frames.Length >= 16 ? alt2Frames : this.frames;
            if (frames == null || frames.Length < 16 || target == null || walker == null) return;

            Vector2 f = walker.Facing;
            int row = Mathf.Abs(f.x) > Mathf.Abs(f.y) ? (f.x < 0 ? 2 : 3) : (f.y > 0 ? 1 : 0);

            int col = 0;
            if (walker.IsMoving)
            {
                clock += Time.deltaTime * framesPerSecond;
                col = (int)clock % 4;
            }
            else clock = 0f;

            target.sprite = frames[row * 4 + col];
        }
    }
}

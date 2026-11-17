using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// Top-down depth sorting: things lower on the screen (smaller y) draw in front.
    /// Put it on the root whose pivot is at the object's feet. Static props sort once; set isStatic = false for movers.
    /// </summary>
    public class YSorter : MonoBehaviour
    {
        public bool isStatic = true;
        private SpriteRenderer[] renderers;

        private void Awake() => renderers = GetComponentsInChildren<SpriteRenderer>();

        private void Start()
        {
            Apply();
            if (isStatic) enabled = false;
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            int order = -Mathf.RoundToInt(transform.position.y * 100f);
            // Stacked sprites (e.g. a monitor on a desk) keep their hierarchy order within the same depth.
            for (int i = 0; i < renderers.Length; i++) renderers[i].sortingOrder = order + i;
        }
    }
}

using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// Keeps the camera centered on a target, optionally clamped inside a room.
    /// If the room is smaller than the screen it is simply centered. The PixelPerfectCamera component handles pixel snapping.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public Vector2 targetOffset = new Vector2(0f, 0.9f); // roughly torso height above the feet pivot
        public bool useBounds;
        public Rect bounds;

        private Camera cam;
        private static float shakeUntil, shakeLength, shakeAmount;

        private void Awake() => cam = GetComponent<Camera>();

        /// <summary>A short camera shake (being caught): seconds, and how far in world units.</summary>
        public static void Shake(float seconds, float amount)
        {
            shakeLength = seconds;
            shakeUntil = Time.unscaledTime + seconds;
            shakeAmount = amount;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector2 p = (Vector2)target.position + targetOffset * target.localScale.y;

            if (useBounds)
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                p.x = bounds.width <= halfW * 2f ? bounds.center.x : Mathf.Clamp(p.x, bounds.xMin + halfW, bounds.xMax - halfW);
                p.y = bounds.height <= halfH * 2f ? bounds.center.y : Mathf.Clamp(p.y, bounds.yMin + halfH, bounds.yMax - halfH);
            }

            float left = shakeUntil - Time.unscaledTime;
            if (left > 0f && shakeLength > 0f)
            {
                // dies away over its length
                float k = shakeAmount * (left / shakeLength);
                p += new Vector2(Mathf.Sin(Time.unscaledTime * 71f), Mathf.Cos(Time.unscaledTime * 53f)) * k;
            }
            transform.position = new Vector3(p.x, p.y, -10f);
        }
    }
}

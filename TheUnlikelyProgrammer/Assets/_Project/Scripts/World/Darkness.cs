using System;
using Unlikely.Core;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Unlikely.World
{
    /// <summary>
    /// Power cuts and Donnie's flashlight. While the power is out on a floor (flag "power_out_floorN"),
    /// its corridor and rooms go almost black. With the flashlight in his pockets Donnie shines a beam the way
    /// he's facing (his sprite switches to the torch sheet). F flicks the beam at a spot ahead: anyone hunting
    /// him in the dark goes to look (see <see cref="Flashed"/>).
    /// </summary>
    public class Darkness : MonoBehaviour
    {
        public static Darkness Instance { get; private set; }

        /// <summary>Raised when Donnie flicks the torch at a spot (world position).</summary>
        public static event Action<Vector3> Flashed;

        [SerializeField] private Light2D beam;     // child of Donnie: the torch cone
        [SerializeField] private Light2D glow;     // child of Donnie: a little light around him, so he's visible
        [SerializeField] private Light2D spot;     // the flicked spot
        [SerializeField] private float darkIntensity = 0.07f;

        private Light2D global;
        private float spotTime, cooldown;
        private const float FlashReach = 4.5f;

        public static bool IsDark { get; private set; }

        public void Configure(Light2D beamLight, Light2D glowLight, Light2D spotLight)
        {
            beam = beamLight;
            glow = glowLight;
            spot = spotLight;
        }

        private void Awake() => Instance = this;

        public static string FloorOf(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (roomId.StartsWith("floor")) return roomId.Substring(5);
            if (roomId.Length > 2 && roomId[0] == 'f' && char.IsDigit(roomId[1])) return roomId.Substring(1, 1);
            return null;
        }

        private float nextLightSearch;
        private SpriteWalkAnimator playerAnim;
        private bool wasDark;

        private void Update()
        {
            // look for the room's global light at most twice a second (searching the scene every frame is wasteful)
            if (global == null && Time.unscaledTime >= nextLightSearch)
            {
                nextLightSearch = Time.unscaledTime + 0.5f;
                foreach (var l in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
                    if (l.lightType == Light2D.LightType.Global) { global = l; break; }
            }

            var rm = RoomManager.Instance;
            // the room on screen decides (a Peek into another room is lit, or not, as that room is)
            string floor = rm != null && rm.Viewed != null ? FloorOf(rm.Viewed.roomId) : null;
            bool away = rm != null && rm.IsPeeking;   // Donnie isn't in the shot: no torch, no glow round him
            IsDark = floor != null && GameState.Has("power_out_floor" + floor);
            // a cutscene in the CEO's office always has the lights on
            if (rm != null && rm.IsPeeking && rm.Viewed != null && rm.Viewed.roomId == "f3_ceo") IsDark = false;
            if (global != null) global.intensity = Mathf.MoveTowards(global.intensity, IsDark ? darkIntensity : 1f, Time.deltaTime * 3f);
            if (global != null) global.color = IsDark ? new Color(0.55f, 0.6f, 0.85f) : Color.white;

            var p = PlayerController.Instance;
            bool torch = IsDark && !away && Inventory.Has("flashlight") && !JanitorCart.Holding && !HideSpot.IsHidden;
            if (beam != null)
            {
                beam.enabled = torch;
                if (p != null && torch)
                {
                    var face = p.Facing;
                    beam.transform.localPosition = TorchLens(face);
                    beam.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(face.y, face.x) * Mathf.Rad2Deg - 90f);
                }
            }
            if (glow != null) glow.enabled = IsDark && !away && !HideSpot.IsHidden;
            if (p != null)
            {
                if (playerAnim == null) playerAnim = p.GetComponentInChildren<SpriteWalkAnimator>();
                if (playerAnim != null) playerAnim.UseAlt2 = torch;
            }


            if (spot != null)
            {
                spotTime -= Time.deltaTime;
                spot.enabled = spotTime > 0f;
                if (spot.enabled) spot.intensity = Mathf.Clamp01(spotTime / 0.6f) * 1.6f;
            }

            cooldown -= Time.deltaTime;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool f = (kb != null && kb.fKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (f && torch && cooldown <= 0f && p != null && !UiKit.AnyModalOpen && !PlayerController.Frozen
                && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen))
                Flash(p);
        }

        /// <summary>
        /// Where the torch's lens is in Donnie's hand, relative to his feet (metres, before his size scale).
        /// Measured on char_donnie_flash: held low at his side facing down/up, at waist height pointing ahead from the side.
        /// </summary>
        public static Vector3 TorchLens(Vector2 face)
        {
            if (face.x < -0.5f) return new Vector3(-0.42f, 0.86f, 0f);
            if (face.x > 0.5f) return new Vector3(0.42f, 0.86f, 0f);
            if (face.y > 0.5f) return new Vector3(0.26f, 0.62f, 0f);
            return new Vector3(-0.28f, 0.52f, 0f);
        }

        /// <summary>Flick the beam at a spot ahead (up to a wall).</summary>
        public void Flash(PlayerController p)
        {
            cooldown = 1.2f;
            float s = p.transform.localScale.x;
            Vector3 from = p.transform.position + TorchLens(p.Facing) * s;
            Vector3 dir = p.Facing;
            float reach = FlashReach * s;
            var grid = NavGrid.For(RoomManager.Instance.Current);
            Vector3 point = from + dir * reach;
            if (grid != null)
            {
                // stop at the first wall
                for (float d = 0.3f; d <= reach; d += 0.2f)
                {
                    var q = from + dir * d;
                    if (!grid.IsWalkable(q)) { point = from + dir * Mathf.Max(0.3f, d - 0.3f); break; }
                }
            }
            point.z = 0f;
            if (spot != null)
            {
                spot.transform.position = point;
                spotTime = 0.8f;
            }
            Flashed?.Invoke(point);
        }

        /// <summary>For tests and scripted moments.</summary>
        public static void RaiseFlash(Vector3 point) => Flashed?.Invoke(point);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() { Flashed = null; IsDark = false; }
    }
}

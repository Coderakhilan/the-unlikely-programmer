using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// The soundtrack (The Unlikely Programmer OST, in Resources/Music, one file per track id like "the_paper_trail").
    /// Four layers; the highest one that's set is what plays, cross-faded when it changes:
    ///   Scene  (a story moment: an ending, the opening's parts, the Ryker fight; set by {music:id} in dialogue,
    ///           until {music:stop} or a new scene)
    ///   Talk   (one conversation: the kidnappers' calls, Dan and Ryker's first talk; loops while they talk and fades out
    ///           into the floor's track once the conversation is over)
    ///   Phase  (while a sequence lasts: Floor 2's chases, Floor 3's blackout; the story director sets it every frame)
    ///   Base   (the place: the title, each floor's own track, looping)
    /// Caught (the CAUGHT screen): the music fades out and pauses; it carries on from the same spot on retry.
    /// Dialogue lines can carry cues the player never sees: "{music:the_final_query}" (scene), "{music:stop}" (end the scene
    /// track), "{music:once:the_usual_suspects}" (for this conversation), "{music:once:call}" (the kidnappers' theme, for a call
    /// Donnie makes himself). Edit them in Data/Dialogue/*.json.
    /// </summary>
    public class Music : MonoBehaviour
    {
        public const float Fade = 1.6f;
        /// <summary>How long after the last line of a conversation its music starts fading back (gaps between lines don't count).</summary>
        public const float TalkGrace = 0.8f;
        private static readonly Regex Cue = new Regex(@"\{music:(?:(once|talk):)?([a-z0-9_]+)\}\s*");

        private static Music instance;
        private static bool quitting;

        public static string Base { get; private set; }
        public static string Phase { get; private set; }
        public static string Scene { get; private set; }
        public static string Talk { get; private set; }
        /// <summary>The track that should be playing: what the four layers come to.</summary>
        public static string Current { get; private set; }
        /// <summary>The CAUGHT screen is up: the music is paused.</summary>
        public static bool Held { get; private set; }

        private static float lastTalking;
        private static int calls;

        private AudioSource a, b;   // cross-fade between the two
        private AudioSource live;
        private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        private static Music Instance
        {
            get
            {
                if (instance != null || quitting || !Application.isPlaying) return instance;   // (EditMode tests: just the logic)
                var go = new GameObject("Music");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Music>();
                return instance;
            }
        }

        private void Awake()
        {
            a = gameObject.AddComponent<AudioSource>();
            b = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { a, b }) { s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; s.loop = true; }
            live = a;
        }

        private void OnApplicationQuit() => quitting = true;

        // ------------------------------------------------------------------ the layers

        public static void SetBase(string id) { if (Base != id) { Base = id; Touch(); } }
        public static void SetPhase(string id) { if (Phase != id) { Phase = id; Touch(); } }
        public static void SetScene(string id) { if (Scene != id) { Scene = id; Touch(); } }

        /// <summary>A track for this conversation: it plays while they talk, then fades into the floor's track.</summary>
        public static void StartTalk(string id)
        {
            Talk = id;
            lastTalking = Time.unscaledTime;
            Touch();
        }

        /// <summary>The conversation is over (the music director calls this; tests too).</summary>
        public static void EndTalk()
        {
            if (Talk == null) return;
            Talk = null;
            Touch();
        }

        /// <summary>The kidnappers ring: The Usual Suspects, then Three's Company the next time, and so on.</summary>
        public static void KidnapperCall() => StartTalk(calls++ % 2 == 0 ? "the_usual_suspects" : "threes_company");

        /// <summary>A new scene (the title, a fresh game): only the base is kept.</summary>
        public static void ClearStory()
        {
            Scene = null;
            Phase = null;
            Talk = null;
            Held = false;
            Touch();
        }

        /// <summary>Takes any {music:...} cues out of a line (and acts on them). Returns the line as it should show.</summary>
        public static string ApplyCues(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf("{music:", System.StringComparison.Ordinal) < 0) return line;
            return Cue.Replace(line, m =>
            {
                string id = m.Groups[2].Value;
                if (m.Groups[1].Success && id == "call") KidnapperCall();   // a call Donnie makes himself
                else if (m.Groups[1].Success) StartTalk(id);
                else if (id == "stop") SetScene(null);
                else SetScene(id);
                return "";
            });
        }

        private static string Wanted() => Preview ?? Scene ?? Talk ?? Phase ?? Base;

        // ------------------------------------------------------------------ Extras > Soundtrack

        /// <summary>A track chosen on the Soundtrack page: above every other layer until cleared (null).</summary>
        public static string Preview { get; private set; }
        /// <summary>The soundtrack page's pause button.</summary>
        public static bool PreviewPaused { get; set; }

        public static void SetPreview(string id)
        {
            PreviewPaused = false;
            if (Preview == id) return;
            Preview = id;
            Touch();
        }

        /// <summary>The track now playing has reached its new clip (cross-fade started).</summary>
        public static bool PreviewReady => instance != null && instance.live != null && instance.live.clip != null && Preview != null
                                           && instance.live.clip.name == Preview;
        /// <summary>Seconds into the track now playing, and its length.</summary>
        public static float Position => instance != null && instance.live != null && instance.live.clip != null ? instance.live.time : 0f;
        public static float Length => instance != null && instance.live != null && instance.live.clip != null ? instance.live.clip.length : 0f;

        /// <summary>Jump to this many seconds into the track now playing.</summary>
        public static void Seek(float seconds)
        {
            if (instance == null || instance.live == null || instance.live.clip == null) return;
            instance.live.time = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, instance.live.clip.length - 0.1f));
        }

        private static void Touch()
        {
            Current = Wanted();
            if (instance == null && !quitting && Current != null) _ = Instance;
        }

        // ------------------------------------------------------------------ playback

        private AudioClip Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!cache.TryGetValue(id, out var clip))
            {
                clip = Resources.Load<AudioClip>("Music/" + id);
                if (clip == null) Debug.LogWarning("Music: no track " + id);
                cache[id] = clip;
            }
            return clip;
        }

        /// <summary>Someone is talking: a text box, a ringing or open phone, or the story pictures.</summary>
        private static bool Talking =>
            (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) ||
            (PhoneUI.Instance != null && PhoneUI.Instance.IsRinging) ||
            (CutsceneUI.Instance != null && CutsceneUI.Instance.IsPlaying);

        private void Update()
        {
            float now = Time.unscaledTime;

            // a conversation's track fades back into the floor's once nobody has said anything for a moment
            if (Talk != null)
            {
                if (Talking) lastTalking = now;
                else if (now - lastTalking > TalkGrace) EndTalk();
            }

            // caught: fade out and pause; on retry, carry on from the same spot
            Held = GameOverUI.Instance != null && GameOverUI.Instance.IsOpen;

            Current = Wanted();
            var clip = Load(Current);
            float vol = GameSettings.MusicVolume;
            if (live.clip != clip)
            {
                if (clip != null) Profile.HearTrack(Current);   // Extras > Soundtrack unlocks it
                // the other source fades the new track in while this one fades out
                var next = live == a ? b : a;
                next.clip = clip;
                next.volume = 0f;
                if (clip != null) next.Play();
                live = next;
            }
            float step = Time.unscaledDeltaTime / Fade * Mathf.Max(vol, 0.05f);
            foreach (var s in new[] { a, b })
            {
                bool wanted = s == live && s.clip != null && !Held && !(Preview != null && PreviewPaused);
                if (wanted && s.clip != null && !s.isPlaying) s.UnPause();   // back from CAUGHT
                s.volume = Mathf.MoveTowards(s.volume, wanted ? vol : 0f, Held ? step * 2.5f : PreviewPaused ? step * 4f : step);
                if (s.volume > 0f || !s.isPlaying) continue;
                if (s == live && (Held || PreviewPaused)) s.Pause();   // keep its place (the retry, or the soundtrack's pause)
                else if (s != live) s.Stop();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            instance = null;
            quitting = false;
            Base = Phase = Scene = Talk = Current = Preview = null;
            PreviewPaused = false;
            Held = false;
            calls = 0;
        }
    }
}

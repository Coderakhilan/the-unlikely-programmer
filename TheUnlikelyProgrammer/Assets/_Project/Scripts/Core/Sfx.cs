using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unlikely.Core
{
    /// <summary>
    /// Every sound in the game, synthesised in code at start-up (no audio files): UI blips, the dialogue "voice",
    /// footsteps, doors, keypad, computer, the phone's ring, pick-ups and the caught sting. (No background room tone: the
    /// author found it sounded like air or rain.)
    /// Sfx.Play(Sfx.Id.Confirm) from anywhere; volume follows Options > SFX volume.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public enum Id { Move, Confirm, Back, Blip, Step, Door, Pickup, Toast, Key, Error, Success, Caught, Tick, Type, Objective, Save, Lock }

        private const int Rate = 44100;
        private static Sfx instance;
        private static bool quitting;

        private readonly Dictionary<Id, AudioClip> clips = new Dictionary<Id, AudioClip>();
        private readonly Dictionary<Id, float> lastPlayed = new Dictionary<Id, float>();
        private AudioSource[] voices;
        private AudioSource ring;
        private int next;

        /// <summary>Master switch (tests can turn it off).</summary>
        public static bool Muted;

        private static Sfx Instance
        {
            get
            {
                if (instance != null || quitting) return instance;
                var go = new GameObject("Sfx");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Sfx>();
                return instance;
            }
        }

        private void Awake()
        {
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            voices = new AudioSource[6];
            for (int i = 0; i < voices.Length; i++) voices[i] = NewSource(false);
            ring = NewSource(true);
            Build();
            ring.clip = MakeRing();
        }

        private void OnApplicationQuit() => quitting = true;

        private AudioSource NewSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        /// <summary>Build every sound now (tests): returns each clip's name, length in samples and loudest sample.</summary>
        public static List<(string name, int samples, float peak)> Warm()
        {
            var s = Instance;
            var list = new List<(string, int, float)>();
            void Add(string name, AudioClip c)
            {
                var d = new float[c.samples];
                c.GetData(d, 0);
                float peak = 0f;
                foreach (float v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
                list.Add((name, c.samples, peak));
            }
            foreach (var kv in s.clips) Add(kv.Key.ToString(), kv.Value);
            Add("ring", s.ring.clip);
            return list;
        }

        // ------------------------------------------------------------------ playing

        /// <summary>Play a sound. minGap: ignore repeats of the same sound closer together than this (seconds).</summary>
        public static void Play(Id id, float volume = 1f, float pitch = 1f, float minGap = 0.02f)
        {
            if (Muted || Application.isBatchMode) return;
            var s = Instance;
            if (s == null || !s.clips.TryGetValue(id, out var clip)) return;
            float now = Time.unscaledTime;
            if (s.lastPlayed.TryGetValue(id, out float last) && now - last < minGap) return;
            s.lastPlayed[id] = now;
            var v = s.voices[s.next];
            s.next = (s.next + 1) % s.voices.Length;
            v.pitch = pitch;
            v.volume = volume * GameSettings.Volume;   // Options > SFX volume (music has its own)
            v.clip = clip;
            v.Play();
        }

        /// <summary>The dialogue "voice": a short blip per few letters, pitched by who's talking.</summary>
        public static void Voice(string speaker)
        {
            float pitch = 1f;
            if (!string.IsNullOrEmpty(speaker))
            {
                switch (speaker)
                {
                    case "Yuvie": pitch = 0.62f; break;
                    case "Irshad": pitch = 0.7f; break;
                    case "Adi": pitch = 1.15f; break;
                    case "Betty": pitch = 1.45f; break;
                    case "CEO": pitch = 0.75f; break;
                    case "Liam": pitch = 0.95f; break;
                    case "Ryker": pitch = 1.25f; break;
                    case "Dan": pitch = 0.82f; break;
                    case "Donnie": pitch = 0.88f; break;
                    default: pitch = 1.05f; break;
                }
            }
            else pitch = 0.88f;   // Donnie thinking
            Play(Id.Blip, 0.32f, pitch * UnityEngine.Random.Range(0.97f, 1.03f), 0.045f);
        }

        /// <summary>The phone ringing (loops until turned off).</summary>
        public static void Ring(bool on)
        {
            if (Application.isBatchMode) return;
            var s = Instance;
            if (s == null) return;
            if (on && !Muted) { if (!s.ring.isPlaying) { s.ring.volume = 0.5f * GameSettings.Volume; s.ring.Play(); } }
            else s.ring.Stop();
        }

        // ------------------------------------------------------------------ synthesis

        private void Build()
        {
            clips[Id.Move] = Tone(0.035f, t => Sine(1250, t) * 0.5f, 0.002f, 0.03f);
            clips[Id.Confirm] = Notes(new[] { 784f, 1175f }, 0.05f, 0.11f, Tri, 0.5f);
            clips[Id.Back] = Notes(new[] { 660f, 440f }, 0.05f, 0.1f, Tri, 0.45f);
            clips[Id.Blip] = Tone(0.04f, t => (Square(420, t) * 0.25f + Sine(420, t) * 0.5f), 0.002f, 0.035f);
            clips[Id.Step] = Noise(0.06f, 900f, 0.55f, 0.001f, 0.055f, 7);
            clips[Id.Door] = Mix(Noise(0.22f, 500f, 0.5f, 0.02f, 0.2f, 11), Tone(0.22f, t => Sine(70 + 40 * Mathf.Exp(-t * 30), t) * 0.7f, 0.002f, 0.2f));
            clips[Id.Pickup] = Notes(new[] { 1047f, 1319f, 1568f }, 0.045f, 0.16f, Tri, 0.42f);
            clips[Id.Toast] = Notes(new[] { 880f, 1175f }, 0.06f, 0.14f, Sine, 0.3f);
            clips[Id.Key] = Tone(0.06f, t => Sine(1480, t) * 0.4f + Sine(1976, t) * 0.2f, 0.002f, 0.055f);
            clips[Id.Error] = Tone(0.22f, t => Square(150, t) * 0.25f + Square(155, t) * 0.2f, 0.004f, 0.2f);
            clips[Id.Success] = Notes(new[] { 784f, 988f, 1175f, 1568f }, 0.06f, 0.2f, Tri, 0.42f);
            clips[Id.Caught] = Mix(Tone(0.7f, t => Saw(196 * Mathf.Exp(-t * 1.1f), t) * 0.3f + Sine(98, t) * 0.45f, 0.004f, 0.65f), Noise(0.25f, 1800f, 0.35f, 0.001f, 0.24f, 3));
            clips[Id.Tick] = Tone(0.03f, t => Sine(980, t) * 0.45f, 0.001f, 0.028f);
            clips[Id.Type] = Noise(0.022f, 3200f, 0.45f, 0.0005f, 0.02f, 5);
            clips[Id.Objective] = Notes(new[] { 659f, 988f, 1319f }, 0.07f, 0.25f, Sine, 0.32f);
            clips[Id.Save] = Notes(new[] { 1319f, 1760f }, 0.04f, 0.09f, Sine, 0.16f);
            clips[Id.Lock] = Mix(Noise(0.05f, 2400f, 0.6f, 0.0005f, 0.045f, 9), Tone(0.09f, t => Sine(220, t) * 0.5f, 0.001f, 0.08f));
        }

        private static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
        private static float Square(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t) >= 0f ? 1f : -1f;
        private static float Tri(float f, float t) => 2f * Mathf.Abs(2f * (t * f - Mathf.Floor(t * f + 0.5f))) - 1f;
        private static float Saw(float f, float t) => 2f * (t * f - Mathf.Floor(t * f + 0.5f));

        private static float Envelope(float t, float length, float attack, float decay)
        {
            float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
            float tail = Mathf.Clamp01((length - t) / Mathf.Max(0.0001f, decay));
            return a * tail * tail;
        }

        private static AudioClip Clip(float[] data, string name = "sfx")
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float[] ToneData(float length, Func<float, float> wave, float attack, float decay)
        {
            int n = Mathf.CeilToInt(length * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = wave(t) * Envelope(t, length, attack, decay);
            }
            return d;
        }

        private static AudioClip Tone(float length, Func<float, float> wave, float attack, float decay) => Clip(ToneData(length, wave, attack, decay));

        /// <summary>A few notes one after another (little jingles).</summary>
        private static AudioClip Notes(float[] freqs, float step, float noteLength, Func<float, float, float> wave, float gain)
        {
            int n = Mathf.CeilToInt((step * (freqs.Length - 1) + noteLength) * Rate);
            var d = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = Mathf.RoundToInt(k * step * Rate);
                int len = Mathf.CeilToInt(noteLength * Rate);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = i / (float)Rate;
                    d[start + i] += wave(freqs[k], t) * Envelope(t, noteLength, 0.003f, noteLength * 0.9f) * gain;
                }
            }
            return Clip(d);
        }

        private static float[] NoiseData(float length, float cutoff, float gain, float attack, float decay, int seed)
        {
            int n = Mathf.CeilToInt(length * Rate);
            var d = new float[n];
            var rng = new System.Random(seed);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate), y = 0f;
            for (int i = 0; i < n; i++)
            {
                y += a * ((float)rng.NextDouble() * 2f - 1f - y);   // one-pole low-pass
                float t = i / (float)Rate;
                d[i] = y * gain * 2.2f * Envelope(t, length, attack, decay);
            }
            return d;
        }

        private static AudioClip Noise(float length, float cutoff, float gain, float attack, float decay, int seed) =>
            Clip(NoiseData(length, cutoff, gain, attack, decay, seed));

        private static AudioClip Mix(AudioClip a, AudioClip b)
        {
            var da = new float[a.samples];
            var db = new float[b.samples];
            a.GetData(da, 0);
            b.GetData(db, 0);
            var d = new float[Mathf.Max(da.Length, db.Length)];
            for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp((i < da.Length ? da[i] : 0f) + (i < db.Length ? db[i] : 0f), -1f, 1f);
            return Clip(d);
        }

        /// <summary>A phone ring: two bursts of a warbling two-tone, then a pause (loops).</summary>
        private static AudioClip MakeRing()
        {
            float length = 2.0f;
            int n = Mathf.CeilToInt(length * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                bool on = (t < 0.4f) || (t > 0.55f && t < 0.95f);
                if (!on) continue;
                float local = t < 0.4f ? t : t - 0.55f;
                float warble = Mathf.Sin(2f * Mathf.PI * 22f * t) > 0f ? 1318f : 1568f;
                float edge = Mathf.Clamp01(local / 0.01f) * Mathf.Clamp01((0.4f - local) / 0.02f);
                d[i] = (Sine(warble, t) * 0.35f + Sine(warble * 2f, t) * 0.08f) * edge;
            }
            return Clip(d, "ring");
        }


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            instance = null;
            quitting = false;
            Muted = false;
        }
    }
}

using UnityEngine;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Synthesizes element-flavoured AudioClips at runtime for moves whose
    /// castSfx / hitSfx are unassigned. Cached per element/role so we don't
    /// re-synthesize on every move resolution. Each clip is short (≤0.6 s),
    /// mono, 22050 Hz, designed to be self-contained on Quest with zero
    /// external assets.
    /// </summary>
    public static class ProceduralMoveSfx
    {
        public enum Role { Cast, Hit }

        private const int SampleRate = 22050;

        private static readonly System.Collections.Generic.Dictionary<int, AudioClip> _cache
            = new System.Collections.Generic.Dictionary<int, AudioClip>();

        public static AudioClip Get(ElementType element, Role role)
        {
            int key = ((int)element << 1) | (int)role;
            if (_cache.TryGetValue(key, out var existing) && existing != null) return existing;

            var clip = Synth(element, role);
            _cache[key] = clip;
            return clip;
        }

        private static AudioClip Synth(ElementType element, Role role)
        {
            // Cast = anticipation (sweep up), Hit = impact (sweep down + body).
            // Per-element timbre keeps moves audibly distinct.
            switch (element)
            {
                case ElementType.Electric: return role == Role.Cast ? Zap(0.18f) : Crack(0.35f);
                case ElementType.Fire:     return role == Role.Cast ? Roar(0.30f, 320f, 180f) : Roar(0.40f, 240f, 90f);
                case ElementType.Water:    return role == Role.Cast ? Bubble(0.25f, 380f, 580f) : Splash(0.40f);
                case ElementType.Ice:      return role == Role.Cast ? Chime(0.25f, 1320f) : Chime(0.45f, 880f);
                case ElementType.Grass:    return role == Role.Cast ? Pluck(0.18f, 520f) : Pluck(0.30f, 320f);
                case ElementType.Earth:    return role == Role.Cast ? Rumble(0.30f) : Thud(0.40f);
                case ElementType.Dark:     return role == Role.Cast ? Growl(0.30f) : Growl(0.45f);
                case ElementType.Neutral:
                default:                   return role == Role.Cast ? Pop(0.15f, 660f) : Pop(0.25f, 440f);
            }
        }

        // ─── Voice generators ───────────────────────────────────────────
        private static AudioClip Zap(float duration)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float freq = Mathf.Lerp(900f, 2200f, p);
                float saw = (2f * (t * freq - Mathf.Floor(t * freq + 0.5f)));
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * 0.5f * t));
                float env = AdsrEnv(p, 0.05f, 0.1f, 0.6f, 0.25f);
                data[i] = (0.55f * saw + 0.45f * square) * env * 0.45f;
            }
            return MakeClip("ZapCast", data);
        }

        private static AudioClip Crack(float duration)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float noise = (Random.value * 2f - 1f);
                float freq = Mathf.Lerp(2400f, 600f, p);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                float env = Mathf.Pow(1f - p, 1.6f);
                data[i] = (0.7f * noise + 0.3f * tone) * env * 0.55f;
            }
            return MakeClip("ZapHit", data);
        }

        private static AudioClip Roar(float duration, float fStart, float fEnd)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float freq = Mathf.Lerp(fStart, fEnd, p);
                float noise = (Random.value * 2f - 1f);
                lp += (noise - lp) * 0.18f;
                float body = Mathf.Sin(2f * Mathf.PI * freq * t);
                float env = AdsrEnv(p, 0.08f, 0.15f, 0.7f, 0.4f);
                data[i] = (0.6f * lp + 0.4f * body) * env * 0.50f;
            }
            return MakeClip("Roar", data);
        }

        private static AudioClip Bubble(float duration, float fStart, float fEnd)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float freq = Mathf.Lerp(fStart, fEnd, Mathf.Sqrt(p));
                float wob = Mathf.Sin(2f * Mathf.PI * 18f * t) * 0.06f;
                float tone = Mathf.Sin(2f * Mathf.PI * freq * (t + wob));
                float env = AdsrEnv(p, 0.05f, 0.1f, 0.7f, 0.3f);
                data[i] = tone * env * 0.45f;
            }
            return MakeClip("Bubble", data);
        }

        private static AudioClip Splash(float duration)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float p = (float)i / n;
                float noise = (Random.value * 2f - 1f);
                lp += (noise - lp) * Mathf.Lerp(0.06f, 0.30f, p);
                float env = Mathf.Pow(1f - p, 1.2f);
                data[i] = lp * env * 0.55f;
            }
            return MakeClip("Splash", data);
        }

        private static AudioClip Chime(float duration, float fundamental)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float a = Mathf.Sin(2f * Mathf.PI * fundamental * t);
                float b = Mathf.Sin(2f * Mathf.PI * fundamental * 1.5f * t) * 0.5f;
                float c = Mathf.Sin(2f * Mathf.PI * fundamental * 2.0f * t) * 0.25f;
                float env = Mathf.Pow(1f - p, 1.4f);
                data[i] = (a + b + c) * env * 0.30f;
            }
            return MakeClip("Chime", data);
        }

        private static AudioClip Pluck(float duration, float fundamental)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float tone = Mathf.Sin(2f * Mathf.PI * fundamental * t)
                           + 0.5f * Mathf.Sin(2f * Mathf.PI * fundamental * 2f * t);
                float env = Mathf.Pow(1f - p, 2.2f);
                data[i] = tone * env * 0.42f;
            }
            return MakeClip("Pluck", data);
        }

        private static AudioClip Rumble(float duration)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float p = (float)i / n;
                float noise = (Random.value * 2f - 1f);
                lp += (noise - lp) * 0.06f;
                float env = AdsrEnv(p, 0.10f, 0.10f, 0.85f, 0.30f);
                data[i] = lp * env * 0.55f;
            }
            return MakeClip("Rumble", data);
        }

        private static AudioClip Thud(float duration)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float freq = Mathf.Lerp(180f, 80f, p);
                float body = Mathf.Sin(2f * Mathf.PI * freq * t);
                float noise = (Random.value * 2f - 1f) * 0.3f;
                float env = Mathf.Pow(1f - p, 1.2f);
                data[i] = (body + noise) * env * 0.55f;
            }
            return MakeClip("Thud", data);
        }

        private static AudioClip Growl(float duration)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float freq = 110f + 30f * Mathf.Sin(2f * Mathf.PI * 4f * t);
                float saw = (2f * (t * freq - Mathf.Floor(t * freq + 0.5f)));
                float noise = (Random.value * 2f - 1f) * 0.25f;
                float env = AdsrEnv(p, 0.08f, 0.15f, 0.75f, 0.30f);
                data[i] = (saw + noise) * env * 0.45f;
            }
            return MakeClip("Growl", data);
        }

        private static AudioClip Pop(float duration, float fundamental)
        {
            int n = SampleCount(duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;
                float tone = Mathf.Sin(2f * Mathf.PI * fundamental * t);
                float env = Mathf.Pow(1f - p, 1.8f);
                data[i] = tone * env * 0.40f;
            }
            return MakeClip("Pop", data);
        }

        // ─── Victory sting (battle KO) ──────────────────────────────────
        private static AudioClip _cachedVictorySting;
        public static AudioClip GetVictorySting()
        {
            if (_cachedVictorySting != null) return _cachedVictorySting;
            // 2-second rising C-major arpeggio that lands on a sustained
            // chord. Sawtooth-flavoured so it reads "brass fanfare" rather
            // than "synth pad" — punchy enough to cut through the fading
            // battle music.
            const float dur = 2.0f;
            int n = SampleCount(dur);
            var data = new float[n];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f }; // C5 E5 G5 C6
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / n;

                // Rising arpeggio in the first 1.0s — each note 0.25s.
                float arp = 0f;
                int noteIdx = Mathf.Clamp((int)(p * 4f), 0, 3);
                if (p < 0.85f)
                {
                    float f = notes[noteIdx];
                    float saw = (2f * (t * f - Mathf.Floor(t * f + 0.5f)));
                    float noteEnv = 1f - Mathf.Abs((p * 4f) - noteIdx - 0.5f) * 2f;
                    arp = saw * Mathf.Clamp01(noteEnv) * 0.55f;
                }
                // Sustained chord across the whole clip, swelling in.
                float chord = (
                    Mathf.Sin(2f * Mathf.PI * notes[0] * t) +
                    Mathf.Sin(2f * Mathf.PI * notes[1] * t) * 0.85f +
                    Mathf.Sin(2f * Mathf.PI * notes[2] * t) * 0.70f
                ) / 3f;
                float chordEnv = AdsrEnv(p, 0.10f, 0.10f, 0.85f, 0.20f);
                float chordSwell = Mathf.SmoothStep(0.20f, 1.0f, p);

                data[i] = (arp + chord * chordEnv * chordSwell) * 0.55f;
            }
            _cachedVictorySting = MakeClip("VictorySting", data);
            return _cachedVictorySting;
        }

        // ─── Helpers ────────────────────────────────────────────────────
        private static int SampleCount(float duration)
        {
            return Mathf.Max(64, Mathf.RoundToInt(SampleRate * duration));
        }

        // Attack-Decay-Sustain-Release envelope normalised over progress p∈[0,1].
        private static float AdsrEnv(float p, float a, float d, float s, float r)
        {
            if (p < a) return p / a;
            float ad = a + d;
            if (p < ad) return Mathf.Lerp(1f, s, (p - a) / d);
            float sr = 1f - r;
            if (p < sr) return s;
            return Mathf.Lerp(s, 0f, (p - sr) / r);
        }

        private static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

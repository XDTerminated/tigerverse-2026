using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Plays a clap / applause sound at a world position. Loads
    /// Resources/Sfx/Clap if a wav is present, otherwise synthesizes a
    /// short multi-clap burst using a two-band physical model:
    /// a sharp high-frequency transient (skin slap) plus a band-passed
    /// mid-frequency body (palm cavity resonance), which together read
    /// as real hands meeting rather than filtered noise.
    /// </summary>
    public static class ClapSfx
    {
        private static AudioClip _clip;
        private static bool _resolved;

        public static void Play(Vector3 worldPos, float volume = 0.85f)
        {
            var clip = GetClip();
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, worldPos, volume);
        }

        private static AudioClip GetClip()
        {
            if (_resolved) return _clip;
            _resolved = true;
            _clip = Resources.Load<AudioClip>("Sfx/Clap");
            if (_clip == null) _clip = Synthesize();
            return _clip;
        }

        private static AudioClip Synthesize()
        {
            const int sampleRate = 44100;
            const float duration = 1.0f;
            int total = (int)(sampleRate * duration);
            var data  = new float[total];
            var rng = new System.Random(20260428);

            // Three claps with slight timing jitter (real applause never
            // hits perfect 200ms intervals) and per-clap pitch / amplitude
            // variation so they don't sound like the same sample three times.
            float[] starts = { 0.00f, 0.205f, 0.418f };
            float[] amps   = { 1.00f, 0.88f, 0.74f };
            float[] pitch  = { 1.00f, 1.06f, 0.93f };  // cavity resonance shift

            for (int b = 0; b < starts.Length; b++)
                AddClap(data, sampleRate, (int)(starts[b] * sampleRate), amps[b], pitch[b], rng);

            // Soft saturation: rounds peaks, kills harshness, adds body.
            for (int i = 0; i < total; i++)
            {
                float x = data[i] * 1.2f;
                data[i] = (float)System.Math.Tanh(x);
            }

            // Normalize to ~0.92 peak.
            float peak = 0f;
            for (int i = 0; i < total; i++) if (Mathf.Abs(data[i]) > peak) peak = Mathf.Abs(data[i]);
            if (peak > 0f)
            {
                float g = 0.92f / peak;
                for (int i = 0; i < total; i++) data[i] *= g;
            }

            var clip = AudioClip.Create("ClapSynth", total, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Single clap = transient + body + early reflection. The transient
        // is a 1.5ms broadband impulse (the skin slap), the body is ~30ms
        // of band-passed noise centered on a cavity resonance freq, and
        // the early reflection is a 12ms-delayed quieter copy for room cue.
        private static void AddClap(float[] data, int sampleRate, int s0, float amp, float pitch, System.Random rng)
        {
            // ---- Transient: ultra-fast hi-freq impulse (~1.5ms) ----
            const float transientDur = 0.0015f;
            int tLen = (int)(transientDur * sampleRate);
            // Hipass through one-pole differential to keep only sparkle.
            float prev = 0f;
            for (int i = 0; i < tLen && (s0 + i) < data.Length; i++)
            {
                float t = i / (float)sampleRate;
                float env = 1f - (t / transientDur);   // fast linear ramp-down
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = n - prev * 0.6f;            // crude hipass
                prev = n;
                data[s0 + i] += hp * env * amp * 0.55f;
            }

            // ---- Body: band-passed noise (palm cavity resonance) ----
            float bodyAttack = 0.001f;
            float bodyDecay  = 0.028f * (1f / pitch); // higher pitch = faster decay (smaller cavity)
            float bodyDur    = bodyAttack + bodyDecay + 0.04f;
            int   bLen       = (int)(bodyDur * sampleRate);

            // Resonant bandpass via a state-variable filter centered around
            // 1500Hz * pitch. Real hand-claps peak in the 1.2–2.5kHz band.
            float fc = 1500f * pitch;
            float q  = 5.5f;                   // moderate resonance
            float f  = 2f * Mathf.Sin(Mathf.PI * fc / sampleRate);
            float qInv = 1f / q;
            float low = 0f, band = 0f, high;

            for (int i = 0; i < bLen && (s0 + i) < data.Length; i++)
            {
                float t = i / (float)sampleRate;
                float env;
                if (t < bodyAttack) env = t / bodyAttack;
                else                env = Mathf.Exp(-(t - bodyAttack) / bodyDecay);

                float input = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Two-pass SVF for steeper rolloff.
                low  += f * band;
                high  = input - low - qInv * band;
                band += f * high;
                low  += f * band;
                high  = input - low - qInv * band;
                band += f * high;

                data[s0 + i] += band * env * amp * 0.85f;
            }

            // ---- Early reflection: 12ms slap-back at ~25%. Implies a small room. ----
            int erDelay = (int)(0.012f * sampleRate);
            for (int i = 0; i < bLen && (s0 + erDelay + i) < data.Length; i++)
            {
                float src = data[s0 + i];
                data[s0 + erDelay + i] += src * 0.25f;
            }
        }
    }
}

using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Plays a clap / applause sound at a world position. Loads
    /// Resources/Sfx/Clap if a wav is present, otherwise synthesizes a
    /// short multi-burst clap with a band-limited mid-frequency body so
    /// it reads as a hand clap rather than a generic noise click.
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

        // Three-clap "small applause" burst. Each clap is band-passed white
        // noise (1.5–4 kHz peak) with a sharp attack, decaying tail, and
        // tiny early-reflection slap-back to imply a room. Reads as real
        // hands meeting, not a click.
        private static AudioClip Synthesize()
        {
            const int sampleRate = 44100;
            const float duration = 0.95f;
            int total = (int)(sampleRate * duration);
            var data  = new float[total];
            var rng = new System.Random(20260428);

            // Three claps spaced ~180–220ms apart with mild amplitude variance.
            float[] starts  = { 0.00f, 0.21f, 0.43f };
            float[] amps    = { 1.00f, 0.85f, 0.70f };

            for (int b = 0; b < starts.Length; b++)
                AddClap(data, sampleRate, (int)(starts[b] * sampleRate), amps[b], rng);

            // Soft tail: small reverberant ring across the whole window so it
            // doesn't feel anechoic. Just a tiny exponentially decaying noise
            // floor mixed in at very low level.
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)sampleRate;
                float room = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.015f * Mathf.Exp(-t * 4f);
                data[i] += room;
            }

            // Soft saturation to tame peaks and add warmth.
            for (int i = 0; i < total; i++)
            {
                float x = data[i];
                data[i] = Mathf.Sign(x) * (1f - Mathf.Exp(-Mathf.Abs(x) * 1.4f));
            }

            // Normalize gently.
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

        // Adds a single clap into `data` starting at sample s0 with given amp.
        private static void AddClap(float[] data, int sampleRate, int s0, float amp, System.Random rng)
        {
            const float attack  = 0.0008f;
            const float decay   = 0.045f;
            float dur = attack + decay + 0.06f;
            int len = (int)(dur * sampleRate);
            int end = Mathf.Min(data.Length, s0 + len);

            // Bandpass via simple state-variable filter (resonant, centered ~2.4kHz).
            // Two cascaded one-pole highpass + one-pole lowpass approximates a
            // bandpass cheaply without external DSP.
            float lp = 0f, hp = 0f;
            float lpA = ExpCoef(3500f, sampleRate);   // lowpass cutoff
            float hpA = ExpCoef(800f,  sampleRate);   // highpass cutoff

            for (int i = s0; i < end; i++)
            {
                float t = (i - s0) / (float)sampleRate;
                float env;
                if (t < attack) env = t / attack;
                else            env = Mathf.Exp(-(t - attack) / decay);

                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += lpA * (n - lp);
                float bp = lp - hp;
                hp += hpA * (lp - hp);
                data[i] += bp * env * amp * 0.95f;
            }

            // Slap-back early reflection ~14ms later, ~30% level. Cheap room cue.
            int slapOffset = (int)(0.014f * sampleRate);
            int slapEnd = Mathf.Min(data.Length, s0 + slapOffset + len);
            for (int i = s0 + slapOffset; i < slapEnd; i++)
            {
                float t = (i - (s0 + slapOffset)) / (float)sampleRate;
                float env;
                if (t < attack) env = t / attack;
                else            env = Mathf.Exp(-(t - attack) / decay);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] += n * env * amp * 0.28f;
            }
        }

        private static float ExpCoef(float cutoffHz, int sampleRate)
        {
            return 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / sampleRate);
        }
    }
}

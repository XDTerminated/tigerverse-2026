using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Plays a clap sound at a world position. Loads Resources/Sfx/Clap if
    /// a wav is present, otherwise synthesizes a short noise-burst clap so
    /// the gesture has audible feedback without any imported asset.
    /// </summary>
    public static class ClapSfx
    {
        private static AudioClip _clip;
        private static bool _resolved;

        public static void Play(Vector3 worldPos, float volume = 0.7f)
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

        // Two short noise bursts ~80ms apart, each with sharp attack and
        // exponential decay. Reads as a single hand-clap, not a click.
        private static AudioClip Synthesize()
        {
            const int sampleRate = 44100;
            const float duration = 0.22f;
            int total = (int)(sampleRate * duration);
            var data  = new float[total];

            // Burst envelope params.
            float[] burstStarts = { 0.00f, 0.085f };
            const float attack  = 0.002f;
            const float decay   = 0.06f;
            var rng = new System.Random(1337);

            for (int b = 0; b < burstStarts.Length; b++)
            {
                int s0 = (int)(burstStarts[b] * sampleRate);
                int sEnd = Mathf.Min(total, s0 + (int)((attack + decay + 0.02f) * sampleRate));
                for (int i = s0; i < sEnd; i++)
                {
                    float t = (i - s0) / (float)sampleRate;
                    float env;
                    if (t < attack) env = t / attack;
                    else            env = Mathf.Exp(-(t - attack) / decay);
                    // Slight lowpass via 2-tap average to keep it warm.
                    float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                    data[i] += n * env * 0.85f;
                }
            }

            // Soft clip to avoid harshness.
            for (int i = 0; i < total; i++)
                data[i] = Mathf.Clamp(data[i], -0.95f, 0.95f);

            var clip = AudioClip.Create("ClapSynth", total, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

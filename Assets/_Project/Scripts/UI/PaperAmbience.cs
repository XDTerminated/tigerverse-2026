using System.Collections;
using UnityEngine;

namespace Tigerverse.UI
{
    [ExecuteAlways]
    public class PaperAmbience : MonoBehaviour
    {
        const int SampleRate = 22050;

        AudioSource _wind;
        AudioSource _rustle;
        AudioSource _chime;

        public static GameObject Spawn()
        {
            var go = new GameObject("PaperAmbience");
            go.AddComponent<PaperAmbience>();
            return go;
        }

        void Start()
        {
            _wind = CreateChild("Wind");
            _wind.clip = SynthWind();
            _wind.loop = true;
            _wind.volume = 0.15f;
            _wind.spatialBlend = 0f;
            _wind.playOnAwake = false;
            _wind.Play();

            _rustle = CreateChild("Rustle");
            _rustle.loop = false;
            _rustle.volume = 0.20f;
            _rustle.spatialBlend = 0f;
            _rustle.playOnAwake = false;

            _chime = CreateChild("Chime");
            _chime.loop = false;
            _chime.volume = 0.10f;
            _chime.spatialBlend = 0f;
            _chime.playOnAwake = false;

            StartCoroutine(RustleLoop());
            StartCoroutine(ChimeLoop());
        }

        AudioSource CreateChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        IEnumerator RustleLoop()
        {
            var rng = new System.Random(20260428);
            while (true)
            {
                float wait = 8f + (float)rng.NextDouble() * 10f;
                yield return new WaitForSeconds(wait);
                float dur = 0.3f + (float)rng.NextDouble() * 0.5f;
                _rustle.clip = SynthRustle(dur, rng.Next());
                _rustle.Play();
            }
        }

        IEnumerator ChimeLoop()
        {
            var rng = new System.Random(98765);
            while (true)
            {
                float wait = 25f + (float)rng.NextDouble() * 35f;
                yield return new WaitForSeconds(wait);
                _chime.clip = SynthChime();
                _chime.Play();
            }
        }

        static AudioClip SynthWind()
        {
            const float duration = 4f;
            int total = (int)(SampleRate * duration);
            var data = new float[total];
            var rng = new System.Random(13579);

            float fc = 250f;
            float q = 2f;
            float f = 2f * Mathf.Sin(Mathf.PI * fc / SampleRate);
            float qInv = 1f / q;
            float low = 0f, band = 0f, high;

            for (int i = 0; i < total; i++)
            {
                float input = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += f * band;
                high = input - low - qInv * band;
                band += f * high;

                float t = i / (float)SampleRate;
                float env = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.3f * t);
                data[i] = band * env * 0.6f;
            }

            // Match endpoints for seamless loop via short crossfade.
            int xfade = (int)(0.05f * SampleRate);
            for (int i = 0; i < xfade; i++)
            {
                float a = i / (float)xfade;
                data[i] = Mathf.Lerp(data[total - xfade + i], data[i], a);
            }

            var clip = AudioClip.Create("WindWhisper", total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip SynthRustle(float duration, int seed)
        {
            int total = (int)(SampleRate * duration);
            var data = new float[total];
            var rng = new System.Random(seed);

            // High-passed noise with rapid exponential decay.
            float prev = 0f;
            float decay = 1f / (duration * 0.4f);
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * decay);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = n - prev * 0.7f;
                prev = n;
                data[i] = hp * env * 0.85f;
            }

            var clip = AudioClip.Create("PaperRustle", total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip SynthChime()
        {
            const float duration = 2f;
            int total = (int)(SampleRate * duration);
            var data = new float[total];

            const float freq = 880f;
            float decay = 1f / 0.6f;
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * decay);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env;
            }

            var clip = AudioClip.Create("DistantChime", total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

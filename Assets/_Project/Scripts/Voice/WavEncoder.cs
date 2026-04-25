using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Tigerverse.Voice
{
    public static class WavEncoder
    {
        // Encodes an AudioClip to a 16-bit PCM little-endian RIFF/WAVE byte array.
        // trimToSamples: number of frames per channel to keep (clip length). -1 = keep entire clip.
        public static byte[] EncodeWav(AudioClip clip, int trimToSamples = -1)
        {
            if (clip == null) return Array.Empty<byte>();

            int channels   = Mathf.Max(1, clip.channels);
            int sampleRate = Mathf.Max(1, clip.frequency);
            int totalFrames = clip.samples;
            if (trimToSamples > 0 && trimToSamples < totalFrames) totalFrames = trimToSamples;
            if (totalFrames <= 0) return Array.Empty<byte>();

            int floatCount = totalFrames * channels;
            float[] samples = new float[floatCount];
            // GetData reads interleaved float samples starting at offset 0.
            clip.GetData(samples, 0);

            int dataSize  = floatCount * 2; // 16-bit PCM
            int riffSize  = 36 + dataSize;

            using MemoryStream ms = new(44 + dataSize);
            using BinaryWriter bw = new(ms, Encoding.ASCII, leaveOpen: false);

            // RIFF header
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(riffSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);                        // subchunk size for PCM
            bw.Write((short)1);                  // PCM format
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * 2); // byte rate
            bw.Write((short)(channels * 2));     // block align
            bw.Write((short)16);                 // bits per sample

            // data chunk
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);

            for (int i = 0; i < floatCount; i++)
            {
                float f = samples[i];
                if (f >  1f) f =  1f;
                if (f < -1f) f = -1f;
                short s = (short)Mathf.RoundToInt(f * 32767f);
                bw.Write(s);
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}

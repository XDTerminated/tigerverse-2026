using System;

namespace Tigerverse.Voice
{
    public static class Levenshtein
    {
        public static int Distance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int n = a.Length;
            int m = b.Length;

            int[] prev = new int[m + 1];
            int[] curr = new int[m + 1];

            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                char ca = a[i - 1];
                for (int j = 1; j <= m; j++)
                {
                    int cost = ca == b[j - 1] ? 0 : 1;
                    int del  = prev[j] + 1;
                    int ins  = curr[j - 1] + 1;
                    int sub  = prev[j - 1] + cost;
                    int min  = del < ins ? del : ins;
                    if (sub < min) min = sub;
                    curr[j] = min;
                }
                (prev, curr) = (curr, prev);
            }
            return prev[m];
        }

        public static float NormalizedDistance(string a, string b)
        {
            int la = a?.Length ?? 0;
            int lb = b?.Length ?? 0;
            int max = la > lb ? la : lb;
            if (max == 0) return 0f;
            return (float)Distance(a, b) / max;
        }

        public static bool ContainsAny(string haystack, string[] needles)
        {
            if (string.IsNullOrEmpty(haystack) || needles == null) return false;
            string h = haystack.ToLowerInvariant();
            for (int i = 0; i < needles.Length; i++)
            {
                string n = needles[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (h.IndexOf(n.ToLowerInvariant(), StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        // Returns index of best match. Substring containment beats raw distance ties.
        public static int BestPhraseMatch(string transcript, string[] phrases, out float normalizedDistance, out bool isSubstring)
        {
            normalizedDistance = 1f;
            isSubstring = false;

            if (phrases == null || phrases.Length == 0 || string.IsNullOrEmpty(transcript))
                return -1;

            string t = transcript.ToLowerInvariant().Trim();
            int   bestIdx       = -1;
            float bestDist      = float.MaxValue;
            bool  bestSubstring = false;

            for (int i = 0; i < phrases.Length; i++)
            {
                string p = phrases[i];
                if (string.IsNullOrEmpty(p)) continue;
                string pl = p.ToLowerInvariant().Trim();

                bool sub = t.IndexOf(pl, StringComparison.Ordinal) >= 0
                        || pl.IndexOf(t, StringComparison.Ordinal) >= 0;
                float dist = NormalizedDistance(t, pl);

                bool better = false;
                if (sub && !bestSubstring) better = true;
                else if (sub == bestSubstring && dist < bestDist) better = true;

                if (better)
                {
                    bestIdx       = i;
                    bestDist      = dist;
                    bestSubstring = sub;
                }
            }

            normalizedDistance = bestDist == float.MaxValue ? 1f : bestDist;
            isSubstring = bestSubstring;
            return bestIdx;
        }
    }
}

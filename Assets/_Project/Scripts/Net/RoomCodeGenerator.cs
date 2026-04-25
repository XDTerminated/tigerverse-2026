using System;
using System.Text;

namespace Tigerverse.Net
{
    public static class RoomCodeGenerator
    {
        // Drop ambiguous characters: 0/O, 1/I/L
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 4;

        // NOTE: 'L' is intentionally retained per task spec; only 0/O/1/I are dropped.
        // (Spec says alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` exactly.)

        private static readonly System.Random _rng = new System.Random(unchecked((int)DateTime.UtcNow.Ticks));

        public static string Generate()
        {
            var sb = new StringBuilder(CodeLength);
            lock (_rng)
            {
                for (int i = 0; i < CodeLength; i++)
                {
                    sb.Append(Alphabet[_rng.Next(Alphabet.Length)]);
                }
            }
            return sb.ToString();
        }

        public static bool IsValid(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (code.Length != CodeLength) return false;
            for (int i = 0; i < code.Length; i++)
            {
                if (Alphabet.IndexOf(code[i]) < 0) return false;
            }
            return true;
        }
    }
}

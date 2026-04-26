using System;
using System.Text;

namespace Tigerverse.Net
{
    public static class RoomCodeGenerator
    {
        // Full A-Z + 0-9 alphabet. Ambiguous chars (0/O, 1/I/L) are allowed
        // because the on-screen QWERTY keyboard exposes all of them.
        public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        public const int CodeLength = 4;

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

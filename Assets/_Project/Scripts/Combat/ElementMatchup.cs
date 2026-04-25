namespace Tigerverse.Combat
{
    public static class ElementMatchup
    {
        private static readonly float[,] _table = new float[8, 8];

        static ElementMatchup()
        {
            // default everything to 1.0x
            for (int a = 0; a < 8; a++)
                for (int d = 0; d < 8; d++)
                    _table[a, d] = 1.0f;

            int N = (int)ElementType.Neutral;
            int F = (int)ElementType.Fire;
            int W = (int)ElementType.Water;
            int E = (int)ElementType.Electric;
            int G = (int)ElementType.Earth;
            int Gr = (int)ElementType.Grass;
            int I = (int)ElementType.Ice;
            int D = (int)ElementType.Dark;

            // Fire > Grass, Ice (1.5x); Fire weak vs Water, Earth (0.5x).
            _table[F, Gr] = 1.5f;
            _table[F, I] = 1.5f;
            _table[F, W] = 0.5f;
            _table[F, G] = 0.5f;

            // Water > Fire (1.5x); Water weak vs Electric, Grass (0.5x).
            _table[W, F] = 1.5f;
            _table[W, E] = 0.5f;
            _table[W, Gr] = 0.5f;

            // Electric > Water (1.5x); Electric weak vs Earth (0.5x).
            _table[E, W] = 1.5f;
            _table[E, G] = 0.5f;

            // Earth > Electric, Fire (1.5x); Earth weak vs Grass, Ice, Water (0.5x).
            _table[G, E] = 1.5f;
            _table[G, F] = 1.5f;
            _table[G, Gr] = 0.5f;
            _table[G, I] = 0.5f;
            _table[G, W] = 0.5f;

            // Grass > Water, Earth (1.5x); Grass weak vs Fire, Ice (0.5x).
            _table[Gr, W] = 1.5f;
            _table[Gr, G] = 1.5f;
            _table[Gr, F] = 0.5f;
            _table[Gr, I] = 0.5f;

            // Ice > Earth, Grass (1.5x); Ice weak vs Fire (0.5x).
            _table[I, G] = 1.5f;
            _table[I, Gr] = 1.5f;
            _table[I, F] = 0.5f;

            // Dark > Neutral (1.5x); no Dark weakness.
            _table[D, N] = 1.5f;

            // Neutral > nothing; Neutral weak vs Dark (0.5x).
            _table[N, D] = 0.5f;

            // Same element = 1.0x (already default)
        }

        public static float Calculate(ElementType attacker, ElementType defender)
        {
            int a = (int)attacker;
            int d = (int)defender;
            if (a < 0 || a >= 8 || d < 0 || d >= 8) return 1.0f;
            return _table[a, d];
        }
    }
}

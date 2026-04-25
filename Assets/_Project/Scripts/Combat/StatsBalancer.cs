using Tigerverse.Net;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Equalises core combat stats (HP, attackMult, speed) between two
    /// players so neither has a numerical edge from random drawing-color
    /// stat assignment. Element + moves + flavor text are kept per-player
    /// so each scribble still feels distinct in personality. Players don't
    /// see the original raw values — only the balanced ones.
    /// </summary>
    public static class StatsBalancer
    {
        public static void Equalize(MonsterStatsData a, MonsterStatsData b)
        {
            if (a == null || b == null) return;

            int   avgHp    = MidInt(a.hp, b.hp, 100);
            float avgAtk   = MidFloat(a.attackMult, b.attackMult, 1.0f);
            float avgSpeed = MidFloat(a.speed,      b.speed,      1.0f);

            a.hp = b.hp = avgHp;
            a.attackMult = b.attackMult = avgAtk;
            a.speed      = b.speed      = avgSpeed;
        }

        private static int MidInt(int x, int y, int fallback)
        {
            if (x <= 0 && y <= 0) return fallback;
            if (x <= 0) return y;
            if (y <= 0) return x;
            return (x + y) / 2;
        }

        private static float MidFloat(float x, float y, float fallback)
        {
            if (x <= 0f && y <= 0f) return fallback;
            if (x <= 0f) return y;
            if (y <= 0f) return x;
            return (x + y) * 0.5f;
        }
    }
}

using UnityEngine;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Sports-announcer style commentator that calls the play-by-play of a
    /// match in real time using the existing Announcer (ElevenLabs TTS).
    /// Hooks BattleManager events (OnMoveResolved, OnHPChanged, OnBattleEnd)
    /// and picks a random pre-written line per event type. Debounces so we
    /// don't talk over ourselves and skips if the announcer is already mid
    /// clip.
    /// </summary>
    [DisallowMultipleComponent]
    public class BattleCommentator : MonoBehaviour
    {
        [Tooltip("Minimum seconds between commentary lines so quick attacks don't stack TTS calls.")]
        [SerializeField] private float minLineSpacingSec = 1.8f;

        [Tooltip("Skip a commentary line entirely if the announcer is currently playing a clip.")]
        [SerializeField] private bool skipIfBusy = true;

        private BattleManager _battle;
        private Tigerverse.Voice.Announcer _announcer;
        private AudioSource _announcerAudio;
        private string _nameA = "Player 1";
        private string _nameB = "Player 2";
        private float _lastFireAt = -10f;

        // Track HP percentages so we only fire the "low HP" line on the
        // crossing edge, not every tick.
        private bool _firedLowHpA;
        private bool _firedLowHpB;

        public void Bind(BattleManager battle, string nameA, string nameB)
        {
            Unbind();
            _battle = battle;
            _nameA = string.IsNullOrEmpty(nameA) ? "Player 1" : nameA;
            _nameB = string.IsNullOrEmpty(nameB) ? "Player 2" : nameB;
            _firedLowHpA = false;
            _firedLowHpB = false;

            if (_announcer == null)
            {
                _announcer = FindFirstObjectByType<Tigerverse.Voice.Announcer>();
                if (_announcer != null) _announcerAudio = _announcer.GetComponent<AudioSource>();
            }

            if (_battle == null) return;
            _battle.OnMoveResolved.AddListener(OnMoveResolved);
            _battle.OnHPChanged.AddListener(OnHPChanged);
            _battle.OnBattleEnd.AddListener(OnBattleEnd);

            // Battle-start hype line.
            FireLine(PickRandom(BattleStartLines));
        }

        public void Unbind()
        {
            if (_battle == null) return;
            _battle.OnMoveResolved.RemoveListener(OnMoveResolved);
            _battle.OnHPChanged.RemoveListener(OnHPChanged);
            _battle.OnBattleEnd.RemoveListener(OnBattleEnd);
            _battle = null;
        }

        private void OnDestroy() => Unbind();

        // ─── Event handlers ─────────────────────────────────────────────
        private void OnMoveResolved(MoveSO move, int casterIndex)
        {
            if (move == null) return;
            string caster = (casterIndex == 0) ? _nameA : _nameB;
            string defender = (casterIndex == 0) ? _nameB : _nameA;

            string template;
            switch (move.element)
            {
                case ElementType.Electric: template = PickRandom(ElectricLines); break;
                case ElementType.Fire:     template = PickRandom(FireLines);     break;
                case ElementType.Water:    template = PickRandom(WaterLines);    break;
                case ElementType.Ice:      template = PickRandom(IceLines);      break;
                case ElementType.Grass:    template = PickRandom(GrassLines);    break;
                case ElementType.Earth:    template = PickRandom(EarthLines);    break;
                case ElementType.Dark:     template = PickRandom(DarkLines);     break;
                case ElementType.Neutral:
                default:                   template = PickRandom(NeutralLines);  break;
            }

            string line = template
                .Replace("{caster}", caster)
                .Replace("{defender}", defender)
                .Replace("{move}", move.displayName);
            FireLine(line);
        }

        private void OnHPChanged(int hpA, int maxA, int hpB, int maxB)
        {
            // Only fire the "low HP" line on the FALLING edge (HP just crossed
            // 25%). Use a small re-arm window so a heal back over 30% allows
            // another low-HP call later.
            float pctA = (maxA > 0) ? (float)hpA / maxA : 0f;
            float pctB = (maxB > 0) ? (float)hpB / maxB : 0f;

            if (pctA <= 0.25f && !_firedLowHpA)
            {
                _firedLowHpA = true;
                FireLine(PickRandom(LowHpLines).Replace("{name}", _nameA));
            }
            else if (pctA > 0.30f) _firedLowHpA = false;

            if (pctB <= 0.25f && !_firedLowHpB)
            {
                _firedLowHpB = true;
                FireLine(PickRandom(LowHpLines).Replace("{name}", _nameB));
            }
            else if (pctB > 0.30f) _firedLowHpB = false;
        }

        private void OnBattleEnd(int winnerIndex)
        {
            string winner = (winnerIndex == 0) ? _nameA : _nameB;
            // Bypass spacing for the KO line — it's the marquee moment.
            FireLine(PickRandom(KoLines).Replace("{winner}", winner), force: true);
        }

        // ─── Fire ───────────────────────────────────────────────────────
        private void FireLine(string line, bool force = false)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (!force)
            {
                if (Time.time - _lastFireAt < minLineSpacingSec) return;
                if (skipIfBusy && _announcerAudio != null && _announcerAudio.isPlaying) return;
            }
            _lastFireAt = Time.time;
            if (_announcer != null) _announcer.Say(line);
            else Debug.Log($"[BattleCommentator] (no announcer) Would say: {line}");
        }

        private static string PickRandom(string[] arr)
        {
            if (arr == null || arr.Length == 0) return null;
            return arr[Random.Range(0, arr.Length)];
        }

        // ─── Line pools ─────────────────────────────────────────────────
        private static readonly string[] BattleStartLines =
        {
            "Trainers ready? Let's see those scribbles in action!",
            "Welcome to Scribble Showdown!",
            "Alright folks, this one's gonna be a brawl!",
            "And we are LIVE in the arena!"
        };

        private static readonly string[] ElectricLines =
        {
            "{caster} sparks up {move}!",
            "ZAP! That's some serious voltage from {caster}!",
            "{move}! The air is electric!",
            "{caster} brings the thunder with {move}!"
        };

        private static readonly string[] FireLines =
        {
            "{caster} ignites {move}!",
            "It's getting HOT in here, folks!",
            "{move}! That's gonna leave a scorch mark!",
            "Fireballs flying from {caster}!"
        };

        private static readonly string[] WaterLines =
        {
            "{caster} unleashes {move}!",
            "A torrent of water from {caster}!",
            "{defender} is getting drenched!",
            "{caster} makes a splash!"
        };

        private static readonly string[] IceLines =
        {
            "{caster} freezes the field with {move}!",
            "Brrr, {move} from {caster}!",
            "Ice cold strike!",
            "{caster} chills the room!"
        };

        private static readonly string[] GrassLines =
        {
            "{caster} channels nature's fury, {move}!",
            "Vines and venom from {caster}!",
            "{move}! Don't underestimate the green stuff!",
            "{caster} brings the bloom!"
        };

        private static readonly string[] EarthLines =
        {
            "{caster} shakes the ground with {move}!",
            "{move}! That earth is moving!",
            "Tremors from {caster}!",
            "Solid hit from {caster}!"
        };

        private static readonly string[] DarkLines =
        {
            "{caster} drops some shade with {move}!",
            "{move}! Spooky stuff from {caster}!",
            "Things just got ominous!",
            "{caster} unleashes the darkness!"
        };

        private static readonly string[] NeutralLines =
        {
            "{caster} throws {move}!",
            "Clean strike from {caster}!",
            "{caster} keeps the pressure with {move}!",
            "{move}! Right on target!"
        };

        private static readonly string[] LowHpLines =
        {
            "{name} is on the ropes!",
            "{name} is hanging on by a thread!",
            "One more and {name} is done!",
            "{name} is in trouble, folks!"
        };

        private static readonly string[] KoLines =
        {
            "And THAT is the match! {winner} takes it!",
            "GAME OVER! {winner} stands tall!",
            "It's all over! {winner} is your champion!",
            "{winner} WINS the showdown!"
        };
    }
}

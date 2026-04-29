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
            "Helloooo Scribble Showdown! What a crowd tonight!",
            "Trainers, take your positions! HERE WE GO!",
            "Folks, you are NOT gonna want to blink for this one!",
            "Lights down, scribbles up — IT IS ON!",
            "We are LIVE from the Scribble Dome — let's RUMBLE!"
        };

        private static readonly string[] ElectricLines =
        {
            "{move}! OOOH the SPARKS are flying!",
            "{caster} lights it up with {move}!",
            "Lightning down the middle! {move}!",
            "ZAP! {caster} is COOKING with electricity!",
            "Pure voltage! That is text-book {move}!"
        };

        private static readonly string[] FireLines =
        {
            "{move}! {caster} is BRINGING THE HEAT!",
            "Whoa! Fireball connects! That hurt!",
            "{caster} sets the arena ABLAZE with {move}!",
            "RED HOT! {caster} is on FIRE tonight!",
            "{move}! {defender} just got TORCHED!"
        };

        private static readonly string[] WaterLines =
        {
            "{move}! {caster} cracks open the ocean!",
            "Tsunami incoming! {move}!",
            "WHOOSH! {caster} drowns the opposition!",
            "{defender} is SOAKED, folks!",
            "{move}! Big crashing wave on {defender}!"
        };

        private static readonly string[] IceLines =
        {
            "{move}! Sub-zero strike from {caster}!",
            "BRRR! That is COLD! {move}!",
            "{caster} freezes the field — {move}!",
            "Glacier-sized hit! {defender} is iced!",
            "{move}! Pure crystalline DEVASTATION!"
        };

        private static readonly string[] GrassLines =
        {
            "{move}! Mother Nature is FURIOUS!",
            "{caster} whips out the vines! {move}!",
            "Bloom and DOOM! {move}!",
            "{move}! {defender} just got tangled UP!",
            "Green fury from {caster}! That hurt!"
        };

        private static readonly string[] EarthLines =
        {
            "{move}! The ground itself is FIGHTING for {caster}!",
            "BOOM! Tremors rolling out of {caster}!",
            "{caster} drops the BIG ONE — {move}!",
            "Geological warfare, folks! {move}!",
            "{move}! That is some heavyweight ROCK!"
        };

        private static readonly string[] DarkLines =
        {
            "{move}! {caster} brings out the DARKNESS!",
            "Ooooh, things just got SPOOKY!",
            "{move}! Pure shadow energy from {caster}!",
            "{defender} won't see this one coming! {move}!",
            "{caster} plays DIRTY with {move}!"
        };

        private static readonly string[] NeutralLines =
        {
            "{move}! Clean and CRISP from {caster}!",
            "Right on the button! {move}!",
            "{caster} pressing hard with {move}!",
            "{defender} is taking PUNISHMENT!",
            "{move}! Textbook execution!"
        };

        private static readonly string[] LowHpLines =
        {
            "{name} is HURTING, folks! Hanging by a THREAD!",
            "Look at {name} — one more shot and it is OVER!",
            "{name} is on the brink of COLLAPSE!",
            "Critical condition for {name}!",
            "Folks, {name} is RUNNING ON FUMES!"
        };

        private static readonly string[] KoLines =
        {
            "AND THAT'S THE MATCH! {winner} TAKES THE CROWN!",
            "IT'S OVER! {winner} WINS!",
            "GAME! SET! MATCH! {winner} is your CHAMPION!",
            "WHAT A FINISH! {winner} STANDS TALL!",
            "GOODNIGHT EVERYBODY! {winner} JUST WON SCRIBBLE SHOWDOWN!"
        };
    }
}

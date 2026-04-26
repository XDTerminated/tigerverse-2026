using System.Collections.Generic;
using TMPro;
using Tigerverse.Combat;
using Tigerverse.Voice;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Head-locked battle HUD. Shows the local scribble's 4 moves on the
    /// right side of the player's view, and a running event log on the left
    /// (mode switches, moves cast). Built programmatically — no scene/prefab
    /// wiring required.
    ///
    /// Spawn one of these at battle start; <see cref="Configure"/> hooks it
    /// up to the live <see cref="BattleControlModeManager"/> and
    /// <see cref="VoiceCommandRouter"/>. On destroy it unsubscribes.
    /// </summary>
    public class BattleHUD : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Distance from the camera the HUD sits at while head-locked.")]
        public float distanceFromCamera = 1.4f;
        [Tooltip("Horizontal half-width of the HUD's left/right panels (canvas units).")]
        public float panelHorizontalSpread = 380f;
        [Tooltip("Max number of log entries shown before old ones are dropped.")]
        public int maxLogEntries = 8;

        private Canvas _canvas;
        private RectTransform _movesPanel;
        private RectTransform _logPanel;
        private TMP_Text _logText;

        private BattleControlModeManager _modeMgr;
        private VoiceCommandRouter _voice;
        private readonly Queue<string> _log = new Queue<string>();

        private void Awake() => BuildCanvas();
        private void OnDestroy() => Unsubscribe();

        private void LateUpdate()
        {
            // Head-lock to the camera so the HUD sits comfortably in view.
            var cam = Camera.main;
            if (cam == null) return;
            transform.position = cam.transform.position + cam.transform.forward * distanceFromCamera;
            transform.rotation = cam.transform.rotation;
        }

        public void Configure(BattleControlModeManager modeMgr, VoiceCommandRouter voice, string localPlayerName = "You")
        {
            Unsubscribe();
            _modeMgr = modeMgr;
            _voice = voice;
            LocalPlayerName = localPlayerName;

            if (_modeMgr != null) _modeMgr.OnModeChanged += HandleModeChanged;
            if (_voice != null)
            {
                _voice.OnMoveCast.AddListener(HandleMoveCast);
                RefreshMoves(_voice.AvailableMoves);
            }
            else
            {
                // No voice router (e.g. debug shortcut) — show placeholders so
                // you can see the layout.
                RefreshMoves(null);
            }

            AppendLog($"{LocalPlayerName} entered battle ({(_modeMgr != null ? _modeMgr.CurrentMode.ToString() : "?")})");
        }

        private string LocalPlayerName { get; set; } = "You";

        private void Unsubscribe()
        {
            if (_modeMgr != null) _modeMgr.OnModeChanged -= HandleModeChanged;
            if (_voice != null) _voice.OnMoveCast.RemoveListener(HandleMoveCast);
        }

        // ─── Event handlers ───────────────────────────────────────────────

        private void HandleModeChanged(BattleControlMode mode)
        {
            AppendLog($"{LocalPlayerName} → {(mode == BattleControlMode.Scribble ? "SCRIBBLE" : "ARTIST")}");
        }

        private void HandleMoveCast(Tigerverse.Combat.MoveSO move)
        {
            if (move == null) return;
            AppendLog($"{LocalPlayerName} used {move.displayName}!");
        }

        /// <summary>Public hook so remote-player events (network replication) can be appended later.</summary>
        public void AppendRemoteEvent(string playerName, string text) => AppendLog($"{playerName} {text}");

        // ─── Internals ────────────────────────────────────────────────────

        private void AppendLog(string entry)
        {
            _log.Enqueue(entry);
            while (_log.Count > maxLogEntries) _log.Dequeue();
            if (_logText != null) _logText.text = string.Join("\n", _log);
        }

        private void RefreshMoves(Tigerverse.Combat.MoveSO[] moves)
        {
            if (_movesPanel == null) return;
            // Strip any existing children so re-binding is idempotent.
            for (int i = _movesPanel.childCount - 1; i >= 0; i--)
                Destroy(_movesPanel.GetChild(i).gameObject);

            int count = 4;
            for (int i = 0; i < count; i++)
            {
                string label = (moves != null && i < moves.Length && moves[i] != null) ? moves[i].displayName : $"— move {i + 1} —";
                BuildMovePill(label, i);
            }
        }

        private void BuildMovePill(string label, int index)
        {
            const float pillWidth = 280f;
            const float pillHeight = 56f;
            const float gap = 12f;
            float startY = (4 - 1) * (pillHeight + gap) * 0.5f;

            var go = new GameObject($"Move{index}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_movesPanel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(pillWidth, pillHeight);
            rt.anchoredPosition = new Vector2(0, startY - index * (pillHeight + gap));
            var img = go.GetComponent<Image>();
            img.color = Color.white;
#if UNITY_EDITOR
            var panel = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Generated/panel-rounded-sm.png");
            if (panel != null) { img.sprite = panel; img.type = Image.Type.Sliced; }
#endif
            img.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lblRT = (RectTransform)labelGo.transform;
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(20, 6); lblRT.offsetMax = new Vector2(-20, -6);
            var tmp = labelGo.GetComponent<TMP_Text>();
            tmp.text = label;
            tmp.fontSize = 28;
            tmp.color = Color.black;
            tmp.alignment = TextAlignmentOptions.Midline;
            tmp.raycastTarget = false;
#if UNITY_EDITOR
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
            if (font != null) tmp.font = font;
#endif
        }

        private void BuildCanvas()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 900;
            gameObject.AddComponent<CanvasScaler>();

            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(1600, 600);
            transform.localScale = Vector3.one * 0.0015f;

            // Right panel: 4 move pills stacked vertically.
            _movesPanel = new GameObject("MovesPanel", typeof(RectTransform)).GetComponent<RectTransform>();
            _movesPanel.SetParent(transform, false);
            _movesPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _movesPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _movesPanel.pivot = new Vector2(0.5f, 0.5f);
            _movesPanel.sizeDelta = new Vector2(320, 600);
            _movesPanel.anchoredPosition = new Vector2(panelHorizontalSpread, 0);

            // Left panel: a single multi-line TMP for the event log.
            _logPanel = new GameObject("LogPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _logPanel.SetParent(transform, false);
            _logPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _logPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _logPanel.pivot = new Vector2(0.5f, 0.5f);
            _logPanel.sizeDelta = new Vector2(420, 380);
            _logPanel.anchoredPosition = new Vector2(-panelHorizontalSpread, 0);
            var logBg = _logPanel.GetComponent<Image>();
            logBg.color = Color.white;
            logBg.raycastTarget = false;
#if UNITY_EDITOR
            var panel = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Generated/panel-rounded-sm.png");
            if (panel != null) { logBg.sprite = panel; logBg.type = Image.Type.Sliced; }
#endif
            var logTextGo = new GameObject("LogText", typeof(RectTransform), typeof(TextMeshProUGUI));
            logTextGo.transform.SetParent(_logPanel, false);
            var logRT = (RectTransform)logTextGo.transform;
            logRT.anchorMin = Vector2.zero; logRT.anchorMax = Vector2.one;
            logRT.offsetMin = new Vector2(18, 18); logRT.offsetMax = new Vector2(-18, -18);
            _logText = logTextGo.GetComponent<TMP_Text>();
            _logText.fontSize = 22;
            _logText.color = Color.black;
            _logText.alignment = TextAlignmentOptions.BottomLeft;
            _logText.text = "";
            _logText.raycastTarget = false;
#if UNITY_EDITOR
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
            if (font != null) _logText.font = font;
#endif
        }
    }
}

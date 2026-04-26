using TMPro;
using Tigerverse.Combat;
using Tigerverse.Voice;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Wrist-mounted battle HUD on the LEFT controller. Shows the local
    /// monster's 4 moves as a vertical stack of tiles; each tile has a
    /// horizontal cooldown bar that drains while the move is locked out
    /// and a countdown like "1.2s" overlaid on top. Tiles auto-tint between
    /// "ready" and "cooling" states so the player can glance at their wrist
    /// and immediately see what's available.
    ///
    /// Built programmatically — no scene/prefab wiring required. Spawn one
    /// at battle start; <see cref="Configure"/> hooks it to the live
    /// <see cref="VoiceCommandRouter"/> for per-move cooldown reads.
    /// </summary>
    public class BattleHUD : MonoBehaviour
    {
        [Header("Wrist anchor")]
        [Tooltip("World-space offset from the left controller — positions the panel above/in front of the wrist.")]
        public Vector3 wristOffset = new Vector3(0f, 0.06f, 0.04f);
        [Tooltip("Extra rotation applied after billboarding to the head, in degrees. Useful for tilting the panel forward off the wrist.")]
        public Vector3 panelTilt = new Vector3(15f, 0f, 0f);
        [Tooltip("World-space scale multiplier on the panel. Smaller = more wristwatch-y.")]
        public float panelScale = 0.0014f;

        [Header("Layout")]
        public int   tileCount = 4;
        public float tileWidth = 320f;
        public float tileHeight = 78f;
        public float tileGap = 10f;

        [Header("Tints")]
        public Color tileReadyColor   = new Color(1f, 1f, 1f, 0.96f);
        public Color tileCoolingColor = new Color(0.78f, 0.78f, 0.82f, 0.96f);
        public Color barReadyColor    = new Color(0.30f, 0.85f, 0.45f, 1f);
        public Color barCoolingColor  = new Color(1.00f, 0.55f, 0.25f, 1f);

        private Canvas _canvas;
        private RectTransform _movesPanel;

        private VoiceCommandRouter _voice;
        private MoveSO[] _moves;

        // Per-tile elements — parallel arrays for fast Update.
        private Image[]   _tileBgs;
        private Image[]   _tileFills;
        private TMP_Text[] _nameTexts;
        private TMP_Text[] _cdTexts;

        // Cached XR transforms used to anchor the panel each frame.
        private Transform _leftCtrl;
        private Transform _head;
        private float _lastFindAt;

        private void Awake() => BuildCanvas();
        private void OnDestroy() => Unsubscribe();

        public void Configure(VoiceCommandRouter voice, string localPlayerName = "You")
        {
            Unsubscribe();
            _voice = voice;

            if (_voice != null)
            {
                _voice.OnMoveCast.AddListener(HandleMoveCast);
                _moves = _voice.AvailableMoves;
            }
            RefreshMoves(_moves);
            FindAnchors();
        }

        private void Unsubscribe()
        {
            if (_voice != null) _voice.OnMoveCast.RemoveListener(HandleMoveCast);
        }

        private void HandleMoveCast(MoveSO move)
        {
            // Tile pulse on cast — flash the cooling tint immediately so the
            // change is felt even before the cooldown timer ticks down.
            if (_moves == null) return;
            for (int i = 0; i < _moves.Length && i < tileCount; i++)
            {
                if (_moves[i] == move && _tileBgs != null && _tileBgs[i] != null)
                {
                    _tileBgs[i].color = tileCoolingColor;
                    return;
                }
            }
        }

        private void Update()
        {
            // Re-resolve XR transforms occasionally — the rig can be
            // (re)spawned partway through the session.
            if ((_leftCtrl == null || _head == null) && Time.unscaledTime - _lastFindAt > 0.5f)
            {
                _lastFindAt = Time.unscaledTime;
                FindAnchors();
            }

            // The HUD is spawned before VoiceCommandRouter.Bind() runs, so
            // _moves can be null/stale on the first frame. Re-pull each
            // frame and rebuild tiles if the moveset reference changed.
            if (_voice != null && !ReferenceEquals(_voice.AvailableMoves, _moves))
            {
                RefreshMoves(_voice.AvailableMoves);
            }

            UpdateAnchorTransform();
            UpdateTiles();
        }

        // ─── Anchoring ────────────────────────────────────────────────────

        private void FindAnchors()
        {
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                _leftCtrl = FindUnderRig(origin.transform, "Left Controller")
                         ?? FindUnderRig(origin.transform, "LeftHand Controller")
                         ?? FindUnderRig(origin.transform, "Left Hand Controller");
                if (origin.Camera != null) _head = origin.Camera.transform;
            }
            if (_head == null && Camera.main != null) _head = Camera.main.transform;
        }

        private static Transform FindUnderRig(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private void UpdateAnchorTransform()
        {
            // Position the panel above the left controller. If we can't find
            // it (e.g. flat-screen editor without XR running), fall back to a
            // head-locked offset to the player's left so the HUD is still
            // readable for laptop dev testing.
            if (_leftCtrl != null)
            {
                Vector3 worldOffset = _leftCtrl.TransformDirection(wristOffset);
                transform.position = _leftCtrl.position + worldOffset;
            }
            else if (_head != null)
            {
                transform.position = _head.position
                                   + _head.right   * -0.30f
                                   + _head.forward *  0.45f
                                   + _head.up      * -0.18f;
            }
            else
            {
                return;
            }

            // Always face the player's head so the text is readable no
            // matter how the wrist is rotated.
            if (_head != null)
            {
                Vector3 toHead = _head.position - transform.position;
                if (toHead.sqrMagnitude > 1e-4f)
                {
                    Quaternion look = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                    transform.rotation = look * Quaternion.Euler(panelTilt);
                }
            }

            transform.localScale = Vector3.one * panelScale;
        }

        // ─── Tile updates ─────────────────────────────────────────────────

        private void UpdateTiles()
        {
            if (_tileBgs == null) return;

            for (int i = 0; i < tileCount; i++)
            {
                MoveSO move = (_moves != null && i < _moves.Length) ? _moves[i] : null;
                if (move == null)
                {
                    if (_tileFills[i] != null) _tileFills[i].fillAmount = 0f;
                    if (_cdTexts[i]   != null) _cdTexts[i].text = "";
                    continue;
                }

                float remaining = _voice != null ? _voice.GetCooldownRemaining(move) : 0f;
                float total     = Mathf.Max(0.001f, move.cooldownSeconds);

                if (remaining > 0.05f)
                {
                    float frac = Mathf.Clamp01(remaining / total);
                    if (_tileBgs[i]   != null) _tileBgs[i].color = tileCoolingColor;
                    if (_tileFills[i] != null)
                    {
                        _tileFills[i].fillAmount = frac;
                        _tileFills[i].color = barCoolingColor;
                    }
                    if (_cdTexts[i] != null) _cdTexts[i].text = remaining.ToString("F1") + "s";
                }
                else
                {
                    if (_tileBgs[i]   != null) _tileBgs[i].color = tileReadyColor;
                    if (_tileFills[i] != null)
                    {
                        _tileFills[i].fillAmount = 1f;
                        _tileFills[i].color = barReadyColor;
                    }
                    if (_cdTexts[i] != null) _cdTexts[i].text = "READY";
                }
            }
        }

        // ─── Construction ─────────────────────────────────────────────────

        private void RefreshMoves(MoveSO[] moves)
        {
            _moves = moves;

            if (_movesPanel == null) return;
            for (int i = _movesPanel.childCount - 1; i >= 0; i--)
                Destroy(_movesPanel.GetChild(i).gameObject);

            _tileBgs   = new Image[tileCount];
            _tileFills = new Image[tileCount];
            _nameTexts = new TMP_Text[tileCount];
            _cdTexts   = new TMP_Text[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                string label = (moves != null && i < moves.Length && moves[i] != null)
                    ? moves[i].displayName
                    : $"— move {i + 1} —";
                BuildMoveTile(label, i);
            }
        }

        private void BuildMoveTile(string label, int index)
        {
            float startY = (tileCount - 1) * (tileHeight + tileGap) * 0.5f;

            // Tile background.
            var go = new GameObject($"Move{index}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_movesPanel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(tileWidth, tileHeight);
            rt.anchoredPosition = new Vector2(0, startY - index * (tileHeight + tileGap));
            var bgImg = go.GetComponent<Image>();
            bgImg.color = tileReadyColor;
            bgImg.raycastTarget = false;
#if UNITY_EDITOR
            var panel = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Generated/panel-rounded-sm.png");
            if (panel != null) { bgImg.sprite = panel; bgImg.type = Image.Type.Sliced; }
#endif
            _tileBgs[index] = bgImg;

            // Cooldown fill bar — sits inside the tile, drains horizontally.
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(go.transform, false);
            var fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = new Vector2(0f, 0f);
            fillRT.anchorMax = new Vector2(1f, 0f);
            fillRT.pivot     = new Vector2(0.5f, 0f);
            fillRT.sizeDelta = new Vector2(-16f, 10f);
            fillRT.anchoredPosition = new Vector2(0f, 8f);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = barReadyColor;
            fillImg.raycastTarget = false;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;
#if UNITY_EDITOR
            var panelSm = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Generated/panel-rounded-sm.png");
            if (panelSm != null) { fillImg.sprite = panelSm; }
#endif
            _tileFills[index] = fillImg;

            // Move name (top of tile).
            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameGo.transform.SetParent(go.transform, false);
            var nameRT = (RectTransform)nameGo.transform;
            nameRT.anchorMin = new Vector2(0f, 0.4f);
            nameRT.anchorMax = new Vector2(0.6f, 1f);
            nameRT.offsetMin = new Vector2(18f, 0f);
            nameRT.offsetMax = new Vector2(-4f, -2f);
            var nameTmp = nameGo.GetComponent<TMP_Text>();
            nameTmp.text = label;
            nameTmp.fontSize = 26;
            nameTmp.color = Color.black;
            nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
            nameTmp.raycastTarget = false;
            nameTmp.enableWordWrapping = false;
            nameTmp.overflowMode = TextOverflowModes.Ellipsis;
#if UNITY_EDITOR
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
            if (font != null) nameTmp.font = font;
#endif
            _nameTexts[index] = nameTmp;

            // Countdown text (right side, near the bar).
            var cdGo = new GameObject("Cooldown", typeof(RectTransform), typeof(TextMeshProUGUI));
            cdGo.transform.SetParent(go.transform, false);
            var cdRT = (RectTransform)cdGo.transform;
            cdRT.anchorMin = new Vector2(0.55f, 0.4f);
            cdRT.anchorMax = new Vector2(1f, 1f);
            cdRT.offsetMin = new Vector2(0f, 0f);
            cdRT.offsetMax = new Vector2(-18f, -2f);
            var cdTmp = cdGo.GetComponent<TMP_Text>();
            cdTmp.text = "READY";
            cdTmp.fontSize = 22;
            cdTmp.color = new Color(0.15f, 0.5f, 0.25f);
            cdTmp.alignment = TextAlignmentOptions.MidlineRight;
            cdTmp.raycastTarget = false;
#if UNITY_EDITOR
            var font2 = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
            if (font2 != null) cdTmp.font = font2;
#endif
            _cdTexts[index] = cdTmp;
        }

        private void BuildCanvas()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 900;
            gameObject.AddComponent<CanvasScaler>();

            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(tileWidth + 40f, tileCount * tileHeight + (tileCount - 1) * tileGap + 40f);
            transform.localScale = Vector3.one * panelScale;

            // Single moves panel — vertically stacked tiles.
            _movesPanel = new GameObject("MovesPanel", typeof(RectTransform)).GetComponent<RectTransform>();
            _movesPanel.SetParent(transform, false);
            _movesPanel.anchorMin = new Vector2(0f, 0f);
            _movesPanel.anchorMax = new Vector2(1f, 1f);
            _movesPanel.offsetMin = new Vector2(20f, 20f);
            _movesPanel.offsetMax = new Vector2(-20f, -20f);
        }
    }
}

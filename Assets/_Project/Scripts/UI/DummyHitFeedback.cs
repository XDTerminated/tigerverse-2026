using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Visual hit feedback for the practice dummy. Provides a flash tint,
    /// a brief knockback shove away from the camera, a floating damage
    /// number, and a persistent world-space HP bar. Coroutines are
    /// re-entrant via cached handles so back-to-back hits restart cleanly.
    /// </summary>
    [DisallowMultipleComponent]
    public class DummyHitFeedback : MonoBehaviour
    {
        [Header("Flash")]
        [SerializeField] private float flashHoldSec = 0.12f;
        [SerializeField] private float flashFadeSec = 0.18f;
        [SerializeField] private float flashEmissionStrength = 2.5f;

        [Header("Knockback")]
        [SerializeField] private float knockbackDistance = 0.15f;
        [SerializeField] private float knockbackOutSec   = 0.10f;
        [SerializeField] private float knockbackBackSec  = 0.20f;

        [Header("HP")]
        [SerializeField] private int maxHP = 100;

        [Header("Damage number")]
        [SerializeField] private float damageNumberRiseSec  = 1.0f;
        [SerializeField] private float damageNumberRiseDist = 0.45f;

        // Cached renderer state so we can restore _BaseColor / _EmissionColor
        // after a flash. One slot per material instance the dummy owns.
        private struct MatSlot
        {
            public Material mat;
            public bool hasBaseColor;
            public Color  baseColor;
            public bool hasEmission;
            public Color emissionColor;
            public bool emissionWasEnabled;
            public Renderer renderer;
            public bool rendererHadColor;
            public Color rendererColor;
        }
        private readonly List<MatSlot> _slots = new List<MatSlot>();

        private Transform _headAnchor;
        private float     _headHeight = 1.85f;
        private int       _hp;
        private bool      _initialised;

        private Coroutine _flashCo;
        private Coroutine _knockCo;
        private Vector3   _knockBaseLocalPos;
        private bool      _knockBaseCaptured;

        // Persistent HP bar — same scribble component the battle bars use.
        private HPBar _hpBar;

        // Cached UI assets so the bar matches the rest of the scribble look
        // without re-loading every spawn.
        private static Sprite     _cachedPanelSprite;
        private static TMP_FontAsset _cachedFont;

        private Animator _animator;
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");

        public void Initialize()
        {
            if (_initialised) return;
            _initialised = true;
            _hp = maxHP;
            CacheRenderers();
            ResolveHeadAnchor();
            BuildHPBar();
            _animator = GetComponentInChildren<Animator>();
            // CRITICAL: a fresh Animator on a runtime-Instantiated prefab
            // silently ignores SetTrigger / Play / CrossFade until Rebind
            // is called. Without this the Hit/Die clips never fire.
            if (_animator != null) _animator.Rebind();
        }

        private bool AnimatorHasParam(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return false;
            foreach (var p in _animator.parameters) if (p.nameHash == hash) return true;
            return false;
        }

        private void OnEnable()
        {
            if (!_initialised) Initialize();
        }

        // Public API. spellColor of default => fall back to a soft white flash.
        public void OnHit(Color spellColor = default, float damage = 12f)
        {
            if (!_initialised) Initialize();

            Color tint = (spellColor.r == 0f && spellColor.g == 0f && spellColor.b == 0f && spellColor.a == 0f)
                ? new Color(1f, 1f, 1f, 1f)
                : spellColor;

            // HP update — never let it sink below 0; wrap to full so the
            // tutorial dummy keeps standing through endless practice.
            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damage));
            _hp -= dmgInt;
            bool wrapped = false;
            if (_hp <= 0) { _hp = maxHP; wrapped = true; }
            UpdateHPBar();

            if (_flashCo != null) StopCoroutine(_flashCo);
            _flashCo = StartCoroutine(FlashRoutine(tint));

            if (_knockCo != null)
            {
                StopCoroutine(_knockCo);
                if (_knockBaseCaptured) transform.localPosition = _knockBaseLocalPos;
            }
            _knockCo = StartCoroutine(KnockbackRoutine());

            SpawnDamageNumber(damage, tint);

            // Trigger the Hoodie's HitRecieve clip via the Animator. On
            // the wrap-to-full edge case (HP would have dropped to 0), play
            // Death once for an extra-dramatic reaction before the wrap.
            // CrossFade in addition to SetTrigger because some controllers
            // can swallow triggers if their condition graph isn't quite
            // right; CrossFade forces the state regardless.
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[DummyHitFeedback] No animator/controller on dummy — Hit clip won't play.");
            }
            else if (wrapped && AnimatorHasParam(DieHash))
            {
                _animator.SetTrigger(DieHash);
                _animator.CrossFade("Die", 0.08f, 0);
                Debug.Log("[DummyHitFeedback] Triggered Die (HP wrapped to full).");
            }
            else if (AnimatorHasParam(HitHash))
            {
                _animator.SetTrigger(HitHash);
                _animator.CrossFade("Hit", 0.08f, 0);
                Debug.Log($"[DummyHitFeedback] Triggered Hit (hp={_hp}).");
            }
        }

        // ─── Renderer caching ───────────────────────────────────────────
        private void CacheRenderers()
        {
            _slots.Clear();
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                // sharedMaterials is fine for read; .materials forces an
                // instance per renderer which is what we want so the flash
                // doesn't leak across other dummies/props using the same
                // shared asset.
                var mats = r.materials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var slot = new MatSlot
                    {
                        mat = m,
                        renderer = r,
                        hasBaseColor = m.HasProperty("_BaseColor"),
                        hasEmission  = m.HasProperty("_EmissionColor"),
                    };
                    if (slot.hasBaseColor) slot.baseColor = m.GetColor("_BaseColor");
                    if (slot.hasEmission)
                    {
                        slot.emissionColor = m.GetColor("_EmissionColor");
                        slot.emissionWasEnabled = m.IsKeywordEnabled("_EMISSION");
                    }
                    if (!slot.hasBaseColor && m.HasProperty("_Color"))
                    {
                        slot.rendererHadColor = true;
                        slot.rendererColor = m.GetColor("_Color");
                    }
                    _slots.Add(slot);
                }
            }
        }

        private IEnumerator FlashRoutine(Color tint)
        {
            // Hold the flash colour, then fade back to the cached defaults.
            ApplyFlash(tint, 1f);
            float t = 0f;
            while (t < flashHoldSec)
            {
                t += Time.deltaTime;
                yield return null;
            }
            t = 0f;
            while (t < flashFadeSec)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / flashFadeSec);
                ApplyFlash(tint, k);
                yield return null;
            }
            ApplyFlash(tint, 0f);
            _flashCo = null;
        }

        private void ApplyFlash(Color tint, float k)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.mat == null) continue;
                if (s.hasBaseColor)
                {
                    Color c = Color.Lerp(s.baseColor, tint, 0.55f * k);
                    s.mat.SetColor("_BaseColor", c);
                }
                else if (s.rendererHadColor)
                {
                    Color c = Color.Lerp(s.rendererColor, tint, 0.55f * k);
                    s.mat.SetColor("_Color", c);
                }
                if (s.hasEmission)
                {
                    if (k > 0f) s.mat.EnableKeyword("_EMISSION");
                    else if (!s.emissionWasEnabled) s.mat.DisableKeyword("_EMISSION");
                    Color em = Color.Lerp(s.emissionColor, tint * flashEmissionStrength, k);
                    s.mat.SetColor("_EmissionColor", em);
                }
            }
        }

        // ─── Knockback ──────────────────────────────────────────────────
        private IEnumerator KnockbackRoutine()
        {
            if (!_knockBaseCaptured)
            {
                _knockBaseLocalPos = transform.localPosition;
                _knockBaseCaptured = true;
            }
            Vector3 basePos = _knockBaseLocalPos;

            // Knockback is "away from the camera" along the dummy's local
            // frame, projected onto the horizontal plane so the dummy
            // doesn't pop into the floor or the air.
            Vector3 awayDir;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 worldAway = (transform.position - cam.transform.position);
                worldAway.y = 0f;
                if (worldAway.sqrMagnitude < 1e-4f) worldAway = transform.forward;
                worldAway.Normalize();
                Vector3 parentLocal = transform.parent != null
                    ? transform.parent.InverseTransformDirection(worldAway)
                    : worldAway;
                parentLocal.y = 0f;
                awayDir = parentLocal.sqrMagnitude > 1e-4f ? parentLocal.normalized : Vector3.forward;
            }
            else
            {
                awayDir = Vector3.forward;
            }

            Vector3 backPos = basePos + awayDir * knockbackDistance;

            float t = 0f;
            while (t < knockbackOutSec)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / knockbackOutSec);
                transform.localPosition = Vector3.Lerp(basePos, backPos, k);
                yield return null;
            }
            t = 0f;
            while (t < knockbackBackSec)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / knockbackBackSec);
                transform.localPosition = Vector3.Lerp(backPos, basePos, k);
                yield return null;
            }
            transform.localPosition = basePos;
            _knockCo = null;
        }

        // ─── Head anchor ────────────────────────────────────────────────
        private void ResolveHeadAnchor()
        {
            // Look for a child literally named "Head" (case-insensitive on
            // the first match) before falling back to a renderer-bounds
            // estimate at ~1.85m.
            var children = GetComponentsInChildren<Transform>(true);
            foreach (var c in children)
            {
                if (c == null || c == transform) continue;
                if (string.Equals(c.name, "Head", System.StringComparison.OrdinalIgnoreCase))
                {
                    _headAnchor = c;
                    return;
                }
            }

            float maxY = float.NegativeInfinity;
            var rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                float top = r.bounds.max.y;
                if (top > maxY) maxY = top;
            }
            if (!float.IsNegativeInfinity(maxY))
            {
                _headHeight = Mathf.Max(0.5f, maxY - transform.position.y);
            }
            else
            {
                _headHeight = 1.85f;
            }
        }

        private Vector3 GetHeadWorldPos()
        {
            if (_headAnchor != null) return _headAnchor.position + Vector3.up * 0.18f;
            return transform.position + Vector3.up * (_headHeight + 0.18f);
        }

        // ─── Damage number ──────────────────────────────────────────────
        private void SpawnDamageNumber(float damage, Color tint)
        {
            var go = new GameObject("DamageNumber", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = GetHeadWorldPos();

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 600;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(400, 160);
            go.transform.localScale = Vector3.one * 0.0025f;

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lblRT = (RectTransform)labelGo.transform;
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;

            var tmp = labelGo.GetComponent<TMP_Text>();
            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damage));
            tmp.text = $"-{dmgInt}!";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 140;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(tint.r, tint.g, tint.b, 1f);
            tmp.raycastTarget = false;
            tmp.outlineColor = new Color32(0, 0, 0, 255);
            tmp.outlineWidth = 0.18f;

            StartCoroutine(AnimateDamageNumber(go.transform, tmp));
        }

        private IEnumerator AnimateDamageNumber(Transform t, TMP_Text tmp)
        {
            if (t == null) yield break;
            Vector3 startPos = t.position;
            Vector3 endPos   = startPos + Vector3.up * damageNumberRiseDist;
            float elapsed = 0f;
            while (elapsed < damageNumberRiseSec && t != null)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / damageNumberRiseSec);
                if (t != null) t.position = Vector3.Lerp(startPos, endPos, k);

                if (tmp != null)
                {
                    var c = tmp.color;
                    c.a = 1f - k;
                    tmp.color = c;
                }
                // Billboard toward the camera each frame.
                var cam = Camera.main;
                if (cam != null && t != null)
                {
                    Vector3 dir = t.position - cam.transform.position;
                    if (dir.sqrMagnitude > 1e-4f)
                        t.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        // ─── HP bar ─────────────────────────────────────────────────────
        // Builds an instance of the same scribble HPBar component the battle
        // bars use, so the dummy reads as part of the same UI family.
        // No HPBar prefab ships with the project — TigerverseBattleHud builds
        // them via an Editor menu — so we mirror that construction at runtime
        // and then just call `_hpBar.SetHP(...)` on hit.
        private void BuildHPBar()
        {
            // HP bar floats well above the dummy's head — bumped from
            // +0.32 to +0.75 so the bar reads as "above the character"
            // rather than crowding the shoulders.
            float yOffset = _headAnchor != null
                ? (_headAnchor.position.y - transform.position.y) + 0.75f
                : _headHeight + 0.75f;

            var canvasGo = new GameObject(
                "DummyHPBar",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(HPBar));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, yOffset, 0f);
            canvasGo.transform.localScale    = Vector3.one * 0.0020f;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 590;
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 60f);

            var panelSprite = LoadPanelSprite();
            var font = LoadSophiecomicFont();

            // Background panel — white scribble panel with the themed border.
            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgRT = (RectTransform)bgGo.transform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = TigerverseTheme.White;
            bgImg.raycastTarget = false;
            if (panelSprite != null)
            {
                bgImg.sprite = panelSprite;
                bgImg.type = Image.Type.Sliced;
            }

            // Fill — left-anchored full-height; HPBar.LateUpdate animates
            // sizeDelta.x so the rounded right edge stays intact while
            // draining.
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(canvasGo.transform, false);
            var fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = new Vector2(0f, 0.2f);
            fillRT.anchorMax = new Vector2(0f, 0.8f);
            fillRT.pivot     = new Vector2(0f, 0.5f);
            fillRT.anchoredPosition = new Vector2(8f, 0f);
            fillRT.sizeDelta = new Vector2(rt.sizeDelta.x - 16f, 0f);
            var fillImg = fillGo.GetComponent<Image>();
            // Tailwind green-500, matches HPBar.cs healthyColor.
            fillImg.color = new Color(0x22 / 255f, 0xC5 / 255f, 0x5E / 255f, 1f);
            fillImg.raycastTarget = false;
            if (panelSprite != null)
            {
                fillImg.sprite = panelSprite;
                fillImg.type = Image.Type.Sliced;
            }

            // Centered TMP label — "current/max" as the scribble bars show.
            var lblGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(canvasGo.transform, false);
            var lblRT = (RectTransform)lblGo.transform;
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
            var tmp = lblGo.GetComponent<TextMeshProUGUI>();
            tmp.text = $"{_hp}/{maxHP}";
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TigerverseTheme.Black;
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;

            // Wire the HPBar component to the freshly built widgets via a
            // SerializedObject — cheap reflection avoidance and also matches
            // what the Editor menu does.
            _hpBar = canvasGo.GetComponent<HPBar>();
#if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(_hpBar);
            so.FindProperty("fillImage").objectReferenceValue = fillImg;
            so.FindProperty("labelText").objectReferenceValue = tmp;
            so.ApplyModifiedPropertiesWithoutUndo();
#else
            // Runtime path: HPBar's serialized fields need to be set via
            // reflection since they're private. Touch-up only happens once
            // per dummy spawn so this is fine.
            var hpType = typeof(HPBar);
            var fillField = hpType.GetField("fillImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var labelField = hpType.GetField("labelText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fillField != null) fillField.SetValue(_hpBar, fillImg);
            if (labelField != null) labelField.SetValue(_hpBar, tmp);
#endif

            UpdateHPBar();
        }

        private void UpdateHPBar()
        {
            if (_hpBar == null) return;
            _hpBar.SetHP(_hp, maxHP);
        }

        // ─── Asset loaders ──────────────────────────────────────────────
        // Mirrors WinScreenBanner: prefer Resources/, fall back to the
        // Editor AssetDatabase path so the look survives outside of a
        // shipped Resources/ payload.
        private static Sprite LoadPanelSprite()
        {
            if (_cachedPanelSprite != null) return _cachedPanelSprite;
            _cachedPanelSprite = Resources.Load<Sprite>("UI/panel-rounded-sm");
#if UNITY_EDITOR
            if (_cachedPanelSprite == null)
                _cachedPanelSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.PanelSpritePath);
#endif
            return _cachedPanelSprite;
        }

        private static TMP_FontAsset LoadSophiecomicFont()
        {
            if (_cachedFont != null) return _cachedFont;
            _cachedFont = Resources.Load<TMP_FontAsset>("Fonts/sophiecomic SDF");
#if UNITY_EDITOR
            if (_cachedFont == null)
                _cachedFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TigerverseTheme.FontAssetPath);
#endif
            return _cachedFont;
        }
    }
}

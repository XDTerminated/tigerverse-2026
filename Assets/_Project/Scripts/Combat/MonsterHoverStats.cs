using System.Text;
using TMPro;
using Tigerverse.Net;
using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Shows a floating "stats card" above the monster while the local
    /// player's hand or pointer is hovering near it. Read-only, players
    /// can't edit stats. Used during the post-hatch inspection phase so
    /// players can examine what their scribble can do before battle.
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterHoverStats : MonoBehaviour
    {
        [Tooltip("Distance in metres at which a controller counts as 'hovering' over the monster.")]
        [SerializeField] private float hoverRadius = 0.45f;
        [Tooltip("Card hovers this far above the monster pivot.")]
        [SerializeField] private float cardHeight = 0.85f;
        [Tooltip("Optional override; if null we look up the local stats via SessionApiClient.")]
        [SerializeField] private MonsterStatsData stats;
        [Tooltip("Slot index, used to fetch this monster's stats from the live session (0=p1, 1=p2).")]
        [SerializeField] public int slotIndex = 0;

        private GameObject _card;
        private TextMeshPro _label;
        private Transform _leftCtrl, _rightCtrl;
        private bool _leftInside, _rightInside;
        private bool _leftPrimaryWas, _rightPrimaryWas;
        private bool _visible;
        private float _phase;
        private float _appearTime;
        private float _lastFindAt;
        private Vector3 _cardBaseScale = Vector3.one;
        private MonsterCry _cry;

        private void Awake()
        {
            BuildCard();
            _card.SetActive(false);
            _cry = GetComponentInChildren<MonsterCry>(true);
        }

        private void Start()
        {
            FindControllers();
        }

        public void SetStats(MonsterStatsData s) => stats = s;

        private void FindControllers()
        {
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            _leftCtrl  = FindUnderRig(origin.transform, "Left Controller")
                      ?? FindUnderRig(origin.transform, "LeftHand Controller")
                      ?? FindUnderRig(origin.transform, "Left Hand Controller");
            _rightCtrl = FindUnderRig(origin.transform, "Right Controller")
                      ?? FindUnderRig(origin.transform, "RightHand Controller")
                      ?? FindUnderRig(origin.transform, "Right Hand Controller");
        }

        private static Transform FindUnderRig(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private void Update()
        {
            _phase += Time.deltaTime;
            if ((_leftCtrl == null || _rightCtrl == null) && Time.unscaledTime - _lastFindAt > 0.5f)
            {
                _lastFindAt = Time.unscaledTime;
                FindControllers();
            }

            // Strict controller-hover only, no camera-proximity fallback,
            // so players know it's their hand that triggered the card and
            // not just walking past the monster.
            bool leftIn  = _leftCtrl  != null && Vector3.Distance(_leftCtrl.position,  transform.position) < hoverRadius;
            bool rightIn = _rightCtrl != null && Vector3.Distance(_rightCtrl.position, transform.position) < hoverRadius;
            bool hover = leftIn || rightIn;

            // Hover-enter haptic on the hand that just entered.
            if (leftIn && !_leftInside)
                Buzz(UnityEngine.XR.XRNode.LeftHand);
            if (rightIn && !_rightInside)
                Buzz(UnityEngine.XR.XRNode.RightHand);
            _leftInside = leftIn;
            _rightInside = rightIn;

            // While hovering, A (right) or X (left) plays the monster's cry.
            if (rightIn) PollPrimaryAndCry(UnityEngine.XR.XRNode.RightHand, ref _rightPrimaryWas);
            else _rightPrimaryWas = false;
            if (leftIn) PollPrimaryAndCry(UnityEngine.XR.XRNode.LeftHand, ref _leftPrimaryWas);
            else _leftPrimaryWas = false;

            if (hover != _visible)
            {
                _visible = hover;
                _card.SetActive(_visible);
                if (_visible) { RefreshText(); _appearTime = Time.time; }
            }

            if (_visible && _card != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - _card.transform.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 1e-4f)
                        _card.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }
                // Pop-in scale animation: 0 → full over 0.18s with a slight overshoot.
                float t = Mathf.Clamp01((Time.time - _appearTime) / 0.18f);
                float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.10f;
                _card.transform.localScale = _cardBaseScale * t * overshoot;

                // Subtle bob.
                _card.transform.localPosition = new Vector3(0f, cardHeight + Mathf.Sin(_phase * 1.6f) * 0.015f, 0f);
            }
        }

        private static void Buzz(UnityEngine.XR.XRNode node)
        {
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                dev.SendHapticImpulse(0, 0.25f, 0.05f);
        }

        // Reads primaryButton (A on right, X on left). On press-edge while
        // hovering, plays the monster's name cry once and gives a stronger
        // haptic confirmation.
        private void PollPrimaryAndCry(UnityEngine.XR.XRNode node, ref bool wasPressed)
        {
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!dev.isValid) { wasPressed = false; return; }
            bool isPressed = false;
            dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out isPressed);
            if (isPressed && !wasPressed)
            {
                if (_cry == null) _cry = GetComponentInChildren<MonsterCry>(true);
                if (_cry == null)
                {
                    Debug.LogWarning($"[MonsterHoverStats] '{name}', no MonsterCry component found, cry SKIPPED.");
                }
                else
                {
                    Debug.Log($"[MonsterHoverStats] Cry-press via {node} on '{_cry.gameObject.name}', calling PlaySpawn().");
                    _cry.PlaySpawn();
                }
                if (dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    dev.SendHapticImpulse(0, 0.55f, 0.08f);
            }
            wasPressed = isPressed;
        }

        private void BuildCard()
        {
            _card = new GameObject("StatsCard");
            _card.transform.SetParent(transform, false);
            _card.transform.localPosition = new Vector3(0f, cardHeight, 0f);

            // Backing quad
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Backing";
            var bgCol = bg.GetComponent<Collider>(); if (bgCol != null) Destroy(bgCol);
            bg.transform.SetParent(_card.transform, false);
            bg.transform.localPosition = new Vector3(0, 0, 0.001f);
            bg.transform.localScale = new Vector3(0.55f, 0.40f, 1f);
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.99f, 0.97f, 0.92f, 1f));
            else mat.color = new Color(0.99f, 0.97f, 0.92f, 1f);
            bg.GetComponent<Renderer>().sharedMaterial = mat;

            var lblGo = new GameObject("Text");
            lblGo.transform.SetParent(_card.transform, false);
            lblGo.transform.localPosition = new Vector3(0, 0, 0f);
            _label = lblGo.AddComponent<TextMeshPro>();
            _label.text = "";
            _label.fontSize = 0.35f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(0.07f, 0.06f, 0.10f);
            _label.outlineColor = new Color32(255, 255, 255, 220);
            _label.outlineWidth = 0.12f;
            _label.enableWordWrapping = true;
            _label.rectTransform.sizeDelta = new Vector2(0.50f, 0.36f);
        }

        private void RefreshText()
        {
            if (_label == null) return;

            // Pull from session if we don't have local stats yet.
            MonsterStatsData s = stats;
            if (s == null)
            {
                var api = FindFirstObjectByType<SessionApiClient>();
                var sess = api != null ? api.LastSession : null;
                if (sess != null)
                    s = (slotIndex == 0 ? sess.p1 : sess.p2)?.stats;
            }

            if (s == null)
            {
                _label.text = "Stats unavailable";
                return;
            }

            var sb = new StringBuilder();
            string elem = string.IsNullOrEmpty(s.element) ? "Neutral" : Capitalize(s.element);
            sb.Append("<b>").Append(elem).Append("</b>\n");
            sb.Append("HP: ").Append(s.hp).Append('\n');
            sb.Append("ATK: ").Append(s.attackMult.ToString("F2")).Append('\n');
            sb.Append("SPD: ").Append(s.speed.ToString("F2"));
            if (s.moves != null && s.moves.Length > 0)
            {
                sb.Append("\nMoves: ");
                for (int i = 0; i < s.moves.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(s.moves[i]);
                }
            }
            _label.text = sb.ToString();
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}

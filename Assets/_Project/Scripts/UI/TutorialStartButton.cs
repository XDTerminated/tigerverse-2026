using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Tigerverse.UI
{
    /// <summary>
    /// Paper-craft 3D "START TUTORIAL" button that hovers next to the
    /// player's egg. Press by either:
    ///   - Touching with a VR controller (proximity poke), or
    ///   - Clicking it in the Game view with the mouse (editor / flat).
    /// Fires <see cref="OnPressed"/> exactly once, then plays a press-down
    /// animation and destroys itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialStartButton : MonoBehaviour
    {
        public event Action OnPressed;

        [Tooltip("Distance from button centre at which a controller counts as 'pressing' it.")]
        [SerializeField] private float pokeRadius = 0.13f;

        [Tooltip("Distance at which a controller counts as 'hovering' (highlight + haptic but no press). Should be > pokeRadius.")]
        [SerializeField] private float hoverRadius = 0.32f;

        [Tooltip("Button label.")]
        [SerializeField] private string label = "START TUTORIAL";

        private Transform _leftCtrl, _rightCtrl;
        private bool      _leftInside, _rightInside;
        private bool      _leftHovering, _rightHovering;
        private bool      _consumed;
        private Camera    _cam;
        private TextMeshPro _labelTmp;
        private GameObject  _backing;
        private GameObject  _shadow;
        private float       _phase;
        private bool        _hoverState;
        private Color       _backingBaseColor = new Color(1.00f, 0.84f, 0.36f);
        private Color       _backingHoverColor = new Color(1.00f, 0.95f, 0.55f);
        private Material    _backingMat;

        private void Awake() { BuildVisual(); }

        private void Start()
        {
            FindControllers();
            _cam = Camera.main;
        }

        private void BuildVisual()
        {
            // Backing — flat paper-craft card.
            _backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _backing.name = "BtnBacking";
            _backing.transform.SetParent(transform, false);
            _backing.transform.localPosition = Vector3.zero;
            _backing.transform.localScale = new Vector3(0.46f, 0.18f, 0.04f);

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _backingBaseColor);
            else mat.color = _backingBaseColor;
            _backingMat = mat;
            _backing.GetComponent<Renderer>().sharedMaterial = mat;
            // Keep BoxCollider — needed for mouse raycast and acts as visual reference for poke.

            // Soft shadow plate behind the button to give it depth.
            _shadow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _shadow.name = "BtnShadow";
            DestroyIfExists(_shadow.GetComponent<Collider>());
            _shadow.transform.SetParent(transform, false);
            _shadow.transform.localPosition = new Vector3(0.012f, -0.014f, 0.018f);
            _shadow.transform.localScale = new Vector3(0.46f, 0.18f, 0.02f);
            var smat = new Material(sh);
            if (smat.HasProperty("_BaseColor")) smat.SetColor("_BaseColor", new Color(0.20f, 0.16f, 0.10f));
            else smat.color = new Color(0.20f, 0.16f, 0.10f);
            _shadow.GetComponent<Renderer>().sharedMaterial = smat;

            // Label.
            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(transform, false);
            lblGo.transform.localPosition = new Vector3(0f, 0f, -0.025f);
            _labelTmp = lblGo.AddComponent<TextMeshPro>();
            _labelTmp.text = label;
            _labelTmp.fontSize = 0.45f;
            _labelTmp.alignment = TextAlignmentOptions.Center;
            _labelTmp.color = new Color(0.10f, 0.07f, 0.05f);
            _labelTmp.outlineColor = new Color32(255, 255, 255, 200);
            _labelTmp.outlineWidth = 0.18f;
            _labelTmp.fontStyle = FontStyles.Bold;
            _labelTmp.enableWordWrapping = false;
            var rt = _labelTmp.rectTransform;
            rt.sizeDelta = new Vector2(0.42f, 0.18f);
        }

        private static void DestroyIfExists(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        private void FindControllers()
        {
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            _leftCtrl = FindUnderRig(origin.transform, "Left Controller")
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
            if (_consumed) return;

            _phase += Time.deltaTime;

            // VR controller poke.
            if (_leftCtrl == null || _rightCtrl == null) FindControllers();

            // Hover state — checked separately at a wider radius so the
            // player gets clear visual + haptic feedback before they're
            // close enough to actually press.
            bool nowHover = false;
            CheckHover(_leftCtrl,  ref _leftHovering,  XRNode.LeftHand,  ref nowHover);
            CheckHover(_rightCtrl, ref _rightHovering, XRNode.RightHand, ref nowHover);
            UpdateHoverVisual(nowHover);

            // Pulse: bigger and brighter when hovered.
            float pulseMag = _hoverState ? 0.10f : 0.05f;
            float pulse = 1f + Mathf.Sin(_phase * 2.6f) * pulseMag;
            float scaleBoost = _hoverState ? 1.18f : 1.0f;
            _backing.transform.localScale = new Vector3(0.46f * pulse * scaleBoost, 0.18f * pulse * scaleBoost, 0.04f);

            if (CtrlEnter(_leftCtrl,  ref _leftInside)
             || CtrlEnter(_rightCtrl, ref _rightInside))
            {
                Press();
                return;
            }

            // Mouse click via Input System.
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    Vector2 mp = Mouse.current.position.ReadValue();
                    Ray ray = _cam.ScreenPointToRay(mp);
                    if (Physics.Raycast(ray, out RaycastHit hit, 50f))
                    {
                        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
                        {
                            Press();
                            return;
                        }
                    }
                }
            }
        }

        private bool CtrlEnter(Transform ctrl, ref bool inside)
        {
            if (ctrl == null) return false;
            float dist = Vector3.Distance(ctrl.position, transform.position);
            bool nowInside = dist < pokeRadius;
            bool justEntered = nowInside && !inside;
            inside = nowInside;
            return justEntered;
        }

        private void CheckHover(Transform ctrl, ref bool wasHovering, XRNode node, ref bool nowHover)
        {
            if (ctrl == null) return;
            float dist = Vector3.Distance(ctrl.position, transform.position);
            bool isHovering = dist < hoverRadius;
            if (isHovering) nowHover = true;
            if (isHovering && !wasHovering)
            {
                // Hover-enter haptic: brief soft buzz so the player knows they're in range.
                var dev = InputDevices.GetDeviceAtXRNode(node);
                if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    dev.SendHapticImpulse(0, 0.20f, 0.04f);
            }
            wasHovering = isHovering;
        }

        private void UpdateHoverVisual(bool nowHover)
        {
            if (nowHover == _hoverState) return;
            _hoverState = nowHover;
            if (_backingMat != null)
            {
                Color c = _hoverState ? _backingHoverColor : _backingBaseColor;
                if (_backingMat.HasProperty("_BaseColor")) _backingMat.SetColor("_BaseColor", c);
                else _backingMat.color = c;
            }
        }

        public void Press()
        {
            if (_consumed) return;
            _consumed = true;
            try { OnPressed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            StartCoroutine(PressAndFade());
        }

        private IEnumerator PressAndFade()
        {
            // Quick punch-down + scale-out.
            float t = 0f;
            const float dur = 0.30f;
            Vector3 start = transform.localScale;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                transform.localScale = start * (1f - k);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}

using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Tigerverse.UI
{
    /// <summary>
    /// Reusable paper-craft 3D button for the title screen and other VR menus.
    /// Press by hovering with a controller and squeezing the trigger, or by
    /// clicking with the mouse in the editor / flat. Persists across presses
    /// (unlike TutorialStartButton which self-destroys), and supports a
    /// disabled / grayed-out state that ignores input until enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public class Title3DButton : MonoBehaviour
    {
        public event Action OnPressed;

        [SerializeField] private float hoverRadius = 0.32f;

        private Transform _leftCtrl, _rightCtrl;
        private bool      _leftHovering, _rightHovering;
        private bool      _leftTrigWas = true, _rightTrigWas = true;
        private Camera    _cam;
        private TextMeshPro _labelTmp;
        private GameObject  _backing;
        private GameObject  _shadow;
        private float       _phase;
        private float       _lastFindAt;
        private bool        _hoverState;
        private bool        _disabled;
        private string      _label = "BUTTON";
        private Vector3     _backingScale = new Vector3(0.46f, 0.18f, 0.04f);

        private Color _backingBaseColor   = new Color(1.00f, 0.84f, 0.36f);
        private Color _backingHoverColor  = new Color(1.00f, 0.95f, 0.55f);
        private Color _disabledBaseColor  = new Color(0.55f, 0.50f, 0.45f);
        private Color _disabledLabelColor = new Color(0.30f, 0.27f, 0.25f);
        private Color _enabledLabelColor  = new Color(0.10f, 0.07f, 0.05f);
        private Material _backingMat;

        public void Configure(string label, Vector3 backingScale, Color baseColor, Color hoverColor)
        {
            _label = label ?? "BUTTON";
            _backingScale = backingScale;
            _backingBaseColor = baseColor;
            _backingHoverColor = hoverColor;
            if (_labelTmp != null) _labelTmp.text = _label;
            if (_backing != null) _backing.transform.localScale = _backingScale;
            if (_backingMat != null && !_disabled)
            {
                if (_backingMat.HasProperty("_BaseColor")) _backingMat.SetColor("_BaseColor", _backingBaseColor);
                else _backingMat.color = _backingBaseColor;
            }
        }

        public void SetDisabled(bool disabled)
        {
            _disabled = disabled;
            if (_backingMat != null)
            {
                Color c = _disabled ? _disabledBaseColor : _backingBaseColor;
                if (_backingMat.HasProperty("_BaseColor")) _backingMat.SetColor("_BaseColor", c);
                else _backingMat.color = c;
            }
            if (_labelTmp != null)
                _labelTmp.color = _disabled ? _disabledLabelColor : _enabledLabelColor;
        }

        public bool IsDisabled => _disabled;

        private void Awake() { BuildVisual(); }

        private void Start()
        {
            FindControllers();
            _cam = Camera.main;
        }

        private void BuildVisual()
        {
            // Backing card — flat paper-craft cube.
            _backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _backing.name = "BtnBacking";
            _backing.transform.SetParent(transform, false);
            _backing.transform.localPosition = Vector3.zero;
            _backing.transform.localScale = _backingScale;

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _backingBaseColor);
            else mat.color = _backingBaseColor;
            _backingMat = mat;
            _backing.GetComponent<Renderer>().sharedMaterial = mat;

            // Soft shadow plate behind the button to give it depth.
            _shadow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _shadow.name = "BtnShadow";
            DestroyIfExists(_shadow.GetComponent<Collider>());
            _shadow.transform.SetParent(transform, false);
            _shadow.transform.localPosition = new Vector3(0.012f, -0.014f, 0.018f);
            _shadow.transform.localScale = new Vector3(_backingScale.x, _backingScale.y, _backingScale.z * 0.5f);
            var smat = new Material(sh);
            if (smat.HasProperty("_BaseColor")) smat.SetColor("_BaseColor", new Color(0.20f, 0.16f, 0.10f));
            else smat.color = new Color(0.20f, 0.16f, 0.10f);
            _shadow.GetComponent<Renderer>().sharedMaterial = smat;

            // Label.
            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(transform, false);
            lblGo.transform.localPosition = new Vector3(0f, 0f, -0.025f);
            _labelTmp = lblGo.AddComponent<TextMeshPro>();
            _labelTmp.text = _label;
            _labelTmp.fontSize = 0.45f;
            _labelTmp.alignment = TextAlignmentOptions.Center;
            _labelTmp.color = _enabledLabelColor;
            _labelTmp.outlineColor = new Color32(255, 255, 255, 200);
            _labelTmp.outlineWidth = 0.18f;
            _labelTmp.fontStyle = FontStyles.Bold;
            _labelTmp.enableWordWrapping = false;
            var rt = _labelTmp.rectTransform;
            rt.sizeDelta = new Vector2(_backingScale.x - 0.04f, _backingScale.y);
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
            _phase += Time.deltaTime;

            if ((_leftCtrl == null || _rightCtrl == null) && Time.unscaledTime - _lastFindAt > 0.5f)
            {
                _lastFindAt = Time.unscaledTime;
                FindControllers();
            }

            bool nowHover = false;
            CheckHover(_leftCtrl,  ref _leftHovering,  XRNode.LeftHand,  ref nowHover);
            CheckHover(_rightCtrl, ref _rightHovering, XRNode.RightHand, ref nowHover);
            UpdateHoverVisual(nowHover);

            // Pulse: bigger and brighter when hovered (subtle when disabled).
            float pulseMag = _disabled ? 0.02f : (_hoverState ? 0.10f : 0.05f);
            float pulse = 1f + Mathf.Sin(_phase * 2.6f) * pulseMag;
            float scaleBoost = (!_disabled && _hoverState) ? 1.18f : 1.0f;
            _backing.transform.localScale = new Vector3(_backingScale.x * pulse * scaleBoost,
                                                       _backingScale.y * pulse * scaleBoost,
                                                       _backingScale.z);

            if (_disabled) return;

            if (_leftHovering  && TriggerPressEdge(XRNode.LeftHand,  ref _leftTrigWas)) { Press(); return; }
            if (_rightHovering && TriggerPressEdge(XRNode.RightHand, ref _rightTrigWas)) { Press(); return; }
            if (!_leftHovering)  _leftTrigWas  = true;
            if (!_rightHovering) _rightTrigWas = true;

            // Mouse fallback for editor / flat play.
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

        private static bool TriggerPressEdge(XRNode node, ref bool wasPressed)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            bool isPressed = false;
            if (dev.isValid)
            {
                if (!dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out isPressed))
                {
                    float axis = 0f;
                    dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out axis);
                    isPressed = axis > 0.5f;
                }
            }
            bool edge = isPressed && !wasPressed;
            wasPressed = isPressed;
            return edge;
        }

        private void CheckHover(Transform ctrl, ref bool wasHovering, XRNode node, ref bool nowHover)
        {
            if (ctrl == null) return;
            float dist = Vector3.Distance(ctrl.position, transform.position);
            bool isHovering = dist < hoverRadius;
            if (isHovering) nowHover = true;
            if (!_disabled && isHovering && !wasHovering)
            {
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
            if (_disabled) return;
            if (_backingMat != null)
            {
                Color c = _hoverState ? _backingHoverColor : _backingBaseColor;
                if (_backingMat.HasProperty("_BaseColor")) _backingMat.SetColor("_BaseColor", c);
                else _backingMat.color = c;
            }
        }

        public void Press()
        {
            if (_disabled) return;
            try { OnPressed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            StartCoroutine(PressBounce());
        }

        private IEnumerator PressBounce()
        {
            // Quick punch-down then back to normal.
            float t = 0f;
            const float dur = 0.20f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float dip = Mathf.Sin(k * Mathf.PI) * 0.20f;
                _backing.transform.localPosition = new Vector3(0f, 0f, dip * 0.04f);
                yield return null;
            }
            _backing.transform.localPosition = Vector3.zero;
        }
    }
}

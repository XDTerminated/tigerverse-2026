using UnityEngine;

namespace Tigerverse.Net
{
    /// <summary>
    /// FBX-backed humanoid body, attached at runtime to the existing
    /// PlayerAvatar head/hand transforms. Loads a Casual / Male_Casual
    /// character prefab from Resources and anchors it below the synced
    /// head transform. The networked hand/head transforms still drive
    /// the existing visual cubes, this body follows head yaw only.
    /// </summary>
    [DisallowMultipleComponent]
    public class PaperHumanoid : MonoBehaviour
    {
        [Header("Sources (synced transforms)")]
        public Transform headSrc;
        public Transform leftHandSrc;
        public Transform rightHandSrc;

        [Header("Anchor")]
        [Tooltip("Distance below headSrc where the model's feet/root sits. Tuned so the FBX's head ends up roughly at headSrc.")]
        [SerializeField] private float bodyDropFromHead = 1.7f;

        [Header("Variant")]
        [Tooltip("Resources/Characters prefab name. Alternates Casual / MaleCasual per instance if left blank.")]
        [SerializeField] private string variantPrefab = "";

        [Header("Animation hooks")]
        [Tooltip("Speed (m/s) above which the avatar is considered walking. Below this, the Walk bool is set false.")]
        [SerializeField] private float walkSpeedThreshold = 0.10f;
        [Tooltip("Smoothing for velocity tracking so the Walk bool doesn't flicker between frames.")]
        [SerializeField] private float walkVelocityLerp = 0.30f;

        public Renderer[] BodyRenderers { get; private set; }

        private Transform _model;
        private Animator  _animator;
        private Vector3   _lastPos;
        private float     _smoothedSpeed;
        private bool      _isWalking;
        private static readonly int PointHash = Animator.StringToHash("Point");
        private static readonly int WalkHash  = Animator.StringToHash("Walk");

        [SerializeField] private string displayName = "Player";
        private Tigerverse.UI.BillboardLabel _nameLabel;
        public void SetDisplayName(string name)
        {
            displayName = name;
            if (_nameLabel != null) _nameLabel.SetText(name);
        }

        // Static round-robin so successive remote players get different
        // models without explicit configuration.
        private static int _variantCounter;

        private void Awake()
        {
            BuildBody();
        }

        public void SetBodyColor(Color body, Color accent)
        {
            // FBX models ship with their own materials. No-op kept for
            // compatibility with PlayerAvatar's existing call site.
        }

        private void BuildBody()
        {
            string prefabName = variantPrefab;
            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName = (_variantCounter++ % 2 == 0) ? "Casual" : "MaleCasual";
            }

            var prefab = Resources.Load<GameObject>("Characters/" + prefabName);
            if (prefab == null)
            {
                Debug.LogError($"[PaperHumanoid] Missing Resources/Characters/{prefabName}.prefab");
                return;
            }

            var inst = Instantiate(prefab, transform);
            inst.name = prefabName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            _model = inst.transform;
            _animator = inst.GetComponentInChildren<Animator>();
            _lastPos = transform.position;
            BodyRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        /// <summary>
        /// Fires the "Point" trigger on the body's Animator. Wire a Point
        /// state in Resources/Characters/Casual.controller (and MaleCasual)
        /// driven by a Trigger param named "Point". Silently no-ops if the
        /// param doesn't exist on the controller (so the C# call is safe to
        /// ship before the Animator wiring is done).
        /// </summary>
        public void PlayPoint()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
                if (p.nameHash == PointHash) { _animator.SetTrigger(PointHash); return; }
        }

        /// <summary>
        /// Forces the Walk bool on the Animator. Normally the LateUpdate
        /// velocity check sets this automatically; expose it for cases where
        /// movement should be implied from non-positional input (joystick,
        /// scripted moves, etc).
        /// </summary>
        public void SetWalking(bool walking)
        {
            _isWalking = walking;
            ApplyWalkParam();
        }

        private void ApplyWalkParam()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
                if (p.nameHash == WalkHash) { _animator.SetBool(WalkHash, _isWalking); return; }
        }

        private void EnsureNameLabel()
        {
            if (_nameLabel != null || headSrc == null) return;
            _nameLabel = Tigerverse.UI.BillboardLabel.Create(headSrc, displayName, yOffset: 0.28f);
        }

        private void LateUpdate()
        {
            if (headSrc == null) return;
            EnsureNameLabel();

            Vector3 bodyPos = headSrc.position + Vector3.down * bodyDropFromHead;
            float yaw = headSrc.eulerAngles.y;
            transform.position = bodyPos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Walk-state detection from horizontal velocity. Smoothed so a
            // single-frame teleport (scene reload, anchor recalibration)
            // doesn't briefly flip the Walk bool true.
            if (Time.deltaTime > 0f)
            {
                Vector3 delta = transform.position - _lastPos;
                delta.y = 0f;
                float instSpeed = delta.magnitude / Time.deltaTime;
                _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instSpeed, walkVelocityLerp);
            }
            _lastPos = transform.position;

            bool nowWalking = _smoothedSpeed > walkSpeedThreshold;
            if (nowWalking != _isWalking)
            {
                _isWalking = nowWalking;
                ApplyWalkParam();
            }
        }
    }
}

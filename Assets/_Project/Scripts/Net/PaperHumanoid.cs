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

        public Renderer[] BodyRenderers { get; private set; }

        private Transform _model;

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
            BodyRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
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
        }
    }
}

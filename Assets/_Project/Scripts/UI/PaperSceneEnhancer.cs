using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Single coordinator that spawns the full paper-craft scenery layer:
    /// gradient skybox, distant mountains, drifting clouds, warm lighting,
    /// floating lanterns, paper fairies, ground doodles + flora, drifting
    /// paper leaves, ambient audio, and gentle wind sway on existing
    /// BirchTrees. Toggle individual layers off via the SerializeFields if
    /// any one becomes too busy. Spawned children are parented so cleanup
    /// is automatic when this GameObject is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperSceneEnhancer : MonoBehaviour
    {
        [Header("Backdrop layers")]
        [SerializeField] private bool spawnSkybox    = true;
        [SerializeField] private bool spawnMountains = true;
        [SerializeField] private bool spawnClouds    = true;
        [SerializeField] private bool spawnLighting  = true;

        [Header("Ground & flora")]
        [SerializeField] private bool spawnFlora        = true;
        [SerializeField] private bool spawnGroundDoodles= true;
        [SerializeField] private float floraRadius      = 6f;
        [SerializeField] private int   floraCount       = 40;
        [SerializeField] private float doodleRadius     = 8f;
        [SerializeField] private int   doodleCount      = 30;

        [Header("Air layer")]
        [SerializeField] private bool spawnLeaves   = false;
        [SerializeField] private bool spawnFairies  = false;
        [SerializeField] private bool spawnLanterns = false;
        [SerializeField] private float lanternRing  = 12f;
        [SerializeField] private int   lanternCount = 6;
        [SerializeField] private int   fairyCount   = 6;

        [Header("Animation & audio")]
        [SerializeField] private bool spawnTreeSway = true;
        [SerializeField] private bool spawnAmbience = true;

        public static GameObject Spawn(Vector3? center = null, Transform follow = null)
        {
            var go = new GameObject("PaperSceneEnhancer");
            if (center.HasValue) go.transform.position = center.Value;
            var enh = go.AddComponent<PaperSceneEnhancer>();
            enh._followForLeaves = follow;
            return go;
        }

        private Transform _followForLeaves;
        private bool _spawned;

        private void Start()
        {
            if (_spawned) return; // edit-mode safety: don't double-build
            _spawned = true;

            Vector3 c = transform.position;

            // Order matters slightly — skybox + lighting first so the rest
            // is shaded under the new rig; mountains before clouds so
            // clouds layer in front; ground details after, air last.
            if (spawnSkybox)         Parent(PaperSkybox.Spawn());
            if (spawnLighting)       Parent(PaperLightingRig.Spawn());
            if (spawnMountains)      Parent(PaperMountains.Spawn(c));
            if (spawnClouds)         Parent(PaperClouds.Spawn());

            if (spawnGroundDoodles)  Parent(PaperGroundDoodles.Spawn(c, doodleRadius, doodleCount));
            if (spawnFlora)          Parent(PaperFlora.Spawn(c, floraRadius, floraCount));

            if (spawnLanterns)       Parent(PaperLanterns.Spawn(c, lanternRing, lanternCount));
            if (spawnFairies)        Parent(PaperFairies.Spawn(c, fairyCount));
            if (spawnLeaves)         Parent(PaperLeaves.Spawn(_followForLeaves));

            if (spawnTreeSway)       Parent(PaperTreeSway.InstallSceneWide().gameObject);
            if (spawnAmbience)       Parent(PaperAmbience.Spawn());
        }

        private void Parent(GameObject child)
        {
            if (child != null) child.transform.SetParent(transform, worldPositionStays: true);
        }
    }
}

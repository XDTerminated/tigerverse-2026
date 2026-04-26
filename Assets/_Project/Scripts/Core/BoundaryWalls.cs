using UnityEngine;

namespace TigerVerse.Core
{
    /// <summary>
    /// Spawns four invisible BoxCollider walls around a rectangular play area
    /// to block the XR rig's CharacterController from walking off the floor.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoundaryWalls : MonoBehaviour
    {
        [Header("Area")]
        [Tooltip("World-space center of the bounded area.")]
        public Vector3 areaCenter = Vector3.zero;

        [Tooltip("Width (X) and depth (Z) of the bounded rectangle in metres.")]
        public Vector2 areaSize = new Vector2(36f, 36f);

        [Tooltip("Vertical extent of each wall (so the player cannot crouch under).")]
        public float wallHeight = 4f;

        [Tooltip("Thickness of each wall slab.")]
        public float wallThickness = 0.5f;

        [Header("Behavior")]
        [Tooltip("If true, raycasts down at areaCenter on Start to snap walls to the floor.")]
        public bool autoFindFloor = true;

        [Tooltip("Draw a yellow wireframe box in the Scene view matching the bounded rectangle.")]
        public bool drawGizmos = true;

        private bool _spawned;

        private void Start()
        {
            SpawnWalls();
        }

        /// <summary>
        /// Programmatically configure the boundary before walls spawn.
        /// </summary>
        public void Configure(Vector3 center, Vector2 size, float height = 4f)
        {
            areaCenter = center;
            areaSize = size;
            wallHeight = height;
        }

        private void SpawnWalls()
        {
            if (_spawned) return;
            _spawned = true;

            float floorY = areaCenter.y;
            if (autoFindFloor)
            {
                Vector3 origin = areaCenter + Vector3.up * 50f;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                {
                    floorY = hit.point.y;
                }
            }

            float cx = areaCenter.x;
            float cz = areaCenter.z;
            float sx = areaSize.x;
            float sz = areaSize.y;
            float h = wallHeight;
            float t = wallThickness;
            float baseY = floorY + h * 0.5f;

            // North (+Z)
            CreateWall(
                "BoundaryWall_North",
                new Vector3(cx, baseY, cz + sz * 0.5f),
                new Vector3(sx + t * 2f, h, t));

            // South (-Z)
            CreateWall(
                "BoundaryWall_South",
                new Vector3(cx, baseY, cz - sz * 0.5f),
                new Vector3(sx + t * 2f, h, t));

            // East (+X)
            CreateWall(
                "BoundaryWall_East",
                new Vector3(cx + sx * 0.5f, baseY, cz),
                new Vector3(t, h, sz));

            // West (-X)
            CreateWall(
                "BoundaryWall_West",
                new Vector3(cx - sx * 0.5f, baseY, cz),
                new Vector3(t, h, sz));
        }

        private void CreateWall(string wallName, Vector3 worldPosition, Vector3 worldScale)
        {
            // Empty GameObject + BoxCollider; no MeshRenderer/MeshFilter so it's invisible.
            GameObject wall = new GameObject(wallName);
            wall.layer = 0; // Default
            wall.transform.SetParent(transform, worldPositionStays: false);
            wall.transform.position = worldPosition;
            wall.transform.rotation = Quaternion.identity;
            wall.transform.localScale = Vector3.one;

            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = Vector3.zero;
            // Use the collider's own size (no mesh available to scale against).
            box.size = worldScale;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            DrawBoundsGizmo(new Color(1f, 0.92f, 0.016f, 0.6f));
        }

        private void OnDrawGizmosSelected()
        {
            DrawBoundsGizmo(Color.yellow);
        }

        private void DrawBoundsGizmo(Color color)
        {
            Gizmos.color = color;
            Vector3 center = new Vector3(areaCenter.x, areaCenter.y + wallHeight * 0.5f, areaCenter.z);
            Vector3 size = new Vector3(areaSize.x, wallHeight, areaSize.y);
            Gizmos.DrawWireCube(center, size);
        }
    }
}

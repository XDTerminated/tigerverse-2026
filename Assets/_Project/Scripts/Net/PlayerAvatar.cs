#if FUSION2
using Fusion;
using UnityEngine;

namespace Tigerverse.Net
{
    /// <summary>
    /// Network-synced VR avatar (head + 2 hands as simple visuals). Each peer drives
    /// their own avatar's transforms from the local XR rig; remote peers see the
    /// networked positions.
    /// </summary>
    public class PlayerAvatar : NetworkBehaviour
    {
        [Networked] public Vector3 HeadPos { get; set; }
        [Networked] public Quaternion HeadRot { get; set; }
        [Networked] public Vector3 LeftHandPos { get; set; }
        [Networked] public Quaternion LeftHandRot { get; set; }
        [Networked] public Vector3 RightHandPos { get; set; }
        [Networked] public Quaternion RightHandRot { get; set; }

        [SerializeField] Transform headVisual;
        [SerializeField] Transform leftHandVisual;
        [SerializeField] Transform rightHandVisual;
        [SerializeField] Renderer[] colorTargets; // tinted per-player so players are distinguishable

        // Local-only refs (set in Spawned for the input-authority peer)
        private Transform _headSrc;
        private Transform _leftSrc;
        private Transform _rightSrc;

        public override void Spawned()
        {
            DontDestroyOnLoad(gameObject);

            // Tint by player number so players can tell each other apart.
            int pid = Object.InputAuthority.PlayerId;
            Color tint = pid == 0 ? new Color(0.30f, 0.55f, 1.00f) : new Color(1.00f, 0.45f, 0.55f);
            Color accent = pid == 0 ? new Color(0.10f, 0.20f, 0.55f) : new Color(0.55f, 0.10f, 0.20f);
            if (colorTargets != null)
            {
                foreach (var r in colorTargets)
                {
                    if (r != null) r.material.color = tint;
                }
            }

            if (HasInputAuthority)
            {
                // We own this avatar, find local rig sources to drive it from.
                var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (origin != null)
                {
                    _headSrc = origin.Camera != null ? origin.Camera.transform : null;

                    // Search the whole rig for controller objects (XRI ones default-disable until devices appear,
                    // so include inactive children).
                    _leftSrc = FindUnderRig(origin.transform, "Left Controller")
                            ?? FindUnderRig(origin.transform, "LeftHand Controller")
                            ?? FindUnderRig(origin.transform, "Left Hand Controller");
                    _rightSrc = FindUnderRig(origin.transform, "Right Controller")
                             ?? FindUnderRig(origin.transform, "RightHand Controller")
                             ?? FindUnderRig(origin.transform, "Right Hand Controller");
                }

                // Hide our own visuals so we don't see a head cube floating in our face.
                if (headVisual != null) headVisual.gameObject.SetActive(false);
                if (leftHandVisual != null) leftHandVisual.gameObject.SetActive(false);
                if (rightHandVisual != null) rightHandVisual.gameObject.SetActive(false);

                Debug.Log($"[PlayerAvatar] Spawned local. headSrc={_headSrc}, L={_leftSrc}, R={_rightSrc}");
            }
            else
            {
                Debug.Log($"[PlayerAvatar] Spawned remote (PlayerId={pid}). Showing visuals.");

                // Build a paper-craft humanoid body for the remote avatar so
                // it reads as a person, not three floating cubes. Anchored
                // to the existing head/hand transforms which Render() drives
                // from the networked positions.
                var bodyGo = new GameObject("PaperHumanoidBody");
                bodyGo.transform.SetParent(transform, worldPositionStays: false);
                var humanoid = bodyGo.AddComponent<PaperHumanoid>();
                humanoid.headSrc      = headVisual;
                humanoid.leftHandSrc  = leftHandVisual;
                humanoid.rightHandSrc = rightHandVisual;
                humanoid.SetBodyColor(tint, accent);
                // PlayerId is 0-based; show 1-based slot in the floating tag.
                humanoid.SetDisplayName($"Player {pid + 1}");

                // Replace the head cube with a sphere so it looks like a head.
                if (headVisual != null)
                {
                    var headRend = headVisual.GetComponent<MeshRenderer>();
                    var headMf   = headVisual.GetComponent<MeshFilter>();
                    if (headMf != null)
                    {
                        // Steal the unity primitive sphere mesh.
                        var primSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        var sphereMesh = primSphere.GetComponent<MeshFilter>().sharedMesh;
                        DestroyImmediate(primSphere);
                        headMf.sharedMesh = sphereMesh;
                        headVisual.localScale = Vector3.one * 0.22f;
                    }
                    if (headRend != null && headRend.sharedMaterial != null)
                    {
                        var headMat = headRend.material; // instance for this avatar
                        if (headMat.HasProperty("_BaseColor")) headMat.SetColor("_BaseColor", tint);
                        else headMat.color = tint;
                    }
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasInputAuthority) return;
            if (_headSrc != null)  { HeadPos = _headSrc.position;  HeadRot = _headSrc.rotation; }
            if (_leftSrc != null)  { LeftHandPos = _leftSrc.position;  LeftHandRot = _leftSrc.rotation; }
            if (_rightSrc != null) { RightHandPos = _rightSrc.position; RightHandRot = _rightSrc.rotation; }
        }

        // Recursive find by name under a root, including inactive children.
        private static Transform FindUnderRig(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (var t in all) if (t.name == name) return t;
            return null;
        }

        public override void Render()
        {
            if (HasInputAuthority) return;
            if (headVisual != null)      { headVisual.position = HeadPos;           headVisual.rotation = HeadRot; }
            if (leftHandVisual != null)  { leftHandVisual.position = LeftHandPos;   leftHandVisual.rotation = LeftHandRot; }
            if (rightHandVisual != null) { rightHandVisual.position = RightHandPos; rightHandVisual.rotation = RightHandRot; }
        }
    }
}
#else
namespace Tigerverse.Net
{
    public class PlayerAvatar : UnityEngine.MonoBehaviour { }
}
#endif

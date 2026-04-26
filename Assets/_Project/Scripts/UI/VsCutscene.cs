using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Pokemon-style "VS" transition. Walks each monster's renderer
    /// hierarchy and rebuilds it as a script-free, network-free,
    /// audio-free visual-only clone (MeshRenderers + MeshFilters with the
    /// same shared meshes and materials; SkinnedMeshRenderers are baked).
    /// The clones are parented to a head-locked stage in front of the
    /// player, posed to face the camera, with two halves crashing
    /// together at centre.
    /// </summary>
    [DisallowMultipleComponent]
    public class VsCutscene : MonoBehaviour
    {
        [SerializeField] private float distanceFromCam = 3.0f;
        [SerializeField] private float cardWidth      = 6.5f;
        [SerializeField] private float cardHeight     = 4.0f;
        [SerializeField] private float monsterDisplayHeight = 2.2f;
        [Tooltip("Base yaw applied to each clone before the inward angle. Flip 180 if monsters render back-first.")]
        [SerializeField] private float monsterFacingYawDeg  = 180f;
        [SerializeField] private float slideInSec     = 0.55f;
        [SerializeField] private float holdSec        = 2.4f;
        [SerializeField] private float slideOutSec    = 0.35f;
        [SerializeField] private float overshootDist  = 0.10f;
        [SerializeField] private float hardTimeoutSec = 8f;

        private static readonly Color LeftColor     = Color.white;
        private static readonly Color RightColor    = Color.white;
        private static readonly Color VsLetterColor = Color.black;

        public IEnumerator Play(GameObject monsterA, string nameA,
                                GameObject monsterB, string nameB,
                                Action onComplete)
        {
            float startedAt = Time.time;
            Debug.Log($"[VsCutscene] Play start. monsterA={(monsterA != null ? monsterA.name : "<null>")} nameA='{nameA}' monsterB={(monsterB != null ? monsterB.name : "<null>")} nameB='{nameB}'");

            bool finished = false;
            StartCoroutine(RunInner(monsterA, nameA, monsterB, nameB, () => finished = true));

            while (!finished && Time.time - startedAt < hardTimeoutSec) yield return null;
            if (!finished) Debug.LogWarning("[VsCutscene] HARD TIMEOUT, forcing completion.");

            try { onComplete?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            Destroy(gameObject);
        }

        private IEnumerator RunInner(GameObject monsterA, string nameA,
                                     GameObject monsterB, string nameB,
                                     Action onInnerDone)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[VsCutscene] No Camera.main, bailing.");
                onInnerDone?.Invoke();
                yield break;
            }

            transform.SetParent(cam.transform, worldPositionStays: false);
            transform.localPosition = new Vector3(0f, 0f, distanceFromCam);
            transform.localRotation = Quaternion.identity;
            transform.localScale    = Vector3.one;

            float halfWidth = cardWidth * 0.5f;
            var leftRoot  = BuildHalf("LeftHalf",  isLeft: true,  centerX: -halfWidth * 0.5f, color: LeftColor);
            var rightRoot = BuildHalf("RightHalf", isLeft: false, centerX:  halfWidth * 0.5f, color: RightColor);

            // Visual-only clone of each monster.
            var visualA = BuildVisualClone(monsterA, leftRoot,  localPos: new Vector3(-halfWidth * 0.5f, -cardHeight * 0.10f, -0.05f), yawDeg: monsterFacingYawDeg);
            var visualB = BuildVisualClone(monsterB, rightRoot, localPos: new Vector3( halfWidth * 0.5f, -cardHeight * 0.10f, -0.05f), yawDeg: monsterFacingYawDeg);
            Debug.Log($"[VsCutscene] Visuals built: A={(visualA != null ? "OK" : "NULL")} B={(visualB != null ? "OK" : "NULL")}");

            BuildLabel(leftRoot,  new Vector3(-halfWidth * 0.5f, -cardHeight * 0.42f, -0.06f), nameA);
            BuildLabel(rightRoot, new Vector3( halfWidth * 0.5f, -cardHeight * 0.42f, -0.06f), nameB);

            var vsGo = new GameObject("VS");
            vsGo.transform.SetParent(transform, false);
            vsGo.transform.localPosition = new Vector3(0f, 0f, -0.10f);
            var vsTmp = vsGo.AddComponent<TextMeshPro>();
            vsTmp.text = "VS";
            vsTmp.fontSize = 9f;
            vsTmp.fontStyle = FontStyles.Bold;
            vsTmp.alignment = TextAlignmentOptions.Center;
            vsTmp.color = VsLetterColor;
            vsTmp.outlineWidth = 0f;
            vsTmp.enableWordWrapping = false;
            ApplyThemeFont(vsTmp);
            vsTmp.rectTransform.sizeDelta = new Vector2(1.6f, 1.0f);
            vsGo.transform.localScale = Vector3.zero;

            Vector3 leftStart  = new Vector3(-cardWidth, 0f, 0f);
            Vector3 rightStart = new Vector3( cardWidth, 0f, 0f);
            Vector3 leftEnd    = Vector3.zero;
            Vector3 rightEnd   = Vector3.zero;
            Vector3 leftCrash  = new Vector3( overshootDist, 0f, 0f);
            Vector3 rightCrash = new Vector3(-overshootDist, 0f, 0f);

            leftRoot.localPosition  = leftStart;
            rightRoot.localPosition = rightStart;

            float t = 0f;
            float crashT = slideInSec * 0.78f;
            while (t < slideInSec)
            {
                t += Time.deltaTime;
                if (t < crashT)
                {
                    float k = Mathf.Clamp01(t / crashT);
                    float eased = 1f - Mathf.Pow(1f - k, 3f);
                    leftRoot.localPosition  = Vector3.Lerp(leftStart,  leftCrash,  eased);
                    rightRoot.localPosition = Vector3.Lerp(rightStart, rightCrash, eased);
                }
                else
                {
                    float k = Mathf.Clamp01((t - crashT) / Mathf.Max(slideInSec - crashT, 0.001f));
                    float eased = 1f - Mathf.Pow(1f - k, 2f);
                    leftRoot.localPosition  = Vector3.Lerp(leftCrash,  leftEnd,  eased);
                    rightRoot.localPosition = Vector3.Lerp(rightCrash, rightEnd, eased);
                }
                yield return null;
            }
            leftRoot.localPosition  = leftEnd;
            rightRoot.localPosition = rightEnd;

            t = 0f;
            const float vsPopSec = 0.55f;
            while (t < vsPopSec)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / vsPopSec);
                vsGo.transform.localScale = Vector3.one * ElasticOutNorm(k);
                yield return null;
            }
            vsGo.transform.localScale = Vector3.one;

            Debug.Log("[VsCutscene] Slide-in complete, holding.");
            yield return new WaitForSeconds(holdSec);

            t = 0f;
            while (t < slideOutSec)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / slideOutSec);
                float eased = k * k;
                leftRoot.localPosition  = Vector3.Lerp(leftEnd,  leftStart,  eased);
                rightRoot.localPosition = Vector3.Lerp(rightEnd, rightStart, eased);
                vsGo.transform.localScale = Vector3.one * (1f - eased);
                yield return null;
            }

            Debug.Log("[VsCutscene] Complete.");
            onInnerDone?.Invoke();
        }

        // ─── Visual-only clone ──────────────────────────────────────────
        // Walks the monster hierarchy and rebuilds JUST the MeshRenderers
        // (and SkinnedMeshRenderers, baked to a static pose). No scripts,
        // no audio, no network, no colliders. Cannot interfere with game
        // state, it's a dumb mesh display.
        private GameObject BuildVisualClone(GameObject src, Transform parent, Vector3 localPos, float yawDeg)
        {
            if (src == null) return null;

            var root = new GameObject(src.name + "_VsVisual");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            int copied = 0;
            foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var slice = new GameObject(mr.name);
                slice.transform.SetParent(root.transform, false);
                // Place the slice at its world position relative to the source root.
                slice.transform.localPosition = src.transform.InverseTransformPoint(mr.transform.position);
                slice.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * mr.transform.rotation;
                slice.transform.localScale    = mr.transform.lossyScale;
                var nmf = slice.AddComponent<MeshFilter>();
                nmf.sharedMesh = mf.sharedMesh;
                var nmr = slice.AddComponent<MeshRenderer>();
                nmr.sharedMaterials = mr.sharedMaterials;
                copied++;
            }
            foreach (var smr in src.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || !smr.enabled || smr.sharedMesh == null) continue;
                var baked = new Mesh();
                try { smr.BakeMesh(baked); }
                catch (Exception e) { Debug.LogException(e); continue; }

                var slice = new GameObject(smr.name);
                slice.transform.SetParent(root.transform, false);
                slice.transform.localPosition = src.transform.InverseTransformPoint(smr.transform.position);
                slice.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * smr.transform.rotation;
                slice.transform.localScale    = smr.transform.lossyScale;
                var nmf = slice.AddComponent<MeshFilter>();
                nmf.sharedMesh = baked;
                var nmr = slice.AddComponent<MeshRenderer>();
                nmr.sharedMaterials = smr.sharedMaterials;
                copied++;
            }
            Debug.Log($"[VsCutscene] Cloned {copied} renderer(s) from '{src.name}'.");

            // Auto-scale to monsterDisplayHeight (uses the freshly-built bounds).
            var rends = root.GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) if (rends[i] != null) b.Encapsulate(rends[i].bounds);
                float maxAxis = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (maxAxis > 1e-3f)
                {
                    float scale = monsterDisplayHeight / maxAxis;
                    root.transform.localScale = root.transform.localScale * scale;
                }
            }
            return root;
        }

        // ─── Builders ───────────────────────────────────────────────────
        private Transform BuildHalf(string name, bool isLeft, float centerX, Color color)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            var unlitSh = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitSh == null) unlitSh = Shader.Find("Sprites/Default");

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Bg";
            DestroyIfExists(bg.GetComponent<Collider>());
            bg.transform.SetParent(root.transform, false);
            bg.transform.localPosition = new Vector3(centerX, 0f, 0.04f);
            bg.transform.localScale    = new Vector3(cardWidth * 0.55f, cardHeight, 1f);
            bg.transform.localRotation = Quaternion.Euler(0f, 0f, isLeft ? -8f : 8f);
            var bgMat = new Material(unlitSh);
            if (bgMat.HasProperty("_BaseColor")) bgMat.SetColor("_BaseColor", color);
            else bgMat.color = color;
            bg.GetComponent<Renderer>().sharedMaterial = bgMat;

            return root.transform;
        }

        private void BuildLabel(Transform parent, Vector3 localPos, string text)
        {
            var go = new GameObject("Name");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = string.IsNullOrEmpty(text) ? "?" : text;
            tmp.fontSize = 1.2f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.outlineWidth = 0f;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(cardWidth * 0.45f, 0.40f);
            ApplyThemeFont(tmp);
        }

        private static void ApplyThemeFont(TMPro.TMP_Text tmp)
        {
#if UNITY_EDITOR
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
            if (font != null) tmp.font = font;
#endif
        }

        private static void DestroyIfExists(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        private static float ElasticOutNorm(float t)
        {
            const float p = 0.32f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - p / 4f) * (2f * Mathf.PI) / p) + 1f;
        }
    }
}

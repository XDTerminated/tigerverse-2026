using UnityEngine;
using System.Collections.Generic;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    public class PaperLanterns : MonoBehaviour
    {
        private const int LanternCountMin = 5;
        private const int LanternCountMax = 8;
        private const float RingRadiusMin = 8f;
        private const float RingRadiusMax = 18f;
        private const float AltitudeMin = 4f;
        private const float AltitudeMax = 9f;

        private const float BobAmplitude = 0.10f;
        private const float BobFrequency = 0.35f;
        private const float SwayAmplitude = 0.05f;
        private const float SwayFrequency = 0.2f;

        private const float YawSpeedMin = 0.5f;
        private const float YawSpeedMax = 1.5f;

        private static readonly Color PaperTint = new Color(1.0f, 0.85f, 0.55f);
        private static readonly Color FrameTint = new Color(0.30f, 0.20f, 0.15f);
        private static readonly Color GlowTint = new Color(1.0f, 0.75f, 0.4f);

        private float _ringRadius = 12f;
        private int _spawnCount = 6;
        private bool _countOverridden;

        private readonly List<Lantern> _lanterns = new List<Lantern>();

        private struct Lantern
        {
            public Transform t;
            public Vector3 anchor;
            public float bobPhase;
            public float swayPhaseX;
            public float swayPhaseZ;
            public float yawSpeed;
        }

        public static GameObject Spawn(Vector3 center, float ringRadius = 12f, int count = 6)
        {
            var go = new GameObject("PaperLanterns");
            go.transform.position = center;
            var pl = go.AddComponent<PaperLanterns>();
            pl._ringRadius = ringRadius;
            pl._spawnCount = count;
            pl._countOverridden = true;
            return go;
        }

        private void Start()
        {
            int count = _countOverridden
                ? Mathf.Max(1, _spawnCount)
                : Random.Range(LanternCountMin, LanternCountMax + 1);

            var paperMat = BuildUnlitMaterial(PaperTint);
            var frameMat = BuildUnlitMaterial(FrameTint);

            Vector3 origin = transform.position;

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                float radius = _countOverridden
                    ? _ringRadius * Random.Range(0.85f, 1.15f)
                    : Random.Range(RingRadiusMin, RingRadiusMax);
                float alt = Random.Range(AltitudeMin, AltitudeMax);

                Vector3 pos = origin + new Vector3(Mathf.Cos(angle) * radius, alt, Mathf.Sin(angle) * radius);

                var lantern = BuildLantern(i, paperMat, frameMat);
                lantern.transform.SetParent(transform, worldPositionStays: true);
                lantern.transform.position = pos;
                lantern.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                float dir = Random.value < 0.5f ? -1f : 1f;
                _lanterns.Add(new Lantern
                {
                    t = lantern.transform,
                    anchor = pos,
                    bobPhase = Random.Range(0f, Mathf.PI * 2f),
                    swayPhaseX = Random.Range(0f, Mathf.PI * 2f),
                    swayPhaseZ = Random.Range(0f, Mathf.PI * 2f),
                    yawSpeed = Random.Range(YawSpeedMin, YawSpeedMax) * dir
                });
            }
        }

        private void Update()
        {
            float t = Time.time;
            float dt = Time.deltaTime;
            float bobW = BobFrequency * Mathf.PI * 2f;
            float swayW = SwayFrequency * Mathf.PI * 2f;

            for (int i = 0; i < _lanterns.Count; i++)
            {
                var l = _lanterns[i];
                if (l.t == null) continue;

                float yOff = Mathf.Sin(t * bobW + l.bobPhase) * BobAmplitude;
                float xOff = Mathf.Sin(t * swayW + l.swayPhaseX) * SwayAmplitude;
                float zOff = Mathf.Cos(t * swayW + l.swayPhaseZ) * SwayAmplitude;

                l.t.position = l.anchor + new Vector3(xOff, yOff, zOff);
                l.t.Rotate(0f, l.yawSpeed * dt, 0f, Space.Self);
            }
        }

        private static GameObject BuildLantern(int index, Material paperMat, Material frameMat)
        {
            var root = new GameObject("PaperLantern_" + index);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            StripCollider(body);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
            ApplyMaterial(body, paperMat);

            var topCap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topCap.name = "TopCap";
            StripCollider(topCap);
            topCap.transform.SetParent(root.transform, false);
            topCap.transform.localPosition = new Vector3(0f, 0.30f, 0f);
            topCap.transform.localScale = new Vector3(0.40f, 0.06f, 0.40f);
            ApplyMaterial(topCap, frameMat);

            var bottomCap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bottomCap.name = "BottomCap";
            StripCollider(bottomCap);
            bottomCap.transform.SetParent(root.transform, false);
            bottomCap.transform.localPosition = new Vector3(0f, -0.30f, 0f);
            bottomCap.transform.localScale = new Vector3(0.40f, 0.06f, 0.40f);
            ApplyMaterial(bottomCap, frameMat);

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.zero;
            var pl = lightGo.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.range = 4f;
            pl.intensity = 0.8f;
            pl.color = GlowTint;
            pl.shadows = LightShadows.None;

            return root;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static void ApplyMaterial(GameObject go, Material mat)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Material BuildUnlitMaterial(Color tint)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            return mat;
        }
    }
}

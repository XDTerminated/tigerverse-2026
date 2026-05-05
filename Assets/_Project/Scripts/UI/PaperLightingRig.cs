using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperLightingRig : MonoBehaviour
    {
        Light _key;
        Light _fill;
        Light _rim;

        readonly List<Light> _disabledExisting = new List<Light>();

        AmbientMode _prevAmbientMode;
        Color _prevAmbientLight;
        bool _ambientCached;

        bool _prevFog;
        FogMode _prevFogMode;
        Color _prevFogColor;
        float _prevFogStart;
        float _prevFogEnd;
        float _prevFogDensity;
        bool _fogCached;

        public static GameObject Spawn()
        {
            var existing = FindObjectOfType<PaperLightingRig>();
            if (existing != null) return existing.gameObject;

            var go = new GameObject("PaperLightingRig");
            go.AddComponent<PaperLightingRig>();
            return go;
        }

        void Start()
        {
            CacheAndDisableExistingDirectionalLights();
            CacheAndApplyAmbient();
            CacheAndApplyFog();
            BuildRig();
        }

        void CacheAndDisableExistingDirectionalLights()
        {
            var all = FindObjectsOfType<Light>(true);
            foreach (var l in all)
            {
                if (l == null) continue;
                if (l.gameObject == gameObject) continue;
                if (l.transform.IsChildOf(transform)) continue;
                if (l.type != LightType.Directional) continue;
                if (!l.enabled) continue;

                l.enabled = false;
                _disabledExisting.Add(l);
            }
        }

        void CacheAndApplyAmbient()
        {
            _prevAmbientMode = RenderSettings.ambientMode;
            _prevAmbientLight = RenderSettings.ambientLight;
            _ambientCached = true;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f, 1f);
        }

        void CacheAndApplyFog()
        {
            _prevFog = RenderSettings.fog;
            _prevFogMode = RenderSettings.fogMode;
            _prevFogColor = RenderSettings.fogColor;
            _prevFogStart = RenderSettings.fogStartDistance;
            _prevFogEnd = RenderSettings.fogEndDistance;
            _prevFogDensity = RenderSettings.fogDensity;
            _fogCached = true;

            // Soft cool-grey haze tuned to the paper-craft palette: starts past
            // the arena footprint (~7m flora radius) and reaches full opacity by
            // the mountain ring. URP requires the active renderer to have the
            // Render Settings fog feature on; if it's off in the URP asset the
            // RenderSettings flag is silently ignored.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.86f, 0.88f, 0.92f, 1f);
            RenderSettings.fogStartDistance = 10f;
            RenderSettings.fogEndDistance = 55f;
        }

        void BuildRig()
        {
            // Pure greyscale — no warmth, no color casts.
            _key = CreateLight("PaperLight_Key",
                new Color(0.95f, 0.95f, 0.95f, 1f),
                1.1f,
                new Vector3(50f, -30f, 0f));

            _fill = CreateLight("PaperLight_Fill",
                new Color(0.85f, 0.85f, 0.85f, 1f),
                0.45f,
                new Vector3(45f, 150f, 0f));

            _rim = CreateLight("PaperLight_Rim",
                new Color(0.92f, 0.92f, 0.92f, 1f),
                0.35f,
                new Vector3(-25f, 180f, 0f));
        }

        Light CreateLight(string name, Color color, float intensity, Vector3 euler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(euler);

            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            return l;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _disabledExisting.Count; i++)
            {
                var l = _disabledExisting[i];
                if (l != null) l.enabled = true;
            }
            _disabledExisting.Clear();

            if (_ambientCached)
            {
                RenderSettings.ambientMode = _prevAmbientMode;
                RenderSettings.ambientLight = _prevAmbientLight;
            }

            if (_fogCached)
            {
                RenderSettings.fog = _prevFog;
                RenderSettings.fogMode = _prevFogMode;
                RenderSettings.fogColor = _prevFogColor;
                RenderSettings.fogStartDistance = _prevFogStart;
                RenderSettings.fogEndDistance = _prevFogEnd;
                RenderSettings.fogDensity = _prevFogDensity;
            }
        }
    }
}

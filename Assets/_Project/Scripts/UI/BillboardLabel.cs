using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// World-space "Player 1" / "Professor" / etc. tag that floats above an
    /// avatar's head and rotates each frame to face the local camera.
    /// Built procedurally so any script can attach one with a single call.
    /// Uses the Sophiecomic SDF font when available (loaded from Resources or
    /// the editor asset path); falls back to TMP's default font otherwise.
    /// </summary>
    public class BillboardLabel : MonoBehaviour
    {
        public TMP_Text Label { get; private set; }

        private Camera _cam;

        /// <summary>
        /// Spawn a label child of <paramref name="parent"/>, sized for ~1m
        /// reading distance. Returns the new BillboardLabel component so
        /// callers can adjust position / text later.
        /// </summary>
        public static BillboardLabel Create(Transform parent, string text, float yOffset = 0.40f)
        {
            var go = new GameObject("BillboardLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 500;
            go.AddComponent<CanvasScaler>();

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(600, 160);
            // 0.0015 → ~0.9m wide × 0.24m tall at 1:1, big enough to read
            // from a few meters away in the headset.
            go.transform.localScale = Vector3.one * 0.0015f;

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lblRT = (RectTransform)labelGo.transform;
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;

            var tmp = labelGo.GetComponent<TMP_Text>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 110;
            tmp.color = Color.black;
            tmp.raycastTarget = false;
            tmp.font = LoadSophiecomicFont() ?? tmp.font;

            var bl = go.AddComponent<BillboardLabel>();
            bl.Label = tmp;
            return bl;
        }

        public void SetText(string text)
        {
            if (Label != null) Label.text = text;
        }

        private void LateUpdate()
        {
            // Resolve camera lazily; Camera.main can be null during scene
            // load and re-resolve cheap with a cached field.
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Face the camera (yaw + pitch). Using LookAt with the camera
            // position behind the label gives a clean billboard.
            var camPos = _cam.transform.position;
            var dir = transform.position - camPos;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        private static TMP_FontAsset _cachedFont;
        private static TMP_FontAsset LoadSophiecomicFont()
        {
            if (_cachedFont != null) return _cachedFont;
            // Try Resources first (works in builds if the font is copied
            // there). Editor falls back to AssetDatabase.
            _cachedFont = Resources.Load<TMP_FontAsset>("Fonts/sophiecomic SDF");
#if UNITY_EDITOR
            if (_cachedFont == null)
                _cachedFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
#endif
            return _cachedFont;
        }
    }
}

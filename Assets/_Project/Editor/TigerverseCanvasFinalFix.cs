#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Tigerverse.EditorTools
{
    public static class TigerverseCanvasFinalFix
    {
        [MenuItem("Tigerverse/XR -> Hard-reset Title canvas raycaster")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);
            var origin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var canvasGo = GameObject.Find("TitleCanvas");
            if (origin == null || canvasGo == null)
            {
                Debug.LogError("[Tigerverse] Missing XR Origin or TitleCanvas.");
                return;
            }

            // Strip ALL existing raycasters and re-add a fresh one.
            foreach (var rc in canvasGo.GetComponents<UnityEngine.UI.GraphicRaycaster>())
            {
                Object.DestroyImmediate(rc);
            }
            foreach (var rc in canvasGo.GetComponents<TrackedDeviceGraphicRaycaster>())
            {
                Object.DestroyImmediate(rc);
            }

            // Re-set local transform under the camera (correct position + facing the camera).
            // Canvas graphics render on its local +Z side; we want that side facing the eyes,
            // so localRotation is identity (the canvas's +Z then points the same way as camera, i.e. away from us).
            // To make the FRONT face the camera, rotate 180° on Y. We also bump z to 0.6 so it's well within the near clip.
            canvasGo.transform.SetParent(origin.Camera.transform, worldPositionStays: false);
            canvasGo.transform.localPosition = new Vector3(0, 0, 1.4f);   // 1.4m forward — comfortable arm's length
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.002f;         // ~1.4m wide canvas — fits FoV at 1.4m

            // Make the canvas tall enough to hold buttons + input + keyboard.
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(700, 1000); // taller so the StatusLabel fits inside

            // Re-layout existing children to fit the new canvas (y in canvas-local coords; +Y = up).
            // Canvas center is y=0; canvas top = +500, canvas bottom = -500.
            var hostBtn = GameObject.Find("HostButton");
            var joinBtn = GameObject.Find("JoinButton");
            var input = GameObject.Find("CodeInput");
            var status = GameObject.Find("StatusLabel");
            if (status != null)
            {
                var sRt = (RectTransform)status.transform;
                sRt.sizeDelta = new Vector2(680, 90);          // shorter so it fits
                sRt.anchoredPosition = new Vector2(0, 440);    // top of canvas (+500); top edge = +485 ✓
            }
            if (hostBtn != null) ((RectTransform)hostBtn.transform).anchoredPosition = new Vector2(0, 330);
            if (joinBtn != null) ((RectTransform)joinBtn.transform).anchoredPosition = new Vector2(0, 220);
            if (input != null)   ((RectTransform)input.transform).anchoredPosition   = new Vector2(0, 100);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = origin.Camera;

            // Add BOTH raycasters: TrackedDeviceGraphicRaycaster for XR controller rays,
            // plain GraphicRaycaster for mouse clicks (flat-mode editor / Multiplayer Play Mode).
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Canvas raycaster hard-reset. localPos=(0,0,1), worldCamera=" + origin.Camera.name);
        }
    }
}
#endif

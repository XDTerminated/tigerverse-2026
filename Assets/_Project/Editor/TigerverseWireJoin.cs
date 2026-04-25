#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Tigerverse.UI;
using Tigerverse.Core;

namespace Tigerverse.EditorTools
{
    public static class TigerverseWireJoin
    {
        [MenuItem("Tigerverse/Net -> Wire HOST + JOIN buttons (Title scene)")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var canvas = GameObject.Find("TitleCanvas");
            var hostBtn = GameObject.Find("HostButton");
            var joinBtn = GameObject.Find("JoinButton");
            var input = GameObject.Find("CodeInput");
            if (canvas == null || hostBtn == null || joinBtn == null || input == null)
            {
                Debug.LogError("[Tigerverse] Required UI children missing in TitleCanvas. Run 'Add Title Screen UI' first.");
                return;
            }

            var gsm = GameObject.FindFirstObjectByType<GameStateManager>();
            if (gsm == null) { Debug.LogError("[Tigerverse] GameStateManager not in scene."); return; }

            // Add a status label child of canvas if not present.
            var statusGo = GameObject.Find("StatusLabel");
            if (statusGo == null)
            {
                statusGo = new GameObject("StatusLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
                statusGo.transform.SetParent(canvas.transform, false);
                var rt = statusGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(720, 120);
                rt.anchoredPosition = new Vector2(0, 220);
                var tmp = statusGo.GetComponent<TMP_Text>();
                tmp.fontSize = 36;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.text = "";
            }
            var statusLabel = statusGo.GetComponent<TMP_Text>();

            // Attach JoinController to JoinButton.
            var jc = joinBtn.GetComponent<JoinController>();
            if (jc == null) jc = joinBtn.AddComponent<JoinController>();
            var jcSo = new SerializedObject(jc);
            jcSo.FindProperty("codeInput").objectReferenceValue = input.GetComponent<TMP_InputField>();
            jcSo.FindProperty("joinButton").objectReferenceValue = joinBtn.GetComponent<Button>();
            jcSo.FindProperty("gsm").objectReferenceValue = gsm;
            jcSo.FindProperty("statusLabel").objectReferenceValue = statusLabel;
            jcSo.ApplyModifiedPropertiesWithoutUndo();

            // Attach HostController to HostButton.
            var hc = hostBtn.GetComponent<HostController>();
            if (hc == null) hc = hostBtn.AddComponent<HostController>();
            var hcSo = new SerializedObject(hc);
            hcSo.FindProperty("hostButton").objectReferenceValue = hostBtn.GetComponent<Button>();
            hcSo.FindProperty("gsm").objectReferenceValue = gsm;
            hcSo.FindProperty("statusLabel").objectReferenceValue = statusLabel;
            hcSo.ApplyModifiedPropertiesWithoutUndo();

            // Strip any old persistent listeners on the buttons (they were calling JoinByCode("") etc.).
            var hostUiBtn = hostBtn.GetComponent<Button>();
            var joinUiBtn = joinBtn.GetComponent<Button>();
            for (int i = hostUiBtn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(hostUiBtn.onClick, i);
            for (int i = joinUiBtn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(joinUiBtn.onClick, i);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] HOST + JOIN buttons wired with controllers + status label. Type a code into the field and click JOIN, or click HOST and read the code from the label.");
        }
    }
}
#endif

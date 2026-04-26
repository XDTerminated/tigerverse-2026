#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tigerverse.EditorTools
{
    public static class TigerverseProjectSetup
    {
        [MenuItem("Tigerverse/Setup -> Configure Android Player Settings")]
        public static void ConfigureAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            var androidBT = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(androidBT, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(androidBT, (int)AndroidArchitecture.ARM64);
            PlayerSettings.SetApiCompatibilityLevel(androidBT, ApiCompatibilityLevel.NET_Standard);

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new UnityEngine.Rendering.GraphicsDeviceType[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);

            PlayerSettings.companyName = "Tigerverse";
            PlayerSettings.productName = "Tigerverse VR";
            PlayerSettings.SetApplicationIdentifier(androidBT, "com.tigerverse.vr");

            AssetDatabase.SaveAssets();
            Debug.Log("[Tigerverse] Android player settings configured: IL2CPP / ARM64 / Vulkan / minSdk 29 / com.tigerverse.vr");
        }

        [MenuItem("Tigerverse/Setup -> Create Empty Scenes (Title, Lobby, Battle)")]
        public static void CreateEmptyScenes()
        {
            string[] sceneNames = { "Title", "Lobby", "Battle" };
            string folder = "Assets/_Project/Scenes";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            }

            foreach (var name in sceneNames)
            {
                string path = $"{folder}/{name}.unity";
                if (System.IO.File.Exists(path))
                {
                    Debug.Log($"[Tigerverse] Scene already exists: {path}");
                    continue;
                }

                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, path);
                Debug.Log($"[Tigerverse] Created scene: {path}");
            }

            // Add scenes to build settings.
            var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (var name in sceneNames)
            {
                buildScenes.Add(new EditorBuildSettingsScene($"{folder}/{name}.unity", true));
            }
            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log($"[Tigerverse] {sceneNames.Length} scenes added to build settings.");
        }

        [MenuItem("Tigerverse/Setup -> Wire Title Scene Bootstrap")]
        public static void WireTitleSceneBootstrap()
        {
            string scenePath = "Assets/_Project/Scenes/Title.unity";
            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogError($"[Tigerverse] Title scene not found at {scenePath}. Run 'Create Empty Scenes' first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Bootstrap GO.
            var bootstrapGo = GameObject.Find("Bootstrap");
            if (bootstrapGo == null)
            {
                bootstrapGo = new GameObject("Bootstrap");
            }
            EnsureComponent<Tigerverse.Core.AppBootstrap>(bootstrapGo);
            EnsureComponent<Tigerverse.Core.GameStateManager>(bootstrapGo);
            EnsureComponent<Tigerverse.Net.SessionApiClient>(bootstrapGo);
            EnsureComponent<Tigerverse.Net.SessionRunner>(bootstrapGo);
            EnsureComponent<Tigerverse.Meshy.ModelFetcher>(bootstrapGo);

            // Voice router on its own object for easy enable/disable.
            var voiceGo = GameObject.Find("VoiceRouter");
            if (voiceGo == null)
            {
                voiceGo = new GameObject("VoiceRouter");
            }
            EnsureComponent<Tigerverse.Voice.VoiceCommandRouter>(voiceGo);

            // EventSystem (for any UGUI buttons).
            if (GameObject.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // Wire serialized references on GameStateManager.
            var gsm = bootstrapGo.GetComponent<Tigerverse.Core.GameStateManager>();
            if (gsm != null)
            {
                var so = new SerializedObject(gsm);
                SetIfPresent(so, "config", AssetDatabase.LoadAssetAtPath<Tigerverse.Net.BackendConfig>("Assets/_Project/Resources/BackendConfig.asset"));
                SetIfPresent(so, "catalog", AssetDatabase.LoadAssetAtPath<Tigerverse.Combat.MoveCatalog>("Assets/_Project/Resources/MoveCatalog.asset"));
                SetIfPresent(so, "runner", bootstrapGo.GetComponent<Tigerverse.Net.SessionRunner>());
                SetIfPresent(so, "apiClient", bootstrapGo.GetComponent<Tigerverse.Net.SessionApiClient>());
                SetIfPresent(so, "modelFetcher", bootstrapGo.GetComponent<Tigerverse.Meshy.ModelFetcher>());
                SetIfPresent(so, "voiceRouter", voiceGo.GetComponent<Tigerverse.Voice.VoiceCommandRouter>());
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Wire VoiceCommandRouter.config to the BackendConfig asset.
            var vcr = voiceGo.GetComponent<Tigerverse.Voice.VoiceCommandRouter>();
            if (vcr != null)
            {
                var so = new SerializedObject(vcr);
                SetIfPresent(so, "config", AssetDatabase.LoadAssetAtPath<Tigerverse.Net.BackendConfig>("Assets/_Project/Resources/BackendConfig.asset"));
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Wire SessionApiClient.config.
            var api = bootstrapGo.GetComponent<Tigerverse.Net.SessionApiClient>();
            if (api != null)
            {
                var so = new SerializedObject(api);
                SetIfPresent(so, "config", AssetDatabase.LoadAssetAtPath<Tigerverse.Net.BackendConfig>("Assets/_Project/Resources/BackendConfig.asset"));
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Title scene wired with Bootstrap + VoiceRouter + EventSystem (refs assigned).");
        }

        private static void SetIfPresent(SerializedObject so, string fieldName, Object reference)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[Tigerverse] Field '{fieldName}' not found on {so.targetObject.GetType().Name}, skipping.");
                return;
            }
            prop.objectReferenceValue = reference;
        }

        [MenuItem("Tigerverse/Setup -> Add Title Screen UI")]
        public static void AddTitleScreenUI()
        {
            string scenePath = "Assets/_Project/Scenes/Title.unity";
            if (!System.IO.File.Exists(scenePath)) { Debug.LogError("Title scene missing."); return; }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var canvasGo = GameObject.Find("TitleCanvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("TitleCanvas",
                    typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler),
                    typeof(UnityEngine.UI.GraphicRaycaster));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvasGo.transform.position = new Vector3(0, 1.5f, 2f);
                canvasGo.transform.localScale = Vector3.one * 0.005f;
                var rt = canvasGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(800, 600);
            }

            var bootstrap = GameObject.Find("Bootstrap");
            var gsm = bootstrap != null ? bootstrap.GetComponent<Tigerverse.Core.GameStateManager>() : null;

            // Host button.
            var hostBtnGo = GameObject.Find("HostButton");
            if (hostBtnGo == null)
            {
                hostBtnGo = CreateButton(canvasGo.transform, "HostButton", "HOST", new Vector2(0, 100));
            }
            var hostBtn = hostBtnGo.GetComponent<UnityEngine.UI.Button>();
            if (hostBtn != null && gsm != null && hostBtn.onClick.GetPersistentEventCount() == 0)
            {
                UnityEditor.Events.UnityEventTools.AddPersistentListener(hostBtn.onClick,
                    new UnityEngine.Events.UnityAction(gsm.StartHosting));
            }

            // Join button.
            var joinBtnGo = GameObject.Find("JoinButton");
            if (joinBtnGo == null)
            {
                joinBtnGo = CreateButton(canvasGo.transform, "JoinButton", "JOIN", new Vector2(0, -50));
            }

            // Code input field (TMP_InputField).
            var inputGo = GameObject.Find("CodeInput");
            if (inputGo == null)
            {
                inputGo = new GameObject("CodeInput", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                    typeof(TMPro.TMP_InputField));
                inputGo.transform.SetParent(canvasGo.transform, false);
                var rt = inputGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(360, 70);
                rt.anchoredPosition = new Vector2(0, -150);
                inputGo.GetComponent<UnityEngine.UI.Image>().color = new Color(1, 1, 1, 0.15f);

                var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
                textArea.transform.SetParent(inputGo.transform, false);
                var taRT = textArea.GetComponent<RectTransform>();
                taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
                taRT.offsetMin = new Vector2(20, 6); taRT.offsetMax = new Vector2(-20, -6);

                var textGo = new GameObject("Text", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                textGo.transform.SetParent(textArea.transform, false);
                var tx = textGo.GetComponent<TMPro.TextMeshProUGUI>();
                tx.fontSize = 36; tx.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                tx.color = Color.white; tx.text = "";
                var txRT = textGo.GetComponent<RectTransform>();
                txRT.anchorMin = Vector2.zero; txRT.anchorMax = Vector2.one;
                txRT.offsetMin = Vector2.zero; txRT.offsetMax = Vector2.zero;

                var input = inputGo.GetComponent<TMPro.TMP_InputField>();
                input.textViewport = textArea.GetComponent<RectTransform>();
                input.textComponent = tx;
                input.characterLimit = 4;
                input.contentType = TMPro.TMP_InputField.ContentType.Alphanumeric;
            }

            var joinBtn = joinBtnGo.GetComponent<UnityEngine.UI.Button>();
            if (joinBtn != null && gsm != null && joinBtn.onClick.GetPersistentEventCount() == 0)
            {
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(joinBtn.onClick,
                    new UnityEngine.Events.UnityAction<string>(gsm.JoinByCode), "");
                // Note: live binding from input.text not possible via persistent listener; user wires runtime via OnEndEdit if needed.
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Title UI added: HostButton + JoinButton + CodeInput. Wire CodeInput.OnEndEdit -> GameStateManager.JoinByCode at runtime if you want one-tap join.");
        }

        private static GameObject CreateButton(Transform parent, string name, string label, Vector2 anchored)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(360, 90);
            rt.anchoredPosition = anchored;
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.4f, 0.9f, 0.9f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lblRt = labelGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero; lblRt.offsetMax = Vector2.zero;
            var tmp = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 48; tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return go;
        }

        [MenuItem("Tigerverse/Setup -> Run Everything")]
        public static void RunEverything()
        {
            ConfigureAndroid();
            CreateEmptyScenes();
            WireTitleSceneBootstrap();
            AddTitleScreenUI();
            Debug.Log("[Tigerverse] All project setup steps complete. Open Title.unity, fill BackendConfig fields, and import Photon Fusion 2 + Meta XR SDK to enable runtime networking and MR.");
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Tigerverse.EditorTools
{
    public static class TigerverseXRSetup
    {
        // Enables OpenXR loader for Standalone (PC editor play) and Android (Quest device build).
        [MenuItem("Tigerverse/XR -> Enable OpenXR for Standalone + Android")]
        public static void EnableOpenXRForBothTargets()
        {
            EnableForGroup(BuildTargetGroup.Standalone);
            EnableForGroup(BuildTargetGroup.Android);
            AssetDatabase.SaveAssets();
            Debug.Log("[Tigerverse] OpenXR loader enabled for Standalone + Android. Restart Editor (or stop/play) for changes to take effect.");
        }

        private static void EnableForGroup(BuildTargetGroup group)
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (settings == null)
            {
                // Force-create per-build-target settings.
                XRGeneralSettingsPerBuildTarget pbts;
                EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out pbts);
                if (pbts == null)
                {
                    pbts = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(pbts, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                    EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, pbts, true);
                }
                pbts.CreateDefaultSettingsForBuildTarget(group);
                settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            }
            if (settings.AssignedSettings == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = $"{group} XR Manager Settings";
                AssetDatabase.AddObjectToAsset(manager, settings);
                settings.AssignedSettings = manager;
            }

            var loaderType = Type.GetType("UnityEngine.XR.OpenXR.OpenXRLoader, Unity.XR.OpenXR", throwOnError: false);
            if (loaderType == null)
            {
                Debug.LogError("[Tigerverse] OpenXR Loader type not found. Is com.unity.xr.openxr installed?");
                return;
            }
            string loaderTypeName = loaderType.AssemblyQualifiedName;

            // Check if already added.
            bool alreadyAdded = settings.AssignedSettings.activeLoaders.Any(l => l != null && l.GetType() == loaderType);
            if (!alreadyAdded)
            {
                bool added = XRPackageMetadataStore.AssignLoader(settings.AssignedSettings, loaderType.FullName, group);
                Debug.Log($"[Tigerverse] OpenXR loader assigned for {group}: {added}");
            }
            else
            {
                Debug.Log($"[Tigerverse] OpenXR loader already enabled for {group}.");
            }
        }

        // Replaces 'Main Camera' GO with an XR Origin (Action-based) hierarchy in the active scene.
        [MenuItem("Tigerverse/XR -> Replace Main Camera with XR Origin in Title scene")]
        public static void ReplaceMainCameraWithXrOriginInTitle()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);
            EnsureXrOriginInActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Tigerverse/XR -> Replace Main Camera with XR Origin in ALL scenes")]
        public static void ReplaceMainCameraWithXrOriginInAllScenes()
        {
            string[] scenes = {
                "Assets/_Project/Scenes/Title.unity",
                "Assets/_Project/Scenes/Lobby.unity",
                "Assets/_Project/Scenes/Battle.unity",
            };
            foreach (var path in scenes)
            {
                if (!System.IO.File.Exists(path)) continue;
                var s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                EnsureXrOriginInActiveScene();
                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
            }
            Debug.Log("[Tigerverse] XR Origin placed in all 3 scenes.");
        }

        private static void EnsureXrOriginInActiveScene()
        {
            // Already present? Skip.
            var existingOrigin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (existingOrigin != null)
            {
                Debug.Log($"[Tigerverse] XR Origin already present in scene '{existingOrigin.gameObject.scene.name}'.");
                CleanupExtraMainCameras(existingOrigin);
                return;
            }

            // Build hierarchy: XR Origin > Camera Offset > Main Camera (with TrackedPoseDriver targeting head).
            var originGo = new GameObject("XR Origin", typeof(Unity.XR.CoreUtils.XROrigin));
            originGo.transform.position = Vector3.zero;

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            offset.transform.localPosition = Vector3.zero;

            // Reuse existing Main Camera GO if present (so we don't lose AudioListener connections).
            var mainCam = Camera.main;
            GameObject camGo;
            if (mainCam != null)
            {
                camGo = mainCam.gameObject;
                camGo.transform.SetParent(offset.transform, false);
                camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
                camGo.transform.localRotation = Quaternion.identity;
            }
            else
            {
                camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.transform.SetParent(offset.transform, false);
                camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
                camGo.tag = "MainCamera";
            }

            // Add TrackedPoseDriver (Input System) targeting head.
            var driverType = Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem", throwOnError: false);
            if (driverType != null && camGo.GetComponent(driverType) == null)
            {
                camGo.AddComponent(driverType);
            }

            // Wire XR Origin component fields.
            var origin = originGo.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = camGo.GetComponent<Camera>();
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;

            CleanupExtraMainCameras(origin);
            Debug.Log($"[Tigerverse] Built XR Origin hierarchy in scene '{originGo.scene.name}'.");
        }

        private static void CleanupExtraMainCameras(Unity.XR.CoreUtils.XROrigin origin)
        {
            var keep = origin.Camera != null ? origin.Camera.gameObject : null;
            var allCams = GameObject.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var cam in allCams)
            {
                if (cam.gameObject == keep) continue;
                if (cam.CompareTag("MainCamera"))
                {
                    Debug.Log($"[Tigerverse] Removing duplicate Main Camera: {cam.gameObject.name}");
                    UnityEngine.Object.DestroyImmediate(cam.gameObject);
                }
            }
        }

        [MenuItem("Tigerverse/XR -> Enable Meta Quest OpenXR Features")]
        public static void EnableQuestOpenXrFeatures()
        {
            EnableFeatureByName("MetaQuestFeature");
            EnableFeatureByName("OculusTouchControllerProfile");
            EnableFeatureByName("MetaQuestTouchPlusControllerProfile");
            EnableFeatureByName("MetaQuestTouchProControllerProfile");
            EnableFeatureByName("OculusQuestFeature");
            AssetDatabase.SaveAssets();
            Debug.Log("[Tigerverse] Meta Quest OpenXR features enabled (controllers + Quest support).");
        }

        private static void EnableFeatureByName(string namePrefix)
        {
            const string path = "Assets/XR/Settings/OpenXR Package Settings.asset";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            int count = 0;
            foreach (var a in assets)
            {
                if (a == null) continue;
                if (a.name == null) continue;
                if (!a.name.StartsWith(namePrefix)) continue;
                var so = new SerializedObject(a);
                var prop = so.FindProperty("m_enabled");
                if (prop != null && !prop.boolValue)
                {
                    prop.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    count++;
                }
            }
            Debug.Log($"[Tigerverse]   enabled {count} '{namePrefix}*' feature(s).");
        }

        [MenuItem("Tigerverse/XR -> Run Full XR Setup")]
        public static void RunFullXrSetup()
        {
            EnableOpenXRForBothTargets();
            EnableQuestOpenXrFeatures();
            ReplaceMainCameraWithXrOriginInAllScenes();
            Debug.Log("[Tigerverse] Full XR setup complete. Connect Quest Link, hit Play, you should see passthrough/world in the headset.");
        }
    }
}
#endif

// One-click setup for Tigerverse VR + Multiplayer.
// Menu: "Tigerverse / Setup VR + Multiplayer (Quest 3)"
//
// What it does:
//   1. Switches active build target to Android (Quest is Android-based)
//   2. Enables OpenXR loader in XR Plug-in Management for Standalone (PCVR / Quest Link) AND Android (standalone Quest)
//   3. Enables Oculus Touch Controller Profile + Meta Quest Support OpenXR features
//   4. Sets Android Player Settings to Quest-friendly defaults (IL2CPP, ARM64, API 29, Linear color)
//   5. Adds GameObject/XR/XR Origin (VR) to the open scene if no XR Origin is present
//   6. Creates Bootstrap GameObject (GameBootstrap + SessionManager + ConnectUI)
//   7. Creates NetworkManager GameObject (NetworkManager + UnityTransport)
//   8. Generates Assets/Prefabs/NetworkPlayer.prefab and assigns it as the NetworkManager Player Prefab
//
// Each step is wrapped in try/catch so a single failure does not block the rest.
// Re-running the menu is safe — every step is idempotent.

using System;
using System.IO;
using System.Linq;
using Tigerverse.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Tigerverse.EditorTools
{
    public static class TigerverseSetup
    {
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string PrefabFolder = "Assets/Prefabs";
        const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

        [MenuItem("Tigerverse/Setup VR + Multiplayer (Quest 3)")]
        public static void RunAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "Tigerverse VR + Multiplayer Setup",
                    "This will:\n" +
                    " • Switch build target to Android\n" +
                    " • Enable OpenXR for Standalone + Android\n" +
                    " • Configure OpenXR features for Quest 3\n" +
                    " • Set Android Player Settings (IL2CPP, ARM64, API 29)\n" +
                    " • Add an XR Origin + Bootstrap + NetworkManager to the open scene\n" +
                    " • Generate Assets/Prefabs/NetworkPlayer.prefab\n\n" +
                    "Continue?",
                    "Run setup", "Cancel"))
            {
                return;
            }

            SafeStep("Switch to Android build target", SwitchToAndroid);
            SafeStep("Configure XR (Standalone)", () => ConfigureXRForGroup(BuildTargetGroup.Standalone));
            SafeStep("Configure XR (Android)", () => ConfigureXRForGroup(BuildTargetGroup.Android));
            SafeStep("Configure OpenXR features (Standalone)", () => ConfigureOpenXR(BuildTargetGroup.Standalone));
            SafeStep("Configure OpenXR features (Android)", () => ConfigureOpenXR(BuildTargetGroup.Android));
            SafeStep("Configure Android Player Settings", ConfigureAndroidPlayerSettings);
            SafeStep("Ensure XR Origin in scene", EnsureXROriginInScene);
            SafeStep("Wire LocalXRRigRegistry on XR Origin", WireLocalXRRigRegistry);
            SafeStep("Ensure Bootstrap GameObject", EnsureBootstrapInScene);
            SafeStep("Ensure NetworkManager GameObject", EnsureNetworkManagerInScene);
            SafeStep("Generate NetworkPlayer prefab", GenerateNetworkPlayerPrefab);
            SafeStep("Wire Player Prefab into NetworkManager", WirePlayerPrefab);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            EditorUtility.DisplayDialog(
                "Tigerverse Setup",
                "Done. Check the Console for any per-step errors.\n\n" +
                "Remaining manual steps:\n" +
                " 1. Edit > Project Settings > Services — sign in to Unity Cloud and link a project\n" +
                " 2. In your Cloud dashboard, enable Authentication, Lobby, and Relay\n" +
                " 3. Connect Quest 3 via Link cable (or build to device) and press Play",
                "OK");
        }

        // -- Build target ----------------------------------------------------

        static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return;

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogWarning("[Tigerverse] Android build support is not installed in this Unity Hub. " +
                                 "Open Unity Hub > Installs > add Android Build Support, then re-run setup.");
                return;
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        // -- XR Plug-in Management ------------------------------------------

        static void ConfigureXRForGroup(BuildTargetGroup group)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget);

            if (perTarget == null)
            {
                if (!Directory.Exists("Assets/XR")) Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            var settings = perTarget.SettingsForBuildTarget(group);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = group + " Settings";
                AssetDatabase.AddObjectToAsset(settings, perTarget);
                perTarget.SetSettingsForBuildTarget(group, settings);
            }

            if (settings.AssignedSettings == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = group + " Manager";
                AssetDatabase.AddObjectToAsset(manager, perTarget);
                settings.AssignedSettings = manager;
            }

            settings.InitManagerOnStart = true;

            if (!settings.AssignedSettings.activeLoaders.Any(l => l != null && l.GetType().FullName == OpenXRLoader))
            {
                XRPackageMetadataStore.AssignLoader(settings.AssignedSettings, OpenXRLoader, group);
            }

            EditorUtility.SetDirty(perTarget);
        }

        // -- OpenXR features -------------------------------------------------

        static void ConfigureOpenXR(BuildTargetGroup group)
        {
            var openXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXrSettings == null)
            {
                Debug.LogWarning($"[Tigerverse] No OpenXRSettings for {group} yet — settings will populate after first OpenXR import.");
                return;
            }

            foreach (var feature in openXrSettings.GetFeatures())
            {
                if (feature == null) continue;

                if (feature is OculusTouchControllerProfile)
                {
                    feature.enabled = true;
                }
                else if (feature.GetType().Name == "MetaQuestFeature")
                {
                    feature.enabled = true;
                }
            }

            EditorUtility.SetDirty(openXrSettings);
        }

        // -- Android Player Settings ----------------------------------------

        static void ConfigureAndroidPlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            PlayerSettings.Android.forceSDCardPermission = false;
        }

        // -- Scene wiring ----------------------------------------------------

        static void EnsureXROriginInScene()
        {
            if (UnityEngine.Object.FindFirstObjectByType<XROrigin>() != null) return;

            // Try the menu item registered by XR Interaction Toolkit. If the path
            // changes between XRI versions, fall back to a no-op and tell the user.
            if (EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (VR)")) return;
            if (EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Action-based)")) return;

            Debug.LogWarning("[Tigerverse] Could not auto-create XR Origin via menu. " +
                             "Manually add: GameObject > XR > XR Origin (VR).");
        }

        static void WireLocalXRRigRegistry()
        {
            var origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (origin == null) return;

            var registry = origin.GetComponent<LocalXRRigRegistry>()
                           ?? origin.gameObject.AddComponent<LocalXRRigRegistry>();

            if (registry.head == null && origin.Camera != null)
            {
                registry.head = origin.Camera.transform;
            }

            var children = origin.GetComponentsInChildren<Transform>(includeInactive: true);
            if (registry.leftHand == null)
            {
                registry.leftHand = FindControllerTransform(children, "left");
            }
            if (registry.rightHand == null)
            {
                registry.rightHand = FindControllerTransform(children, "right");
            }

            EditorUtility.SetDirty(registry);
        }

        static Transform FindControllerTransform(Transform[] candidates, string side)
        {
            // Match XRI rig naming variants: "Left Controller", "LeftHand Controller",
            // "Left Hand", "Left Direct Interactor", etc.
            foreach (var t in candidates)
            {
                var n = t.name.ToLowerInvariant();
                if (!n.Contains(side)) continue;
                if (n.Contains("controller") || n.Contains("hand") || n.Contains("interactor"))
                {
                    return t;
                }
            }
            return null;
        }

        static void EnsureBootstrapInScene()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameBootstrap>() != null) return;

            var go = new GameObject("Bootstrap",
                typeof(GameBootstrap),
                typeof(SessionManager),
                typeof(ConnectUI));

            Undo.RegisterCreatedObjectUndo(go, "Create Tigerverse Bootstrap");
        }

        static void EnsureNetworkManagerInScene()
        {
            if (UnityEngine.Object.FindFirstObjectByType<NetworkManager>() != null) return;

            var go = new GameObject("NetworkManager",
                typeof(NetworkManager),
                typeof(UnityTransport));

            var nm = go.GetComponent<NetworkManager>();
            var transport = go.GetComponent<UnityTransport>();
            nm.NetworkConfig.NetworkTransport = transport;

            Undo.RegisterCreatedObjectUndo(go, "Create Tigerverse NetworkManager");
        }

        // -- Network Player prefab ------------------------------------------

        static void GenerateNetworkPlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null) return;

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            var root = new GameObject("NetworkPlayer");
            try
            {
                var head = MakeAvatarPart("HeadAvatar", PrimitiveType.Cube,
                    new Vector3(0.18f, 0.22f, 0.22f), new Color(0.9f, 0.5f, 0.2f), root.transform);
                var leftHand = MakeAvatarPart("LeftHandAvatar", PrimitiveType.Cube,
                    new Vector3(0.08f, 0.04f, 0.12f), new Color(0.2f, 0.6f, 0.9f), root.transform);
                var rightHand = MakeAvatarPart("RightHandAvatar", PrimitiveType.Cube,
                    new Vector3(0.08f, 0.04f, 0.12f), new Color(0.9f, 0.2f, 0.4f), root.transform);

                var netObj = root.AddComponent<NetworkObject>();
                var avatar = root.AddComponent<NetworkedXRPlayer>();

                var so = new SerializedObject(avatar);
                so.FindProperty("headTarget").objectReferenceValue = head.transform;
                so.FindProperty("leftHandTarget").objectReferenceValue = leftHand.transform;
                so.FindProperty("rightHandTarget").objectReferenceValue = rightHand.transform;

                var hideArr = so.FindProperty("hideForOwner");
                hideArr.arraySize = 1;
                hideArr.GetArrayElementAtIndex(0).objectReferenceValue = head;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static GameObject MakeAvatarPart(string name, PrimitiveType primitive, Vector3 scale, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localScale = scale;

            var collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = color;
                renderer.sharedMaterial = mat;
            }

            return go;
        }

        static void WirePlayerPrefab()
        {
            var nm = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
            if (nm == null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return;

            nm.NetworkConfig.PlayerPrefab = prefab;
            EditorUtility.SetDirty(nm);
        }

        // -- Helpers ---------------------------------------------------------

        static void SafeStep(string label, Action step)
        {
            try
            {
                step();
                Debug.Log($"[Tigerverse Setup] {label} — OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Tigerverse Setup] {label} — FAILED: {e.Message}");
            }
        }
    }
}

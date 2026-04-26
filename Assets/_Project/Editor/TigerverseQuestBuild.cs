#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// One-click "build a Quest APK" pipeline. Switches platform if needed,
    /// applies the correct Player Settings (IL2CPP, ARM64, Android 10+,
    /// landscape, OpenXR), bakes the active scene + the Title scene into
    /// the build, and writes the APK under Builds/Android/.
    /// </summary>
    public static class TigerverseQuestBuild
    {
        private const string DefaultOutputDir = "Builds/Android";
        private const string ApkPrefix        = "tigerverse";

        [MenuItem("Tigerverse/Build -> Quest APK")]
        public static void BuildQuestApk()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Tigerverse/Build] Stop Play mode before building.");
                return;
            }

            // 1) Make sure we're on Android.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[Tigerverse/Build] Switching active platform to Android...");
                bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                if (!ok)
                {
                    Debug.LogError("[Tigerverse/Build] Failed to switch platform to Android. Make sure Android Build Support is installed via Unity Hub.");
                    return;
                }
            }

            // 2) Apply Player Settings required for Quest.
            ApplyQuestPlayerSettings();

            // 3) Make sure the Title scene is in the build list.
            EnsureTitleSceneInBuild();
            var scenes = GatherEnabledScenePaths();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Tigerverse/Build] No scenes enabled in Build Settings. Add at least the Title scene (File → Build Settings).");
                return;
            }

            // 4) Pick output path.
            Directory.CreateDirectory(DefaultOutputDir);
            string apkName = $"{ApkPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.apk";
            string outputPath = Path.Combine(DefaultOutputDir, apkName);

            // 5) Build.
            Debug.Log($"[Tigerverse/Build] Starting build → {outputPath}");
            var options = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = outputPath,
                target           = BuildTarget.Android,
                targetGroup      = BuildTargetGroup.Android,
                options          = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            switch (report.summary.result)
            {
                case BuildResult.Succeeded:
                    ulong mb = report.summary.totalSize / (1024UL * 1024UL);
                    Debug.Log($"[Tigerverse/Build] Build SUCCEEDED in {report.summary.totalTime.TotalSeconds:F1}s, size {mb} MB -> {outputPath}");
                    EditorUtility.RevealInFinder(outputPath);
                    break;
                case BuildResult.Failed:
                    Debug.LogError($"[Tigerverse/Build] ✗ Build FAILED: {report.summary.totalErrors} errors / {report.summary.totalWarnings} warnings.");
                    break;
                case BuildResult.Cancelled:
                    Debug.LogWarning("[Tigerverse/Build] Build cancelled by user.");
                    break;
                case BuildResult.Unknown:
                    Debug.LogError("[Tigerverse/Build] Build ended with Unknown result. Check Console for details.");
                    break;
            }
        }

        [MenuItem("Tigerverse/Build -> Verify Quest Player Settings (no build)")]
        public static void VerifyQuestPlayerSettings()
        {
            ApplyQuestPlayerSettings();
            Debug.Log("[Tigerverse/Build] Player Settings re-applied for Quest. Now you can run File → Build Settings → Build, OR use 'Tigerverse → Build → Quest APK'.");
        }

        // ─── Implementation ─────────────────────────────────────────────
        private static void ApplyQuestPlayerSettings()
        {
            // IL2CPP backend (required for Quest).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // ARM64 only.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Android 10 (API 29) min — Quest 3 / Pro need it; older Quest 2 OS works too.
            PlayerSettings.Android.minSdkVersion    = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Landscape orientation — Quest is always landscape internally.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            // Allow internet (Photon Fusion + Vercel API + ElevenLabs/Groq).
            PlayerSettings.Android.forceInternetPermission = true;

            // Color space: linear is recommended for VR.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Strip engine code = false (Photon + glTFast use reflection).
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);

            // Use 32-bit display buffer (looks better in VR).
            PlayerSettings.use32BitDisplayBuffer = true;

            Debug.Log("[Tigerverse/Build] Player Settings applied: IL2CPP, ARM64, minSdk=29, landscape, linear color, low strip.");
        }

        private static void EnsureTitleSceneInBuild()
        {
            // Find any scene named Title.unity under Assets/.
            string[] guids = AssetDatabase.FindAssets("Title t:Scene");
            string titlePath = null;
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith("/Title.unity", StringComparison.OrdinalIgnoreCase))
                {
                    titlePath = p; break;
                }
            }
            if (titlePath == null)
            {
                Debug.LogWarning("[Tigerverse/Build] Could not auto-locate a 'Title.unity' scene. Make sure your scene is added to File → Build Settings manually.");
                return;
            }

            var current = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < current.Count; i++)
            {
                if (string.Equals(current[i].path, titlePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!current[i].enabled)
                    {
                        current[i].enabled = true;
                        EditorBuildSettings.scenes = current.ToArray();
                        Debug.Log($"[Tigerverse/Build] Enabled Title scene in build list: {titlePath}");
                    }
                    return;
                }
            }
            current.Insert(0, new EditorBuildSettingsScene(titlePath, enabled: true));
            EditorBuildSettings.scenes = current.ToArray();
            Debug.Log($"[Tigerverse/Build] Added Title scene to build list at index 0: {titlePath}");
        }

        private static string[] GatherEnabledScenePaths()
        {
            var result = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s != null && s.enabled && !string.IsNullOrEmpty(s.path)) result.Add(s.path);
            }
            return result.ToArray();
        }
    }
}
#endif

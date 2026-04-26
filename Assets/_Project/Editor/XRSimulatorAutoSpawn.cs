#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Auto-instantiates the XR Device Simulator prefab when entering Play Mode
    /// in the editor, so laptop testing without a headset gets simulated HMD +
    /// controllers (WASD/QE to move, mouse to look, RMB to switch which
    /// device is being driven, T/G to toggle device, etc). Lives only as long
    /// as Play Mode runs — never saved into a scene.
    ///
    /// The prefab is the standard one shipped with XR Interaction Toolkit's
    /// "XR Device Simulator" sample, copied into Assets/Samples/.
    /// </summary>
    [InitializeOnLoad]
    public static class XRSimulatorAutoSpawn
    {
        private const string PrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.0.7/XR Device Simulator/XR Device Simulator.prefab";

        private const string EnabledKey = "Tigerverse.XRSimulator.AutoSpawn";

        static XRSimulatorAutoSpawn()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tigerverse/XR Simulator/Auto-spawn on Play (toggle)")]
        public static void ToggleEnabled()
        {
            bool now = !EditorPrefs.GetBool(EnabledKey, true);
            EditorPrefs.SetBool(EnabledKey, now);
            Debug.Log($"[Tigerverse] XR Simulator auto-spawn: {(now ? "ENABLED" : "DISABLED")}");
        }

        [MenuItem("Tigerverse/XR Simulator/Auto-spawn on Play (toggle)", validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Tigerverse/XR Simulator/Auto-spawn on Play (toggle)",
                EditorPrefs.GetBool(EnabledKey, true));
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!EditorPrefs.GetBool(EnabledKey, true)) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Tigerverse] XR Device Simulator prefab not found at '{PrefabPath}'. " +
                                 "Re-import the XRI 'XR Device Simulator' sample.");
                return;
            }

            // Skip if a simulator already exists (e.g. someone added one to the scene manually).
            var existing = Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator>();
            if (existing != null) return;

            var instance = Object.Instantiate(prefab);
            instance.name = prefab.name + " (Editor Sim)";
            // Move into the active scene rather than DontDestroyOnLoad so the
            // simulator dies cleanly when scenes change or play mode exits.
            SceneManager.MoveGameObjectToScene(instance, SceneManager.GetActiveScene());
            Debug.Log("[Tigerverse] Spawned XR Device Simulator for laptop testing. " +
                      "Use the on-screen overlay or the package docs for controls.");
        }
    }
}
#endif

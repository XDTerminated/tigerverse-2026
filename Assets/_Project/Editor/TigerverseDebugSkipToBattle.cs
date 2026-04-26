#if UNITY_EDITOR
using Tigerverse.Combat;
using Tigerverse.Core;
using Tigerverse.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Debug-only shortcut for laptop testing of the battle control mode
    /// (Trainer / Scribble) without going through the full Title → Lobby →
    /// Hatch → Inspection sequence.
    ///
    /// Usage:
    ///   1. Open Title scene + press Play.
    ///   2. Tigerverse → Debug → Spawn Battle Mode Manager.
    ///   3. Banner flashes "TRAINER MODE". Press M (or A on a real
    ///      controller) to toggle. WASD moves the dummy scribble while in
    ///      Scribble mode.
    ///
    /// This does NOT start a real BattleManager — moves won't deal damage
    /// because `voiceRouter.Bind()` was never called. To exercise voice
    /// attacks end-to-end, use 'Tigerverse → Battle → Enable Mock + Wire
    /// Spawn Pivots' and play through the real flow.
    /// </summary>
    public static class TigerverseDebugSkipToBattle
    {
        [MenuItem("Tigerverse/Debug -> Spawn Battle Mode Manager (Play mode)")]
        public static void SpawnNow()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Enter Play mode first",
                    "This shortcut wires runtime components and only works while the editor is in Play mode. Press Play, then run this menu item again.",
                    "OK");
                return;
            }

            // 1. Place the dummy scribble on the floor at the local spawn
            //    pivot's XZ. We use cube_height/2 for Y so the cube SITS on
            //    the ground instead of floating above it.
            const float cubeSize = 0.5f;
            Vector3 spawnPos = Vector3.zero;
            var pivotA = GameObject.Find("MonsterSpawnPivotA");
            if (pivotA != null) spawnPos = new Vector3(pivotA.transform.position.x, cubeSize * 0.5f, pivotA.transform.position.z);
            else spawnPos = new Vector3(0f, cubeSize * 0.5f, 0f);

            var scribble = GameObject.CreatePrimitive(PrimitiveType.Cube);
            scribble.name = "DEBUG_LocalScribble";
            scribble.transform.position = spawnPos;
            scribble.transform.localScale = Vector3.one * cubeSize;
            // Tint it red so it's obvious it's the dummy.
            var rend = scribble.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.2f, 0.2f) };
            // Strip the Collider so the trainer can't accidentally walk into it.
            var col = scribble.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            // Resolve XR rig early so we can hand its transform to the mover
            // for camera-follow ("become the scribble" POV).
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            ContinuousMoveProvider xrMove = null;
            FlatMoveController editorMove = null;
            if (origin != null)
            {
                xrMove = origin.gameObject.GetComponentInChildren<ContinuousMoveProvider>(true);
                editorMove = origin.GetComponent<FlatMoveController>();
            }

            var mover = scribble.AddComponent<ScribbleMoveController>();
            mover.target = scribble.transform;
            mover.xrOrigin = origin != null ? origin.transform : null;
            mover.enabled = false;

            // 2. Head-locked overlay banner.
            var overlayGo = new GameObject("BattleModeOverlay", typeof(RectTransform));
            var overlay = overlayGo.AddComponent<BattleModeOverlay>();

            // 3. The manager itself.
            var mgrGo = new GameObject("BattleControlModeManager");
            var mgr = mgrGo.AddComponent<BattleControlModeManager>();

            mgr.Configure(xrMove, editorMove, mover, overlay);

            // Battle HUD with placeholder moves (no voice router bound here).
            var hudGo = new GameObject("BattleHUD", typeof(RectTransform));
            var hud = hudGo.AddComponent<BattleHUD>();
            var voice = Object.FindFirstObjectByType<Tigerverse.Voice.VoiceCommandRouter>();
            hud.Configure(mgr, voice);

            Debug.Log("[Debug] Battle control mode active. Press M (editor) or A (controller) to toggle. WASD moves the dummy scribble in Scribble mode.");
        }

        [MenuItem("Tigerverse/Debug -> Despawn Battle Mode Manager (Play mode)")]
        public static void DespawnNow()
        {
            if (!EditorApplication.isPlaying) return;
            var mgr = BattleControlModeManager.Instance;
            if (mgr != null) Object.Destroy(mgr.gameObject);
            var overlay = GameObject.Find("BattleModeOverlay");
            if (overlay != null) Object.Destroy(overlay);
            var dummy = GameObject.Find("DEBUG_LocalScribble");
            if (dummy != null) Object.Destroy(dummy);
            var hud = GameObject.Find("BattleHUD");
            if (hud != null) Object.Destroy(hud);
            Debug.Log("[Debug] Battle control mode torn down. XR locomotion restored.");
        }
    }
}
#endif

using Tigerverse.Combat;
using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.MR
{
    /// <summary>
    /// Runtime test trigger that drops you into the SAME state the game
    /// reaches right before the fist-bump / READY handshake — except
    /// without needing to host a session, find a partner, draw a monster,
    /// or wait for the egg to hatch. Auto-spawns at app start as a
    /// DontDestroyOnLoad GameObject, so it's always listening.
    ///
    /// HOW TO USE ON QUEST:
    ///   Hold the LEFT controller MENU button (the little 3-line icon
    ///   below the X / Y face buttons) for ~1.5 seconds, in any scene at
    ///   any time. Doesn't matter where in the flow you are.
    ///
    /// What it does:
    ///   1. Spawns a real ReadyHandshake in front of you (the same
    ///      "I'M READY" button + voice + fist-bump listener the live
    ///      game spawns post-hatch).
    ///   2. From here, do exactly what you'd do in a real match — say
    ///      READY out loud and fist-bump (your own hands count as a
    ///      self-bump too), OR press the I'M READY button to bypass.
    ///   3. The handshake's Fire() runs through the production code path
    ///      that loads BattleMR and calls MRSession.Enter(), so you're
    ///      testing the actual MR transition, not a shortcut.
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugMRJump : MonoBehaviour
    {
        private const string MRSceneName = "BattleMR";
        // How long the LEFT menu button must be held before MR fires.
        // Long enough that an accidental tap won't trigger; short enough
        // to feel snappy when intentional.
        private const float HoldSeconds = 1.5f;
        // Re-arm gap so a single very long hold doesn't fire twice.
        private const float Cooldown = 2.0f;

        private float _holdStartedAt = -1f;
        private float _lastFireAt;
        private bool _jumping;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // Idempotent — only spawn once across scene loads.
            if (FindFirstObjectByType<DebugMRJump>() != null) return;
            var go = new GameObject("DebugMRJump");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugMRJump>();
            Debug.Log("[DebugMRJump] Hold the LEFT menu button for 1.5s anywhere in the app to spawn a ReadyHandshake (the pre-fist-bump state) for MR transition testing.");
        }

        private void Update()
        {
            if (_jumping) return;

            bool held = LeftMenuPressed();
            if (held)
            {
                if (_holdStartedAt < 0f) _holdStartedAt = Time.unscaledTime;
                float heldFor = Time.unscaledTime - _holdStartedAt;
                if (heldFor >= HoldSeconds && Time.unscaledTime - _lastFireAt > Cooldown)
                {
                    _lastFireAt = Time.unscaledTime;
                    _jumping = true;
                    SpawnHandshakeForTest();
                    _holdStartedAt = -1f;
                    _jumping = false;
                }
            }
            else
            {
                _holdStartedAt = -1f;
            }
        }

        private static bool LeftMenuPressed()
        {
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!left.isValid) return false;
            bool pressed = false;
            left.TryGetFeatureValue(CommonUsages.menuButton, out pressed);
            return pressed;
        }

        private void SpawnHandshakeForTest()
        {
            // Place the I'M READY button about 0.55 m in front of the
            // player at chest height — same offset the live game uses
            // post-hatch in GameStateManager.RunInspectionPhase.
            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();
            Vector3 head = cam != null ? cam.transform.position : new Vector3(0f, 1.6f, 0f);
            Vector3 buttonWorldPos = head + fwd * 0.55f + new Vector3(0f, -0.30f, 0f);
            Quaternion buttonWorldRot = Quaternion.identity;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - buttonWorldPos;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-4f)
                    buttonWorldRot = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }

            // Don't double-spawn if a handshake is already alive.
            var existing = FindFirstObjectByType<ReadyHandshake>();
            if (existing != null)
            {
                Debug.Log("[DebugMRJump] ReadyHandshake already in scene — leaving it alone.");
                return;
            }

            var hsGo = new GameObject("ReadyHandshake (Debug Test)");
            hsGo.transform.position = Vector3.zero;
            var hs = hsGo.AddComponent<ReadyHandshake>();
            hs.Configure(buttonWorldPos, buttonWorldRot);

            Debug.Log("[DebugMRJump] Spawned ReadyHandshake in front of you. Say READY + fist bump (or self-bump your own hands), or press the I'M READY button. The real Fire() path will load BattleMR + enter passthrough.");
        }
    }
}

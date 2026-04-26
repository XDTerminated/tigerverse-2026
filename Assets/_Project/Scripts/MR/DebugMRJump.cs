using Tigerverse.Combat;
using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.MR
{
    /// <summary>
    /// Runtime test trigger for the VR → MR transition. Two controls:
    ///
    ///   LEFT X BUTTON (tap)
    ///     Spawns two test "scribble" capsules (red + blue) standing on
    ///     the floor in front of you, in the current VR scene. You can
    ///     walk around them, look at them, confirm they're really
    ///     there. Tap X again to despawn / re-spawn at your new
    ///     position.
    ///
    ///   LEFT MENU BUTTON (hold ~1.5s)
    ///     Spawns the real ReadyHandshake. Now do exactly what you'd do
    ///     in a real match, say READY out loud and fist-bump (your own
    ///     hands count as a self-bump too), OR press the I'M READY
    ///     button. The same Fire() path the production game uses then
    ///     loads BattleMR + transitions to passthrough, and the test
    ///     scribbles you spawned with X follow you into the MR arena.
    ///
    /// Auto-spawns at app start (RuntimeInitializeOnLoadMethod) and
    /// survives scene loads via DontDestroyOnLoad, always listening.
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
        private bool _xWas;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // Idempotent, only spawn once across scene loads.
            if (FindFirstObjectByType<DebugMRJump>() != null) return;
            var go = new GameObject("DebugMRJump");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugMRJump>();
            Debug.Log("[DebugMRJump] Test triggers active: tap LEFT X = spawn 2 test scribbles in front of you; hold LEFT MENU 1.5s = spawn ReadyHandshake for the bump → MR transition.");
        }

        private void Update()
        {
            if (_jumping) return;

            // LEFT X (primary face button): tap edge → toggle test
            // scribbles. Spawns two visible capsules in VR so the user
            // can confirm they're there BEFORE bumping.
            bool xNow = LeftPrimaryButtonPressed();
            if (xNow && !_xWas) ToggleTestScribbles();
            _xWas = xNow;

            // LEFT MENU: hold 1.5s → spawn ReadyHandshake (which then
            // listens for voice + bump and runs the real Fire() path).
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

        private static bool LeftPrimaryButtonPressed()
        {
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!left.isValid) return false;
            bool pressed = false;
            left.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
            return pressed;
        }

        // Spawn (or re-spawn) two test scribbles in front of the player
        // standing on the floor. Tap LEFT X again to despawn + re-spawn
        // at the new viewpoint. These are children of MonsterSpawnPivotA
        // / MonsterSpawnPivotB so the existing reparenting code in
        // ReadyHandshake.EnterMRWithBumpAnchor will carry them into the
        // MR arena when you bump.
        private static void ToggleTestScribbles()
        {
            // If they already exist, kill them so X = re-spawn at new pos.
            var existingA = GameObject.Find("MonsterSpawnPivotA");
            var existingB = GameObject.Find("MonsterSpawnPivotB");
            bool hadAny =
                (existingA != null && existingA.GetComponentInChildren<DebugDummyMonster>() != null) ||
                (existingB != null && existingB.GetComponentInChildren<DebugDummyMonster>() != null);

            if (hadAny)
            {
                if (existingA != null) Destroy(existingA);
                if (existingB != null) Destroy(existingB);
                Debug.Log("[DebugMRJump] Removed test scribbles (tap LEFT X again to re-spawn at your current view).");
                return;
            }

            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();
            Vector3 head = cam != null ? cam.transform.position : new Vector3(0f, 1.6f, 0f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // Place them ~1.2 m forward of the player, ±0.5 m to the
            // sides, on the floor (Y=0, DummyMonster lifts itself by
            // +0.45 m to its capsule centre).
            Vector3 posA = new Vector3(head.x, 0f, head.z) + fwd * 1.2f - right * 0.5f;
            Vector3 posB = new Vector3(head.x, 0f, head.z) + fwd * 1.2f + right * 0.5f;

            EnsurePivotWithDummyMonster("MonsterSpawnPivotA", posA, new Color(0.95f, 0.45f, 0.35f), facing: head);
            EnsurePivotWithDummyMonster("MonsterSpawnPivotB", posB, new Color(0.40f, 0.65f, 1.00f), facing: head);

            Debug.Log("[DebugMRJump] Spawned 2 test scribbles in VR (red on left, blue on right). Look around, they should be standing on the floor in front of you. Hold LEFT MENU 1.5s to start the bump → MR transition; the scribbles will follow into MR.");
        }

        private void SpawnHandshakeForTest()
        {
            // Place the I'M READY button about 0.55 m in front of the
            // player at chest height, same offset the live game uses
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
                Debug.Log("[DebugMRJump] ReadyHandshake already in scene, leaving it alone.");
                return;
            }

            var hsGo = new GameObject("ReadyHandshake (Debug Test)");
            hsGo.transform.position = Vector3.zero;
            var hs = hsGo.AddComponent<ReadyHandshake>();
            hs.Configure(buttonWorldPos, buttonWorldRot);

            // Reminder if the user forgot to spawn the test scribbles.
            var anyDummy = FindFirstObjectByType<DebugDummyMonster>();
            if (anyDummy == null)
                Debug.Log("[DebugMRJump] No test scribbles in scene, tap LEFT X first if you want to verify the VR → MR carry-over.");
            else
                Debug.Log("[DebugMRJump] Spawned ReadyHandshake. Say READY + fist bump (self-bump your own hands counts), or press I'M READY. The test scribbles will follow into MR.");
        }

        // Spawn (or re-use) a named pivot at the given world position with
        // a child capsule "monster" so the test exercises the same
        // reparenting code path the production hatch flow uses.
        private static void EnsurePivotWithDummyMonster(string pivotName, Vector3 worldPos, Color tint, Vector3 facing)
        {
            var existingPivot = GameObject.Find(pivotName);
            if (existingPivot != null && existingPivot.GetComponentInChildren<DebugDummyMonster>() != null)
            {
                // Already set up, leave the live pivot alone.
                return;
            }

            GameObject pivot = existingPivot;
            if (pivot == null)
            {
                pivot = new GameObject(pivotName);
            }
            // Floor-snap the pivot. Monsters sit on the real floor.
            pivot.transform.position = new Vector3(worldPos.x, 0f, worldPos.z);
            pivot.transform.rotation = Quaternion.identity;

            // Capsule body so it reads as a "creature" silhouette.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = $"DummyMonster ({pivotName})";
            body.transform.SetParent(pivot.transform, worldPositionStays: false);
            body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            body.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
            // Strip the collider so it doesn't block controller poke.
            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // URP/Lit, matte, tinted.
            var sh  = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            var fight = body.AddComponent<DebugDummyMonster>();
            fight.facingTarget = facing;
        }
    }

    /// <summary>
    /// Tiny "fake fight" behaviour for the DebugMRJump dummy monsters:
    /// idle bob, occasional lunge toward the opponent, recoil. Keeps the
    /// MR test scene visually alive so you can confirm passthrough +
    /// camera tracking + reparenting all line up before wiring real
    /// scribbles in.
    /// </summary>
    public class DebugDummyMonster : MonoBehaviour
    {
        public Vector3 facingTarget;
        private float _phase;
        private float _nextLungeAt;
        private float _lungeT = -1f;
        private Vector3 _lungeFrom;
        private Vector3 _lungeTo;
        private const float BobSpeed   = 1.6f;
        private const float BobHeight  = 0.04f;
        private const float LungeRange = 0.18f;

        private void Start()
        {
            _phase       = Random.Range(0f, Mathf.PI * 2f);
            _nextLungeAt = Time.time + Random.Range(1.5f, 3.5f);
            FaceTarget();
        }

        private void Update()
        {
            _phase += Time.deltaTime * BobSpeed;
            // Idle bob.
            var basePos = transform.localPosition;
            basePos.y = 0.45f + Mathf.Sin(_phase) * BobHeight;

            // Pick another monster to lunge toward (the other dummy).
            if (_lungeT < 0f && Time.time >= _nextLungeAt)
            {
                var others = FindObjectsByType<DebugDummyMonster>(FindObjectsSortMode.None);
                Vector3? targetWorld = null;
                foreach (var o in others)
                {
                    if (o == this) continue;
                    targetWorld = o.transform.position;
                    break;
                }
                if (targetWorld.HasValue)
                {
                    Vector3 dir = (targetWorld.Value - transform.position).normalized;
                    dir.y = 0f;
                    _lungeFrom = transform.localPosition;
                    _lungeTo   = _lungeFrom + transform.parent.InverseTransformDirection(dir) * LungeRange;
                    _lungeT    = 0f;
                }
                _nextLungeAt = Time.time + Random.Range(2.5f, 4.5f);
            }

            // Run the lunge as a quick out-and-back over ~0.4 s.
            if (_lungeT >= 0f)
            {
                _lungeT += Time.deltaTime / 0.4f;
                float t = Mathf.Clamp01(_lungeT);
                float k = Mathf.Sin(t * Mathf.PI); // 0 → 1 → 0
                basePos = Vector3.Lerp(_lungeFrom, _lungeTo, k);
                basePos.y = 0.45f + Mathf.Sin(_phase) * BobHeight;
                if (t >= 1f) _lungeT = -1f;
            }

            transform.localPosition = basePos;
            FaceTarget();
        }

        private void FaceTarget()
        {
            // Face the closest other dummy if any, else the configured
            // "facing" hint (usually the player camera position).
            Vector3 lookAt = facingTarget;
            var others = FindObjectsByType<DebugDummyMonster>(FindObjectsSortMode.None);
            float bestSq = float.MaxValue;
            foreach (var o in others)
            {
                if (o == this) continue;
                float d = (o.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; lookAt = o.transform.position; }
            }
            Vector3 dir = lookAt - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}

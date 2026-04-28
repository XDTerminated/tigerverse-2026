#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Tigerverse.Drawing;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Spawns a test sphere/capsule/cube in the active scene with a
    /// procedurally-generated test drawing applied via DrawingColorize.
    /// Lets you iterate on the paper/pencil shader without going through
    /// the full Meshy pipeline.
    /// </summary>
    public static class TigerverseShaderTest
    {
        [MenuItem("Tigerverse/Test -> Spawn Shader Test Sphere (red drawing)")]
        public static void SpawnSphereRed() => Spawn(PrimitiveType.Sphere, MakeBlobDrawing(Color.red), "TestSphere_Red");

        [MenuItem("Tigerverse/Test -> Spawn Shader Test Capsule (blue drawing)")]
        public static void SpawnCapsuleBlue() => Spawn(PrimitiveType.Capsule, MakeBlobDrawing(new Color(0.2f, 0.4f, 1f)), "TestCapsule_Blue");

        [MenuItem("Tigerverse/Test -> Spawn Shader Test Cube (green drawing)")]
        public static void SpawnCubeGreen() => Spawn(PrimitiveType.Cube, MakeBlobDrawing(new Color(0.2f, 0.85f, 0.35f)), "TestCube_Green");

        [MenuItem("Tigerverse/Test -> Spawn Shader Test Sphere (yellow drawing)")]
        public static void SpawnSphereYellow() => Spawn(PrimitiveType.Sphere, MakeBlobDrawing(new Color(1f, 0.92f, 0.2f)), "TestSphere_Yellow");

        // Hardcoded sample GLB URL, known-good Meshy output the user
        // already produced. Used by the dev menu to skip the drawing flow.
        private const string SampleGlbUrl = "https://ueggfh303j.ufs.sh/f/hqoaI3f7pqQl6koHkKXdhzSPI8ykOc45FrwKeWNpJfbAYMB6";

        [MenuItem("Tigerverse/Dev -> Spawn Fake Monster (egg + hatch + paper, no drawing)")]
        public static void SpawnFakeMonster()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[Tigerverse/Dev] Press Play first, the GLB importer (glTFast) only works in play mode.");
                return;
            }

            var fetcher = Object.FindFirstObjectByType<Tigerverse.Meshy.ModelFetcher>();
            if (fetcher == null)
            {
                // Auto-spawn one for dev convenience — the bootstrap usually
                // owns this, but the dev menu shouldn't fail just because
                // we're testing in a scene that doesn't have it wired yet.
                Debug.LogWarning("[Tigerverse/Dev] No ModelFetcher in scene — auto-creating one on a runtime helper GameObject.");
                var fetcherGo = new GameObject("DevModelFetcher");
                Object.DontDestroyOnLoad(fetcherGo);
                fetcher = fetcherGo.AddComponent<Tigerverse.Meshy.ModelFetcher>();
            }

            // Spawn parent, prefer MonsterSpawnPivotA, fall back to in-front-of-camera.
            Transform parent = null;
            var pivotA = GameObject.Find("MonsterSpawnPivotA");
            if (pivotA != null) parent = pivotA.transform;
            else
            {
                var go = new GameObject("DevSpawnPivot");
                Vector3 spawnPos = Vector3.zero + Vector3.forward * 1.5f;
                if (Camera.main != null)
                    spawnPos = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
                go.transform.position = spawnPos;
                parent = go.transform;
            }

            var data = new Tigerverse.Net.PlayerData
            {
                status   = "ready",
                name     = "DevTestMonster",
                glbUrl   = SampleGlbUrl,
                imageUrl = "",
                cryUrl   = "",
                stats    = new Tigerverse.Net.MonsterStatsData { element = "fire" }
            };

            // Inject a procedural drawing into the egg + paper shader so the
            // sticker face looks real even though we're not going through
            // UploadThing.
            fetcher.devOverrideDrawingTex = MakeBlobDrawing(new Color(0.95f, 0.35f, 0.20f));

            Debug.Log("[Tigerverse/Dev] Kicking off full pipeline: drawing → egg → GLB → hatch → paper shader → READY handshake.");
            fetcher.StartCoroutine(fetcher.Fetch(data, parent, (go, err) =>
            {
                if (err != Tigerverse.Meshy.ModelFetcher.FetchError.None)
                {
                    Debug.LogError($"[Tigerverse/Dev] Fetch error: {err}");
                    return;
                }
                Debug.Log($"[Tigerverse/Dev] Fake monster spawned: {(go != null ? go.name : "<null>")}. Now spawning READY handshake...");
                SpawnDevReadyHandshake(parent);
            }));
        }

        // Mirror what GameStateManager.RunInspectionPhase does, but as a
        // standalone dev menu helper so the fake-monster path can exercise
        // the READY button / voice / fist-bump triggers too.
        private static void SpawnDevReadyHandshake(Transform spawnParent)
        {
            var hsGo = new GameObject("DevReadyHandshake");
            var handshake = hsGo.AddComponent<Tigerverse.Combat.ReadyHandshake>();

            Vector3 anchor = spawnParent != null ? spawnParent.position : Vector3.zero;
            Vector3 btnPos = anchor + new Vector3(0.55f, 1.10f, 0f);
            Quaternion btnRot = Quaternion.identity;
            if (Camera.main != null)
            {
                Vector3 toCam = Camera.main.transform.position - btnPos;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-4f)
                    btnRot = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
            handshake.Configure(btnPos, btnRot);
            handshake.OnLocalReady += () =>
            {
                Debug.Log("[Tigerverse/Dev] READY handshake fired, playing VS cutscene.");
                Object.Destroy(hsGo);

                // Find the freshly-spawned monster on the pivot to use as
                // the "left" side. The right side reuses the same one in
                // dev mode (no opponent in solo testing).
                var monster = spawnParent != null
                    ? spawnParent.GetComponentInChildren<Tigerverse.Combat.MonsterCry>(true)?.gameObject
                    : null;

                // Snap the rig into Pokemon battle stance the moment the
                // dev presses Ready, exactly like the production flow does
                // in GameStateManager.RunInspectionPhase.
                Tigerverse.Combat.BattleStance.PositionBehindMonster(monster, monster);

                var vsGo = new GameObject("DevVsCutscene");
                var vs = vsGo.AddComponent<Tigerverse.UI.VsCutscene>();
                vs.StartCoroutine(vs.Play(
                    monster, "Player 1",
                    monster, "Player 2",
                    () =>
                    {
                        Debug.Log("[Tigerverse/Dev] VS cutscene complete, locking locomotion for dev battle.");
                        LockLocomotionForDev();
                    }));
            };
        }

        // Mirrors GameStateManager.SetupBattleLocomotionAndHud's locomotion
        // lock so the dev-menu path lands the trainer in the same parked
        // state the real battle does. No HUD spawned here, there's no
        // BattleManager wired up in the dev path, so the moves panel would
        // be empty anyway.
        private static void LockLocomotionForDev()
        {
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            var xrMove = origin.gameObject.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>(true);
            if (xrMove != null) xrMove.enabled = false;
            var editorMove = origin.GetComponent<Tigerverse.Core.FlatMoveController>();
            if (editorMove != null) editorMove.enabled = false;
        }

        [MenuItem("Tigerverse/Dev -> AUDIO DIAG: RESET audio engine (re-pick Windows default device)")]
        public static void ResetAudioEngine()
        {
            var cfg = AudioSettings.GetConfiguration();
            Debug.Log($"[AudioDiag] BEFORE reset: sampleRate={cfg.sampleRate} speakerMode={cfg.speakerMode} dspBufferSize={cfg.dspBufferSize}");
            // Re-applying the same config forces Unity to release the current
            // audio device handle and re-grab whatever Windows says is the
            // default RIGHT NOW. Fixes the case where Unity locked onto the
            // Oculus Virtual Audio Device at editor-startup but you've since
            // switched Windows default to your laptop speakers.
            bool ok = AudioSettings.Reset(cfg);
            Debug.Log($"[AudioDiag] AudioSettings.Reset returned {ok}.");
            var after = AudioSettings.GetConfiguration();
            Debug.Log($"[AudioDiag] AFTER reset:  sampleRate={after.sampleRate} speakerMode={after.speakerMode} dspBufferSize={after.dspBufferSize}");
            Debug.Log("[AudioDiag] Now run AUDIO DIAG: Beep test. Should play through whatever Windows currently reports as default output.");
        }

        [MenuItem("Tigerverse/Dev -> AUDIO DIAG: Dump audio-system state")]
        public static void DumpAudioState()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AudioDiag] Not in Play mode, AudioListener state may differ. Press Play and re-run for accurate state.");
            }

            Debug.Log($"[AudioDiag] AudioListener.volume = {AudioListener.volume}    (1.0 = full, 0 = silent)");
            Debug.Log($"[AudioDiag] AudioListener.pause  = {AudioListener.pause}     (true = ALL audio paused)");
            Debug.Log($"[AudioDiag] AudioSettings.driverCapabilities = {AudioSettings.driverCapabilities}");
            Debug.Log($"[AudioDiag] AudioSettings.outputSampleRate   = {AudioSettings.outputSampleRate}");
            Debug.Log($"[AudioDiag] AudioSettings.speakerMode        = {AudioSettings.speakerMode}");

            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[AudioDiag] Found {listeners.Length} AudioListener(s):");
            foreach (var l in listeners)
            {
                if (l == null) continue;
                Debug.Log($"  - '{l.gameObject.name}' enabled={l.enabled} go.activeInHierarchy={l.gameObject.activeInHierarchy} pos={l.transform.position}");
            }

            var sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int playingCount = 0;
            foreach (var s in sources) if (s != null && s.isPlaying) playingCount++;
            Debug.Log($"[AudioDiag] Found {sources.Length} AudioSource(s); {playingCount} are isPlaying.");

            // Force-correct any global mute state and re-test.
            if (AudioListener.volume < 0.99f)
            {
                Debug.LogWarning($"[AudioDiag] AudioListener.volume was {AudioListener.volume}, forcing to 1.0");
                AudioListener.volume = 1f;
            }
            if (AudioListener.pause)
            {
                Debug.LogWarning("[AudioDiag] AudioListener.pause was TRUE, forcing to false");
                AudioListener.pause = false;
            }
        }

        [MenuItem("Tigerverse/Dev -> AUDIO DIAG: Make scene Announcer say test phrase")]
        public static void TestAnnouncerAudio()
        {
            if (!Application.isPlaying) { Debug.LogError("[Tigerverse/Dev] Press Play first."); return; }
            var ann = Object.FindFirstObjectByType<Tigerverse.Voice.Announcer>();
            if (ann == null)
            {
                Debug.LogWarning("[Tigerverse/Dev] No Announcer in scene. Adding one to a temporary GameObject.");
                var go = new GameObject("DevAnnouncer");
                go.AddComponent<UnityEngine.AudioSource>();
                ann = go.AddComponent<Tigerverse.Voice.Announcer>();
            }
            Debug.Log("[Tigerverse/Dev] Asking Announcer to speak. If you don't hear this, the problem is Windows/Unity audio routing, not the Professor.");
            ann.Say("Testing one two three. If you can hear this, the audio system works fine.");
        }

        [MenuItem("Tigerverse/Dev -> AUDIO DIAG: Beep test (built-in clip)")]
        public static void BeepTest()
        {
            if (!Application.isPlaying) { Debug.LogError("[Tigerverse/Dev] Press Play first."); return; }
            // Synth a 0.5s 440Hz square wave so we test the audio pipeline
            // with NO network involvement, pure Unity → speakers.
            int sr = 44100;
            int samples = sr / 2;
            var clip = AudioClip.Create("BeepTest", samples, 1, sr, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = (Mathf.Sin(i * 2 * Mathf.PI * 440 / sr) > 0 ? 0.3f : -0.3f);
            clip.SetData(data, 0);

            var go = new GameObject("BeepTester");
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.PlayOneShot(clip);
            Object.Destroy(go, 1f);
            Debug.Log("[Tigerverse/Dev] Played 0.5s beep. If you didn't hear it, system audio is the problem (Windows output device or Unity Game-view mute).");
        }

        [MenuItem("Tigerverse/Dev -> XR DIAG: Dump rig structure")]
        public static void DumpXrRig()
        {
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) { Debug.LogError("[XRDiag] No XR Origin in scene."); return; }
            Debug.Log($"[XRDiag] XR Origin pos={origin.transform.position} cam={(origin.Camera != null ? origin.Camera.name : "<null>")}");

            var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(UnityEngine.XR.XRNode.LeftHand,  devices);
            Debug.Log($"[XRDiag] LeftHand devices: {devices.Count}");
            foreach (var d in devices) Debug.Log($"  - {d.name} valid={d.isValid} chars={d.characteristics}");
            devices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(UnityEngine.XR.XRNode.RightHand, devices);
            Debug.Log($"[XRDiag] RightHand devices: {devices.Count}");
            foreach (var d in devices) Debug.Log($"  - {d.name} valid={d.isValid} chars={d.characteristics}");

            // Walk every transform under XR Origin and log names so we can
            // see what the controllers are actually called in this scene.
            int count = 0;
            foreach (var t in origin.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                count++;
                Debug.Log($"  [{count}] {Path(t)}  active={t.gameObject.activeInHierarchy} pos={t.position}");
            }
            Debug.Log($"[XRDiag] Total transforms under XR Origin: {count}. If you don't see 'Right Controller' or similar, my code's name lookup will fail.");
        }
        private static string Path(Transform t)
        {
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        [MenuItem("Tigerverse/Dev -> List Microphone Devices")]
        public static void ListMicDevices()
        {
            var devices = UnityEngine.Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[Tigerverse/Dev] No microphone devices detected.");
                return;
            }
            Debug.Log("[Tigerverse/Dev] Microphone devices:\n - " + string.Join("\n - ", devices));
        }

        [MenuItem("Tigerverse/Dev -> Use Laptop Mic (clear preferred-mic substring)")]
        public static void UseLaptopMic()
        {
            var router = Object.FindFirstObjectByType<Tigerverse.Voice.VoiceCommandRouter>();
            if (router == null) { Debug.LogWarning("[Tigerverse/Dev] No VoiceCommandRouter in scene."); return; }
            router.SetPreferredMicSubstring("");
            Debug.Log("[Tigerverse/Dev] preferredMicSubstring cleared, using system default mic (typically laptop).");
        }

        [MenuItem("Tigerverse/Dev -> Use Quest/Oculus Mic (preferredMicSubstring = 'Oculus')")]
        public static void UseQuestMic()
        {
            var router = Object.FindFirstObjectByType<Tigerverse.Voice.VoiceCommandRouter>();
            if (router == null) { Debug.LogWarning("[Tigerverse/Dev] No VoiceCommandRouter in scene."); return; }
            router.SetPreferredMicSubstring("Oculus");
        }

        [MenuItem("Tigerverse/Dev -> Spawn Tutorial Start Button (in front of camera)")]
        public static void SpawnStartButton()
        {
            Vector3 spawnPos = Vector3.up * 1.2f + Vector3.forward * 1.0f;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
                spawnPos = sv.camera.transform.position + sv.camera.transform.forward * 1.2f;

            var pivot = new GameObject("DevStartButtonPivot");
            pivot.transform.position = spawnPos;
            pivot.transform.rotation = Quaternion.identity;
            var btn = pivot.AddComponent<Tigerverse.UI.TutorialStartButton>();
            btn.OnPressed += () => Debug.Log("[Tigerverse/Dev] Start button pressed!");
            Selection.activeObject = pivot;
            Debug.Log("[Tigerverse/Dev] Start button spawned. Click it in the Game view (Play mode) or poke with VR controller.");
        }

        [MenuItem("Tigerverse/Dev -> Spawn Professor Tutorial (no egg, just Professor)")]
        public static void SpawnTutorial()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[Tigerverse/Dev] Press Play first, TTS + voice routing only work in play mode.");
                return;
            }

            Vector3 spawnPos = Vector3.up * 1.0f + Vector3.forward * 1.5f;
            if (Camera.main != null)
                spawnPos = Camera.main.transform.position + Camera.main.transform.forward * 1.6f;

            var pivot = new GameObject("DevTutorialPivot");
            pivot.transform.position = spawnPos;
            pivot.AddComponent<Tigerverse.UI.ProfessorTutorial>();
            Selection.activeObject = pivot;
            Debug.Log("[Tigerverse/Dev] Tutorial pivot spawned. Listen for Professor Pastel, he'll talk through the script then enter Q&A. Press right grip (or Spacebar) to ask a question.");
        }

        [MenuItem("Tigerverse/Dev -> Spawn Tutorial + Egg (full sim)")]
        public static void SpawnTutorialWithEgg()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[Tigerverse/Dev] Press Play first.");
                return;
            }

            Vector3 spawnPos = Vector3.up * 0.0f + Vector3.forward * 1.5f;
            if (Camera.main != null)
                spawnPos = Camera.main.transform.position + Camera.main.transform.forward * 1.6f;

            var pivot = new GameObject("DevTutorialPivot");
            pivot.transform.position = spawnPos;

            // Spawn egg with name + progress + pop-in.
            var eggHost = new GameObject("HatchingEgg");
            eggHost.transform.SetParent(pivot.transform, false);
            eggHost.transform.localPosition = Vector3.up * 0.55f;
            var egg = eggHost.AddComponent<Tigerverse.UI.HatchingEggSequence>();
            egg.Configure(MakeBlobDrawing(new Color(0.3f, 0.7f, 1.0f)));
            egg.SetName("Devving Dev");
            egg.SetDisplayProgress(0f);
            egg.progress01 = 0.05f;
            egg.StartCoroutine(egg.PlayPopInAnimation());

            // Auto-ramp progress over 30s.
            EditorApplication.update += new EggProgressRamp(egg, 30f).Tick;

            // Spawn the tutorial.
            pivot.AddComponent<Tigerverse.UI.ProfessorTutorial>();
            Selection.activeObject = pivot;
            Debug.Log("[Tigerverse/Dev] Egg + Professor spawned. Cracks build over 30s. Speak to the Professor (right grip / Space) to ask questions.");
        }

        [MenuItem("Tigerverse/Dev -> Clear Spawned Monsters + Eggs")]
        public static void ClearDevSpawns()
        {
            int n = 0;
            foreach (var c in Object.FindObjectsByType<Tigerverse.Combat.MonsterCry>(FindObjectsSortMode.None))
            { if (c != null) { Object.DestroyImmediate(c.gameObject); n++; } }
            foreach (var e in Object.FindObjectsByType<Tigerverse.UI.HatchingEggSequence>(FindObjectsSortMode.None))
            { if (e != null) { Object.DestroyImmediate(e.gameObject); n++; } }
            foreach (var t in Object.FindObjectsByType<Tigerverse.UI.ProfessorTutorial>(FindObjectsSortMode.None))
            { if (t != null) { Object.DestroyImmediate(t.gameObject); n++; } }
            foreach (var p in Object.FindObjectsByType<Tigerverse.UI.PaperProfessor>(FindObjectsSortMode.None))
            { if (p != null) { Object.DestroyImmediate(p.gameObject); n++; } }
            foreach (var b in Object.FindObjectsByType<Tigerverse.UI.TutorialStartButton>(FindObjectsSortMode.None))
            { if (b != null) { Object.DestroyImmediate(b.gameObject); n++; } }
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            { if (go != null && (go.name == "DevSpawnPivot" || go.name == "TestEggPivot" || go.name == "DevTutorialPivot" || go.name == "DevStartButtonPivot")) { Object.DestroyImmediate(go); n++; } }
            Debug.Log($"[Tigerverse/Dev] Cleared {n} spawned object(s).");
        }

        [MenuItem("Tigerverse/Test -> Spawn Test Hatching Egg (in front of camera)")]
        public static void SpawnTestEgg()
        {
            Vector3 spawnPos = Vector3.up * 1.2f + Vector3.forward * 1.5f;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
                spawnPos = sv.camera.transform.position + sv.camera.transform.forward * 1.5f;

            var pivot = new GameObject("TestEggPivot");
            pivot.transform.position = spawnPos;

            var eggHost = new GameObject("HatchingEgg");
            eggHost.transform.SetParent(pivot.transform, false);
            eggHost.transform.localPosition = Vector3.zero;

            var egg = eggHost.AddComponent<Tigerverse.UI.HatchingEggSequence>();
            // Attach the same blob drawing the shader test sphere uses so the
            // sticker face is populated.
            var drawing = MakeBlobDrawing(new Color(0.2f, 0.6f, 1.0f));
            egg.Configure(drawing);
            egg.SetName("Devving Dev");
            egg.SetDisplayProgress(0f);

            // Pop-in only fires reliably during play, schedule it via the
            // editor coroutine pump if we're in edit mode.
            if (Application.isPlaying)
                egg.StartCoroutine(egg.PlayPopInAnimation());

            // Auto-ramp the cracks AND the progress bar over ~6s so judges
            // see the bar fill alongside cracks growing.
            EditorApplication.update += new EggProgressRamp(egg, 6f).Tick;

            Selection.activeObject = pivot;
            Debug.Log($"[Tigerverse] Test egg spawned at {spawnPos} ('Devving Dev'). Watch the progress bar fill over 6s while cracks grow, then run 'Tigerverse → Test → Hatch Test Egg' to see the burst.");
        }

        [MenuItem("Tigerverse/Test -> Hatch Test Egg")]
        public static void HatchTestEgg()
        {
            var egg = Object.FindFirstObjectByType<Tigerverse.UI.HatchingEggSequence>();
            if (egg == null)
            {
                Debug.LogWarning("[Tigerverse] No egg in scene. Run 'Spawn Test Hatching Egg' first.");
                return;
            }
            // Spawn a placeholder monster (a paper-shader sphere) and hatch.
            var monster = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            monster.name = "TestHatchedMonster";
            monster.transform.position = egg.transform.position + Vector3.down * 0.3f;
            monster.transform.localScale = Vector3.one * 0.4f;
            Object.DestroyImmediate(monster.GetComponent<Collider>());
            Tigerverse.Drawing.DrawingColorize.Apply(monster, MakeBlobDrawing(new Color(0.2f, 0.6f, 1.0f)), 0f);

            var runner = new GameObject("EggHatchRunner");
            var r = runner.AddComponent<EggHatchRunner>();
            r.Begin(egg, monster);
        }

        // Lightweight helper that pumps egg.progress01 + display progress in edit-mode.
        private class EggProgressRamp
        {
            private readonly Tigerverse.UI.HatchingEggSequence _egg;
            private readonly float _duration;
            private readonly double _startTime;
            public EggProgressRamp(Tigerverse.UI.HatchingEggSequence egg, float duration)
            {
                _egg = egg; _duration = duration; _startTime = EditorApplication.timeSinceStartup;
            }
            public void Tick()
            {
                if (_egg == null) { EditorApplication.update -= Tick; return; }
                float t = (float)(EditorApplication.timeSinceStartup - _startTime);
                float k = Mathf.Clamp01(t / _duration);
                _egg.progress01 = Mathf.Min(0.85f, k * 0.9f);
                _egg.SetDisplayProgress(k);
                if (t >= _duration) EditorApplication.update -= Tick;
            }
        }

        // Hatch runner needs a real coroutine, only works in play mode.
        private class EggHatchRunner : MonoBehaviour
        {
            public void Begin(Tigerverse.UI.HatchingEggSequence egg, GameObject monster)
            {
                if (!Application.isPlaying)
                {
                    Debug.LogWarning("[Tigerverse] Hatch coroutine requires Play mode. Press Play, then click 'Hatch Test Egg' again.");
                    Object.DestroyImmediate(gameObject);
                    return;
                }
                StartCoroutine(egg.BeginHatchSequence(monster, egg.transform.position + Vector3.down * 0.3f, () => Destroy(gameObject)));
            }
        }

        [MenuItem("Tigerverse/Test -> Reapply Paper Shader to Scene")]
        public static void ReapplyToScene()
        {
            int count = ApplyToCandidates(GatherSceneCandidates());
            Debug.Log($"[Tigerverse] Reapplied paper shader to {count} root(s) in the scene.");
        }

        [MenuItem("Tigerverse/Test -> Apply Paper Shader to Selection")]
        public static void ApplyToSelection()
        {
            var sel = Selection.gameObjects;
            if (sel == null || sel.Length == 0)
            {
                Debug.LogWarning("[Tigerverse] No objects selected. Select monster roots in the Hierarchy and try again.");
                return;
            }
            int count = ApplyToCandidates(sel);
            Debug.Log($"[Tigerverse] Reapplied paper shader to {count} selected object(s).");
        }

        // Finds every monster GameObject in the scene by looking for things
        // tagged with MonsterCry or ProceduralPunchAttacker, both are
        // uniquely added when a fetched GLB monster is spawned. This catches
        // monsters wherever they live in the hierarchy (e.g. nested under
        // MonsterSpawnPivot/TabletAnchor/...). Also includes obvious
        // shader-test roots so the test sphere still gets re-skinned.
        private static GameObject[] GatherSceneCandidates()
        {
            var set = new System.Collections.Generic.HashSet<GameObject>();

            foreach (var c in Object.FindObjectsByType<Tigerverse.Combat.MonsterCry>(FindObjectsSortMode.None))
                if (c != null) set.Add(c.gameObject);
            foreach (var c in Object.FindObjectsByType<Tigerverse.Meshy.ProceduralPunchAttacker>(FindObjectsSortMode.None))
                if (c != null) set.Add(c.gameObject);

            // Plus the editor test spawns by name prefix.
            string[] testPrefixes = { "PaperTest_", "TestSphere", "TestCapsule", "TestCube", "FetchedGLB" };
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go == null) continue;
                foreach (var p in testPrefixes)
                {
                    if (go.name.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { set.Add(go); break; }
                }
            }

            var arr = new GameObject[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        private static int ApplyToCandidates(GameObject[] roots)
        {
            // Reusable neutral-grey fallback in case a renderer's old material has no _DrawingTex.
            Texture2D fb = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var fpx = new Color32[64 * 64];
            for (int i = 0; i < fpx.Length; i++) fpx[i] = new Color32(200, 200, 200, 255);
            fb.SetPixels32(fpx); fb.Apply();

            int count = 0;
            foreach (var go in roots)
            {
                if (go == null) continue;
                Texture2D drawing = fb;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.HasProperty("_DrawingTex"))
                    {
                        var t = m.GetTexture("_DrawingTex") as Texture2D;
                        if (t != null) { drawing = t; break; }
                    }
                }
                // Pass 0 → stylized (paper+ink) path. >0.5 routes to legacy triplanar.
                DrawingColorize.Apply(go, drawing, drawingStrength: 0f);
                count++;
            }
            return count;
        }

        [MenuItem("Tigerverse/Test -> Clear All Test Spawns")]
        public static void ClearAll()
        {
            int n = 0;
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go != null && go.name.StartsWith("Test"))
                {
                    Object.DestroyImmediate(go);
                    n++;
                }
            }
            Debug.Log($"[Tigerverse] Cleared {n} test spawn(s).");
        }

        private static void Spawn(PrimitiveType prim, Texture2D drawing, string name)
        {
            // Place ~1.2m in front of the scene-view camera so it's immediately visible.
            Vector3 spawnPos = Vector3.zero + Vector3.up * 1.2f + Vector3.forward * 1.5f;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                var cam = sceneView.camera;
                spawnPos = cam.transform.position + cam.transform.forward * 1.5f;
            }

            var go = GameObject.CreatePrimitive(prim);
            go.name = name;
            go.transform.position = spawnPos;
            go.transform.localScale = Vector3.one * 0.6f;
            // Drop the collider, not needed for visual testing.
            Object.DestroyImmediate(go.GetComponent<Collider>());

            DrawingColorize.Apply(go, drawing, drawingStrength: 0.22f);

            Selection.activeObject = go;
            Debug.Log($"[Tigerverse] Spawned '{name}' at {spawnPos} with paper-craft material. Pick another color from the Tigerverse → Test menu, or play with the material's properties live.");
        }

        // Cheap procedural "drawing", a soft-edged blob of the chosen ink color
        // on a white background. Looks plausible to the dominant-color sampler.
        private static Texture2D MakeBlobDrawing(Color ink)
        {
            const int SIZE = 256;
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[SIZE * SIZE];

            float cx = SIZE * 0.5f, cy = SIZE * 0.5f;
            float blobR = SIZE * 0.32f;

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Soft falloff disc + a few "details" via sine ripples.
                    float blob = Mathf.Clamp01(1f - d / blobR);
                    float ripple = 0.5f + 0.5f * Mathf.Sin(x * 0.07f) * Mathf.Cos(y * 0.06f);
                    float alpha = Mathf.Clamp01(blob * blob + 0.08f * ripple * blob);
                    Color c = Color.Lerp(Color.white, ink, alpha);
                    px[y * SIZE + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
#endif

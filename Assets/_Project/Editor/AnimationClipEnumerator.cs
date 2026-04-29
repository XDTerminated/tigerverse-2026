#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Editor utility that dumps every AnimationClip baked into the
    /// character FBX files to the Unity console. Used so we can see what
    /// takes (Walk, Point, Idle, etc.) the FBX actually contains and wire
    /// them into the AnimatorControllers without having to crack the FBX
    /// open by hand.
    /// </summary>
    public static class AnimationClipEnumerator
    {
        [MenuItem("Tigerverse/Animation/Dump Character Clips")]
        public static void DumpCharacterClips()
        {
            string[] paths = new[]
            {
                "Assets/_Project/Models/Characters/Adventurer.fbx",
                "Assets/_Project/Models/Characters/Casual.fbx",
                "Assets/_Project/Models/Characters/Male_Casual.fbx",
                "Assets/_Project/Models/Characters/characterBase.fbx"
            };
            foreach (string p in paths)
            {
                Object[] subs = AssetDatabase.LoadAllAssetsAtPath(p);
                int clipCount = 0;
                foreach (var s in subs) if (s is AnimationClip) clipCount++;
                Debug.Log($"[ClipDump] {p} → {clipCount} clip(s)");
                foreach (var s in subs)
                {
                    if (s is AnimationClip c)
                    {
                        long fileId = 0;
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string guid, out fileId))
                            Debug.Log($"[ClipDump]   '{c.name}' len={c.length:F2}s fileID={fileId} guid={guid}");
                        else
                            Debug.Log($"[ClipDump]   '{c.name}' len={c.length:F2}s (no fileId)");
                    }
                }
            }
        }

        /// <summary>
        /// One-shot: adds a Point trigger + state to Adventurer.controller and
        /// adds Point trigger + Walk bool + Walk state to Casual / MaleCasual.
        /// Pulls the specific FBX sub-clips by name (Idle_Gun_Pointing, Walk,
        /// Man_Punch, Man_Walk) so we don't depend on Unity's "first clip at
        /// path" default. Idempotent — skips anything already wired.
        /// </summary>
        [MenuItem("Tigerverse/Animation/Wire Point + Walk States")]
        public static void WirePointAndWalk()
        {
            // Adventurer (Professor) — point only.
            var advCtrl = LoadCtrl("Assets/_Project/Resources/Characters/Adventurer.controller");
            var advClips = LoadClipsByName("Assets/_Project/Models/Characters/Adventurer.fbx");
            if (advCtrl != null)
            {
                EnsureTrigger(advCtrl, "Point");
                EnsureStateFromAny(advCtrl, "Point", advClips, "CharacterArmature|Idle_Gun_Pointing", "Point");
                EditorUtility.SetDirty(advCtrl);
            }

            // Casual (Player) — point + walk.
            var casCtrl = LoadCtrl("Assets/_Project/Resources/Characters/Casual.controller");
            var casClips = LoadClipsByName("Assets/_Project/Models/Characters/Casual.fbx");
            if (casCtrl != null)
            {
                EnsureTrigger(casCtrl, "Point");
                EnsureBool(casCtrl, "Walk");
                EnsureStateFromAny(casCtrl, "Point", casClips, "CharacterArmature|Idle_Gun_Pointing", "Point");
                EnsureWalkLoop(casCtrl, casClips, "CharacterArmature|Walk");
                EditorUtility.SetDirty(casCtrl);
            }

            // MaleCasual — uses different clip names. Use Punch as the Point
            // analogue (closest gesture in the HumanArmature take set).
            var maleCtrl = LoadCtrl("Assets/_Project/Resources/Characters/MaleCasual.controller");
            var maleClips = LoadClipsByName("Assets/_Project/Models/Characters/Male_Casual.fbx");
            if (maleCtrl != null)
            {
                EnsureTrigger(maleCtrl, "Point");
                EnsureBool(maleCtrl, "Walk");
                EnsureStateFromAny(maleCtrl, "Point", maleClips, "HumanArmature|Man_Punch", "Point");
                EnsureWalkLoop(maleCtrl, maleClips, "HumanArmature|Man_Walk");
                EditorUtility.SetDirty(maleCtrl);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WireAnim] Done. Verify states via Tigerverse → Animation → Dump Character Clips.");
        }

        /// <summary>
        /// Swaps the Adventurer (Professor) controller's Talk state motion to
        /// use the Man_Clapping clip from Male_Casual.fbx instead of the
        /// generic Wave clip from Adventurer.fbx itself. Mecanim retargets
        /// humanoid → humanoid so the HumanArmature clip plays cleanly on
        /// the CharacterArmature rig.
        /// </summary>
        /// <summary>
        /// Extract Male_Casual's Man_Clapping clip, rebind every curve path
        /// from "HumanArmature/..." → "CharacterArmature/..." (and the Man_*
        /// bone prefix to no prefix, matching the KayKit Adventurer rig),
        /// and save as a standalone .anim. The rebound clip plays directly
        /// on the Adventurer rig without going through cross-rig humanoid
        /// retargeting, which has been silently failing because the auto-
        /// generated humanoid avatars on these FBX files are incomplete.
        /// </summary>
        [MenuItem("Tigerverse/Animation/Bake Adventurer Clap From Man_Clapping")]
        public static void BakeAdventurerClap()
        {
            const string srcFbx = "Assets/_Project/Models/Characters/Male_Casual.fbx";
            const string outPath = "Assets/_Project/Resources/Characters/AdventurerClap.anim";

            var clips = LoadClipsByName(srcFbx);
            if (!clips.TryGetValue("HumanArmature|Man_Clapping", out var src))
            {
                Debug.LogError("[BakeClap] HumanArmature|Man_Clapping not found in Male_Casual.fbx");
                return;
            }

            var rebound = new AnimationClip();
            rebound.name = "AdventurerClap";
            rebound.frameRate = src.frameRate;

            int floatCount = 0, objCount = 0, droppedCount = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(src))
            {
                var newBinding = binding;
                newBinding.path = RebindPath(binding.path);
                if (newBinding.path == null) { droppedCount++; continue; }
                var curve = AnimationUtility.GetEditorCurve(src, binding);
                AnimationUtility.SetEditorCurve(rebound, newBinding, curve);
                floatCount++;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(src))
            {
                var newBinding = binding;
                newBinding.path = RebindPath(binding.path);
                if (newBinding.path == null) { droppedCount++; continue; }
                var curve = AnimationUtility.GetObjectReferenceCurve(src, binding);
                AnimationUtility.SetObjectReferenceCurve(rebound, newBinding, curve);
                objCount++;
            }

            // Make sure the clip is set to Generic so Unity doesn't try to
            // route it through humanoid retargeting at playback (which is
            // exactly what we're trying to avoid).
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(rebound, settings);

            AssetDatabase.CreateAsset(rebound, outPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BakeClap] Wrote '{outPath}'. floatCurves={floatCount} objCurves={objCount} dropped={droppedCount}");

            // Wire it into Adventurer.controller's Cheer state.
            var advCtrl = LoadCtrl("Assets/_Project/Resources/Characters/Adventurer.controller");
            if (advCtrl == null) return;
            var cheer = FindState(advCtrl.layers[0].stateMachine, "Cheer");
            if (cheer == null)
            {
                Debug.LogError("[BakeClap] Cheer state not found in Adventurer.controller — run 'Wire Professor Wave + Clap' first.");
                return;
            }
            cheer.motion = rebound;
            EditorUtility.SetDirty(advCtrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BakeClap] Adventurer Cheer state now references the rebound clip directly (no retargeting).");
        }

        // Rewrite a curve path from the male rig to the Adventurer rig.
        // Common patterns observed in KayKit / Quaternius FBX exports:
        //   "HumanArmature"            → "CharacterArmature"
        //   "HumanArmature/Man_Hips"   → "CharacterArmature/Hips"
        //   "HumanArmature/Man_Spine"  → "CharacterArmature/Spine"
        // The rebind matches those patterns conservatively; bones whose
        // path doesn't translate cleanly are returned null so the curve
        // gets dropped (logged in the BakeClap output).
        private static string RebindPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            // Replace the armature root.
            string newPath = path.Replace("HumanArmature", "CharacterArmature");
            // Strip the "Man_" bone prefix so "CharacterArmature/Man_Hips"
            // becomes "CharacterArmature/Hips".
            newPath = newPath.Replace("/Man_", "/");
            return newPath;
        }

        /// <summary>
        /// Dumps the unique curve paths of a clip so we can see what bones
        /// it actually targets. Used to compare Wave (works on Adventurer)
        /// vs Man_Clapping (doesn't) and figure out the right path rebind.
        /// </summary>
        [MenuItem("Tigerverse/Animation/Dump Clip Paths (Wave + Man_Clapping)")]
        public static void DumpClipPaths()
        {
            DumpClipPath("Assets/_Project/Models/Characters/Adventurer.fbx",   "CharacterArmature|Wave");
            DumpClipPath("Assets/_Project/Models/Characters/Male_Casual.fbx", "HumanArmature|Man_Clapping");
        }

        private static void DumpClipPath(string fbxPath, string clipName)
        {
            var clips = LoadClipsByName(fbxPath);
            if (!clips.TryGetValue(clipName, out var clip))
            {
                Debug.LogError($"[ClipPaths] Clip '{clipName}' not found at {fbxPath}");
                return;
            }
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip)) seen.Add(b.path);
            Debug.Log($"[ClipPaths] {clipName} has {seen.Count} unique bone paths:");
            foreach (var p in seen) Debug.Log($"[ClipPaths]   {p}");
        }

        [MenuItem("Tigerverse/Animation/Dump Adventurer Bones")]
        public static void DumpAdventurerBones()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Characters/Adventurer.prefab");
            if (prefab == null) { Debug.LogError("[Bones] Adventurer.prefab missing"); return; }
            DumpRecursive(prefab.transform, 0);
        }

        private static void DumpRecursive(Transform t, int depth)
        {
            Debug.Log($"[Bones] {new string(' ', depth * 2)}{t.name}");
            for (int i = 0; i < t.childCount; i++) DumpRecursive(t.GetChild(i), depth + 1);
        }

        [MenuItem("Tigerverse/Animation/Wire Professor Wave + Clap")]
        public static void WireProfessorWaveAndClap()
        {
            var advCtrl = LoadCtrl("Assets/_Project/Resources/Characters/Adventurer.controller");
            if (advCtrl == null) return;

            var advClips = LoadClipsByName("Assets/_Project/Models/Characters/Adventurer.fbx");

            // Talk state → Wave. Spawn / leave animations fire the Speak
            // trigger which routes through Talk.
            if (advClips.TryGetValue("CharacterArmature|Wave", out var wave))
            {
                var talk = FindState(advCtrl.layers[0].stateMachine, "Talk");
                if (talk != null) talk.motion = wave;
                Debug.Log($"[WireAnim] Adventurer Talk → '{wave.name}'.");
            }

            // Cheer state → Interact. Two-handed gesture from the SAME rig
            // (no humanoid retargeting required). The cross-rig
            // Man_Clapping route silently failed because the auto-generated
            // humanoid avatars on the KayKit FBX files are incomplete.
            EnsureTrigger(advCtrl, "Cheer");
            EnsureStateFromAny(advCtrl, "Cheer", advClips, "CharacterArmature|Interact", "Cheer");

            // Walk loop for the wander behaviour. Same Casual treatment.
            EnsureBool(advCtrl, "Walk");
            EnsureWalkLoop(advCtrl, advClips, "CharacterArmature|Walk");
            // Force Cheer to use the Interact clip even if a Cheer state
            // already exists from a prior run.
            if (advClips.TryGetValue("CharacterArmature|Interact", out var interact))
            {
                var cheer = FindState(advCtrl.layers[0].stateMachine, "Cheer");
                if (cheer != null) cheer.motion = interact;
                Debug.Log($"[WireAnim] Adventurer Cheer → '{interact.name}' (same-rig, plays directly).");
            }

            EditorUtility.SetDirty(advCtrl);
            AssetDatabase.SaveAssets();
            Debug.Log("[WireAnim] Adventurer controller wired: Talk=Wave, Cheer=Interact.");
        }

        private static AnimatorController LoadCtrl(string path)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (c == null) Debug.LogWarning($"[WireAnim] Controller missing: {path}");
            return c;
        }

        private static System.Collections.Generic.Dictionary<string, AnimationClip> LoadClipsByName(string fbxPath)
        {
            var dict = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (var s in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (s is AnimationClip c && !c.name.StartsWith("__preview__"))
                    dict[c.name] = c;
            }
            return dict;
        }

        private static void EnsureTrigger(AnimatorController c, string name)
        {
            foreach (var p in c.parameters) if (p.name == name) return;
            c.AddParameter(name, AnimatorControllerParameterType.Trigger);
            Debug.Log($"[WireAnim] {c.name}: added Trigger '{name}'");
        }

        private static void EnsureBool(AnimatorController c, string name)
        {
            foreach (var p in c.parameters) if (p.name == name) return;
            c.AddParameter(name, AnimatorControllerParameterType.Bool);
            Debug.Log($"[WireAnim] {c.name}: added Bool '{name}'");
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var cs in sm.states) if (cs.state.name == name) return cs.state;
            return null;
        }

        private static void EnsureStateFromAny(AnimatorController c,
            string stateName, System.Collections.Generic.Dictionary<string, AnimationClip> clips,
            string clipName, string trigger)
        {
            var sm = c.layers[0].stateMachine;
            var existing = FindState(sm, stateName);
            if (existing == null)
            {
                if (!clips.TryGetValue(clipName, out var clip))
                {
                    Debug.LogWarning($"[WireAnim] {c.name}: clip '{clipName}' not found, skipping {stateName}");
                    return;
                }
                existing = sm.AddState(stateName);
                existing.motion = clip;
                Debug.Log($"[WireAnim] {c.name}: added state '{stateName}' → '{clipName}'");
            }
            // Any State → Point on trigger, idempotent.
            bool hasAnyTrans = false;
            foreach (var t in sm.anyStateTransitions)
                if (t.destinationState == existing) { hasAnyTrans = true; break; }
            if (!hasAnyTrans)
            {
                var tr = sm.AddAnyStateTransition(existing);
                tr.hasExitTime = false;
                tr.duration = 0.10f;
                tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                tr.canTransitionToSelf = false;
            }
            // Point → Idle when finished.
            var idle = FindState(sm, "Idle");
            if (idle != null)
            {
                bool hasReturn = false;
                foreach (var tr in existing.transitions) if (tr.destinationState == idle) { hasReturn = true; break; }
                if (!hasReturn)
                {
                    var tr = existing.AddTransition(idle);
                    tr.hasExitTime = true;
                    tr.exitTime = 0.95f;
                    tr.duration = 0.20f;
                }
            }
        }

        private static void EnsureWalkLoop(AnimatorController c,
            System.Collections.Generic.Dictionary<string, AnimationClip> clips, string clipName)
        {
            var sm = c.layers[0].stateMachine;
            var walk = FindState(sm, "Walk");
            if (walk == null)
            {
                if (!clips.TryGetValue(clipName, out var clip))
                {
                    Debug.LogWarning($"[WireAnim] {c.name}: clip '{clipName}' not found, skipping Walk");
                    return;
                }
                walk = sm.AddState("Walk");
                walk.motion = clip;
                Debug.Log($"[WireAnim] {c.name}: added state 'Walk' → '{clipName}'");
            }
            var idle = FindState(sm, "Idle");
            if (idle == null) return;

            bool idleToWalk = false;
            foreach (var tr in idle.transitions) if (tr.destinationState == walk) { idleToWalk = true; break; }
            if (!idleToWalk)
            {
                var tr = idle.AddTransition(walk);
                tr.hasExitTime = false;
                tr.duration = 0.12f;
                tr.AddCondition(AnimatorConditionMode.If, 0f, "Walk");
            }
            bool walkToIdle = false;
            foreach (var tr in walk.transitions) if (tr.destinationState == idle) { walkToIdle = true; break; }
            if (!walkToIdle)
            {
                var tr = walk.AddTransition(idle);
                tr.hasExitTime = false;
                tr.duration = 0.12f;
                tr.AddCondition(AnimatorConditionMode.IfNot, 0f, "Walk");
            }
        }
    }
}
#endif

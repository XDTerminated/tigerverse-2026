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

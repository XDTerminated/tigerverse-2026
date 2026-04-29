#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// One-shot menu utility to rebuild the character prefabs in
    /// Resources/Characters cleanly from their FBXs. Earlier runtime
    /// tooling (head-shot baker, animator tests, agent scripts) saved
    /// bone-rotation overrides into the prefabs that prevented the
    /// Animator's clips from driving the rig at runtime — Idle would be
    /// reported as "playing" but bones stayed frozen at the captured
    /// override poses. This rebuild instantiates each FBX fresh, adds an
    /// Animator on the root with the matching controller + avatar, sets
    /// SkinnedMeshRenderer bounds wide enough to avoid culling, and saves
    /// the prefab WITHOUT touching any bone transforms.
    /// </summary>
    public static class CharacterPrefabRebuilder
    {
        [MenuItem("Tigerverse/Rebuild Character Prefabs")]
        public static void Rebuild()
        {
            // 1. Make sure each FBX has Generic rig + CreateFromThisModel
            //    avatar, then re-import so the avatar exists.
            string[] fbxs = {
                "Assets/_Project/Models/Characters/Adventurer.fbx",
                "Assets/_Project/Models/Characters/Casual.fbx",
                "Assets/_Project/Models/Characters/Male_Casual.fbx",
                "Assets/_Project/Models/Characters/Casual_Hoodie.fbx",
            };
            foreach (var fbx in fbxs)
            {
                var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
                if (imp == null) continue;
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.optimizeGameObjects = false;
                imp.SaveAndReimport();
            }
            AssetDatabase.Refresh();

            // 2. Patch each controller so the Idle state uses the proper
            //    breathing idle clip (KayKit's "Idle"), NOT "Idle_Neutral"
            //    which is a static reference pose with zero motion.
            FixIdleClip("Assets/_Project/Models/Characters/Adventurer.fbx",
                        "Assets/_Project/Resources/Characters/Adventurer.controller",
                        new[]{"|Idle$", "|Idle"});
            FixIdleClip("Assets/_Project/Models/Characters/Casual.fbx",
                        "Assets/_Project/Resources/Characters/Casual.controller",
                        new[]{"|Idle$", "|Idle"});
            FixIdleClip("Assets/_Project/Models/Characters/Male_Casual.fbx",
                        "Assets/_Project/Resources/Characters/MaleCasual.controller",
                        new[]{"Man_Idle"});
            FixIdleClip("Assets/_Project/Models/Characters/Casual_Hoodie.fbx",
                        "Assets/_Project/Resources/Characters/Hoodie.controller",
                        new[]{"|Idle$", "|Idle"});

            // 3. Rebuild each prefab.
            Build("Assets/_Project/Models/Characters/Adventurer.fbx",
                  "Assets/_Project/Resources/Characters/Adventurer.prefab",
                  "Assets/_Project/Resources/Characters/Adventurer.controller");
            Build("Assets/_Project/Models/Characters/Casual.fbx",
                  "Assets/_Project/Resources/Characters/Casual.prefab",
                  "Assets/_Project/Resources/Characters/Casual.controller");
            Build("Assets/_Project/Models/Characters/Male_Casual.fbx",
                  "Assets/_Project/Resources/Characters/MaleCasual.prefab",
                  "Assets/_Project/Resources/Characters/MaleCasual.controller");
            Build("Assets/_Project/Models/Characters/Casual_Hoodie.fbx",
                  "Assets/_Project/Resources/Characters/Hoodie.prefab",
                  "Assets/_Project/Resources/Characters/Hoodie.controller");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CharacterPrefabRebuilder] Done. Prefabs rebuilt + Idle state patched to use breathing 'Idle' clip.");
        }

        // Replaces the "Idle" state's motion in the given controller with
        // the FBX clip whose name matches the first pattern in `prefer`
        // (regex). Falls back through subsequent patterns.
        private static void FixIdleClip(string fbxPath, string controllerPath, string[] prefer)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (ac == null) { Debug.LogWarning("[CharacterPrefabRebuilder] No controller at " + controllerPath); return; }

            AnimationClip pick = null;
            var all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var pat in prefer)
            {
                foreach (var o in all)
                {
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(c.name, pat))
                        {
                            pick = c;
                            break;
                        }
                    }
                }
                if (pick != null) break;
            }
            if (pick == null) { Debug.LogWarning("[CharacterPrefabRebuilder] No matching idle clip in " + fbxPath); return; }

            var sm = ac.layers[0].stateMachine;
            foreach (var s in sm.states)
            {
                if (s.state.name == "Idle")
                {
                    s.state.motion = pick;
                    EditorUtility.SetDirty(ac);
                    Debug.Log($"[CharacterPrefabRebuilder] {System.IO.Path.GetFileName(controllerPath)}: Idle motion = {pick.name}");
                    return;
                }
            }
        }

        private static void Build(string fbxPath, string prefabPath, string controllerPath)
        {
            var fbxGo = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxGo == null) { Debug.LogError("[CharacterPrefabRebuilder] FBX not found: " + fbxPath); return; }

            // Find the FBX's avatar.
            UnityEngine.Avatar avatar = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is UnityEngine.Avatar av) { avatar = av; break; }
            if (avatar == null)
            {
                Debug.LogError("[CharacterPrefabRebuilder] No avatar in " + fbxPath + ". Re-import with avatarSetup=CreateFromThisModel.");
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) Debug.LogWarning("[CharacterPrefabRebuilder] Controller not found: " + controllerPath);

            // Instantiate the FBX cleanly. Do NOT touch any child transforms.
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxGo);

            // Add / configure Animator on the root.
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Widen SkinnedMeshRenderer bounds + always-animate so the
            // mesh stays visible at runtime even when far from origin.
            // Don't disable bone updates.
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.localBounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(2.5f, 2.5f, 2.5f));
            }

            // Save WITHOUT modifying any bone transforms.
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
            Debug.Log($"[CharacterPrefabRebuilder] Rebuilt {System.IO.Path.GetFileName(prefabPath)} (avatar={avatar.name}, controller={controller?.name ?? "NULL"})");
        }
    }
}
#endif

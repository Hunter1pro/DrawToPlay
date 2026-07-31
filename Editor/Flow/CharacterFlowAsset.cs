using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The built-in Character <see cref="FlowDefinition"/> (draw-tool-port-brief.md §6.1:
    /// Draw → Rig → Skin → Animate → Physics → Behavior). M4 "completes the Character flow
    /// through Animate" (§8), so this asset ships the first FOUR stages — the ones whose tools
    /// exist. Physics (M5) and Behavior (M6/M7) are deliberately absent rather than stubbed:
    /// <see cref="EnsureStages"/> appends missing built-ins in canonical order, so a later
    /// milestone adds them to an already-authored asset without touching anything the user
    /// edited.
    ///
    /// Same lifecycle as <see cref="TerrainFlowAsset"/>: created on demand (menu item), upgraded
    /// in place, and otherwise a normal asset whose checklists are data you can edit in the
    /// Inspector. The two flows share the Flows folder and nothing else.
    /// </summary>
    internal static class CharacterFlowAsset
    {
        /// <summary>Same folder as the Terrain flow — referenced rather than re-typed so there is
        /// one definition of where flows live.</summary>
        internal const string FlowFolder = TerrainFlowAsset.FlowFolder;

        internal const string CharacterFlowPath = FlowFolder + "/Character.asset";

        [MenuItem("Tools/Draw To Play/Create Character Flow")]
        private static void CreateCharacterFlowMenuItem()
        {
            var asset = CreateOrLoad();
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>The Character flow asset if it is already in the project, else null — never
        /// creates files. An asset authored by an earlier milestone gains the built-in stages it
        /// is missing (additive only — see <see cref="EnsureStages"/>).</summary>
        internal static FlowDefinition LoadIfPresent()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FlowDefinition>(CharacterFlowPath);
            if (asset != null && EnsureStages(asset))
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            return asset;
        }

        /// <summary>Load the Character flow, creating it (folders included) the first time.
        /// Returns null only when the folder could not be created.</summary>
        internal static FlowDefinition CreateOrLoad()
        {
            var existing = LoadIfPresent();
            if (existing != null)
                return existing;

            if (EnsureFolder(FlowFolder) != FlowFolder)
                return null;

            var asset = ScriptableObject.CreateInstance<FlowDefinition>();
            asset.flowName = "Character";
            asset.stages = BuildStages();

            AssetDatabase.CreateAsset(asset, CharacterFlowPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // --- content ----------------------------------------------------------------------

        /// <summary>§6.1 stages 1-4. Checklists are trimmed to what M0-M4 actually ship; where a
        /// §6.1 requirement has no validator behind it yet (every part bound, required clip
        /// names) the line says so rather than implying the badge checks it.</summary>
        private static List<FlowStage> BuildStages()
        {
            return new List<FlowStage>
            {
                new FlowStage
                {
                    id = FlowValidators.CharacterDrawStageId,
                    title = "Draw",
                    // typeof(...) instead of a literal so a rename cannot silently orphan the
                    // stage; the field stays a plain string because flows are data.
                    toolTypeName = typeof(DrawShapeTool).FullName,
                    description =
                        "Draw the body parts. One GameObject with a DrawnShapeRenderer per part " +
                        "(torso, head, upper arm, forearm...), all parented under a single entity " +
                        "root — the rig, the skin binding and the animator all address things by " +
                        "NAME under that root.",
                    checklist = new List<string>
                    {
                        "Drag with LMB to draw a part. Force New (Draw overlay) keeps every stroke " +
                        "its own part instead of merging it into the selected one.",
                        "Parent every part under one entity root, and give each a unique name — " +
                        "two parts called \"Arm\" are indistinguishable to bones and pose paths.",
                        "Style each part on its DrawnShapeAsset (fill/outline/rim, textures): the " +
                        "skinned mesh reuses those same materials, so styling now saves a pass later.",
                        "The Transform tool moves / rotates / scales a finished part without redrawing it.",
                        "Paint works on characters too, but a painted part is only worth it once its " +
                        "silhouette is final.",
                        "Done when at least one part has geometry."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.CharacterRigStageId,
                    title = "Rig",
                    toolTypeName = typeof(RigShapeTool).FullName,
                    description =
                        "Click a bone chain along each part. The chain is committed into a sibling " +
                        "\"Skeleton\" GameObject (ShapeRig + RigAsset) and the part is bound to it " +
                        "in the same undo step. The asset holds REST poses; the bone Transforms " +
                        "are the live pose.",
                    checklist = new List<string>
                    {
                        "Select a part, activate the Rig tool, click the joints along it, then press " +
                        "Enter (or double-click) to commit.",
                        "Bones are named {ShapeName}Bone{N}. That name is the binding — renaming a " +
                        "bone in the Hierarchy unbinds it.",
                        "Re-rigging a part REPLACES its own chain and leaves every other part's bones alone.",
                        "Bind To Sibling Rig attaches a part to an existing skeleton without drawing " +
                        "a new chain (shared bones between parts is the point).",
                        "Setup Mode makes moved bones write back into the RigAsset rests — that is how " +
                        "a joint gets fixed after the fact. Turn it OFF before posing, or your pose " +
                        "becomes the rest.",
                        "Reset Pose puts every bone back on its rest.",
                        "§6.1 wants every part either bound or explicitly left static; the badge only " +
                        "checks that a skeleton with bones exists.",
                        "Done when a Skeleton's RigAsset has at least one bone."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.CharacterSkinStageId,
                    title = "Skin",
                    // No tool of its own: skinning is added by the Rig tool's bind step, and tuned
                    // in the Inspector with the debug overlay on. The tab must not steal the
                    // user's current tool to show a checklist.
                    toolTypeName = string.Empty,
                    description =
                        "Tune how the drawing follows the bones. Each bound part carries a " +
                        "DrawnShapeSkin whose mesh is the outline ring plus an interior lattice, " +
                        "weighted against the bones' REST segments — so a redraw regenerates the " +
                        "skin and keeps the binding.",
                    checklist = new List<string>
                    {
                        "The skin arrives with the Rig tool's bind step; there is nothing to add by hand.",
                        "skinDetail (DrawnShapeAsset) is the interior lattice spacing — smaller bends " +
                        "smoother and costs vertices.",
                        "skinSoftness widens the weight falloff: raise it when a joint creases, lower " +
                        "it when a whole part follows one bone.",
                        "includeBones / excludeBones pick which bones this part listens to. includeBones " +
                        "wins outright when it is non-empty.",
                        "Turn on the Skin Debug overlay: vertices are coloured by dominant bone, so " +
                        "orphan or wrongly-owned areas show up instantly (§6.1 \"no orphan weights\").",
                        "Holes are ignored by the skin path — a part that needs a hole has to stay unskinned.",
                        "The generated skin layer is not saved with the scene. It comes back on the next " +
                        "bind, on \"Regenerate Skins In Scene\", and on entering play mode; until then " +
                        "the flat MeshRenderer draws the part and this badge reads In progress.",
                        "Done when at least one part reports isSkinned."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.CharacterAnimateStageId,
                    title = "Animate",
                    // The Pose Sheet is an EditorWindow, not an EditorTool, so it cannot be named
                    // here (FlowWindow only activates EditorTools) — the menu path is in the
                    // checklist instead.
                    toolTypeName = string.Empty,
                    description =
                        "Pose the rig and key it. Clips are PoseClipAssets: columns of channel " +
                        "values at times, blended linearly (rotations shortest-path, in degrees) — " +
                        "not Unity AnimationClips. PoseAnimator plays them at runtime and the Pose " +
                        "Sheet scrubs them in the editor.",
                    checklist = new List<string>
                    {
                        "Open the Pose Sheet: Tools ▸ Draw To Play ▸ Pose Sheet. It is a window, not a " +
                        "scene tool, so this tab activates nothing.",
                        "Add a PoseAnimator to the ENTITY ROOT and set its root to that transform: pose " +
                        "paths resolve by descendant name, so the root has to see both the parts and " +
                        "the bones.",
                        "Create a Pose Clip (Assets ▸ Create ▸ Draw To Play ▸ Pose Clip), add it to the " +
                        "animator's clips, and set current to the clip's name.",
                        "Key a column: pose the bones, then Key at the playhead. Auto-key does it for " +
                        "you — it watches the rig while the preview is NOT playing.",
                        "Times are seconds and rotations are degrees; length + looping make the clip a cycle.",
                        "Capture Form appends the part's current outline to its morphTargets. A " +
                        "\"PartName:morph\" channel then blends between forms — squash, blink, a mouth " +
                        "opening — which no bone can do.",
                        "playing = true on the animator means the scene animates the moment you enter " +
                        "play mode; Play(\"name\") is the gameplay entry point.",
                        "captureFilter on a clip restricts what a column records — that is how " +
                        "partial-body layer clips (aim, shot) are authored for LayerPlay/LayerScrub.",
                        "§6.1 wants the game's required clip names (idle / run / hit / death); nothing " +
                        "enforces that list yet.",
                        "Done when an animator holds a clip with at least two pose columns.",
                        "Tools ▸ Draw To Play ▸ Verify M4 Pose Animation builds a worked example of all " +
                        "of the above in one scene."
                    }
                }
            };
        }

        /// <summary>Add any built-in stage the asset does not have yet, in canonical order, and
        /// report whether anything changed. Purely additive — an existing stage object is never
        /// replaced, so edited checklists (and stages the user added) survive. New stages land
        /// immediately after the nearest earlier canonical stage that IS present, which is what
        /// will put Physics and Behavior after Animate when M5+ appends them.</summary>
        private static bool EnsureStages(FlowDefinition asset)
        {
            if (asset == null)
                return false;

            if (asset.stages == null)
                asset.stages = new List<FlowStage>();

            var canonical = BuildStages();
            var changed = false;

            for (var i = 0; i < canonical.Count; ++i)
            {
                if (IndexOfStage(asset.stages, canonical[i].id) >= 0)
                    continue;

                var insertAt = asset.stages.Count;
                for (var j = i - 1; j >= 0; --j)
                {
                    var anchor = IndexOfStage(asset.stages, canonical[j].id);
                    if (anchor < 0)
                        continue;
                    insertAt = anchor + 1;
                    break;
                }

                asset.stages.Insert(insertAt, canonical[i]);
                changed = true;
            }

            return changed;
        }

        private static int IndexOfStage(List<FlowStage> stages, string id)
        {
            if (stages == null || string.IsNullOrEmpty(id))
                return -1;

            for (var i = 0; i < stages.Count; ++i)
            {
                if (stages[i] != null && string.Equals(stages[i].id, id, System.StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        // --- assets -----------------------------------------------------------------------

        /// <summary>Create every missing folder along an "Assets/a/b" path. Returns the deepest
        /// folder that exists.</summary>
        private static string EnsureFolder(string assetFolderPath)
        {
            var segments = assetFolderPath.Split('/');
            var path = segments[0]; // "Assets"
            for (var i = 1; i < segments.Length; ++i)
            {
                var next = $"{path}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    return path;
                path = next;
            }

            return path;
        }
    }
}

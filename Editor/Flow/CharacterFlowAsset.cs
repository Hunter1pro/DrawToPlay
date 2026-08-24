using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The built-in Character <see cref="FlowDefinition"/> (draw-tool-port-brief.md §6.1:
    /// Draw → Rig → Skin → Animate → Physics → Behavior), plus the §6.2 Enemy/AI tail.
    ///
    /// M7 completes it. M4 shipped the first four stages (§8: "completes the Character flow
    /// through Animate"); Physics has been authorable since M5 and Behavior since M6/M7, so
    /// <see cref="EnsureStages"/> now appends all three remaining tabs — Physics, Behavior, AI —
    /// in canonical order, additively, leaving an asset a user has already edited alone.
    ///
    /// WHY AI LIVES HERE. §6.2 defines the Enemy flow as "(inherits Character stages 1–5) →
    /// Combat → AI" — it is this flow plus a tail, not a second document, and building a second
    /// FlowDefinition would have meant maintaining five duplicated stages to add one. The AI tab
    /// is therefore the seventh stage of this asset and simply stays neutral for a player
    /// entity, which is the same "nothing is modal, badges just tell the truth" contract every
    /// other stage follows. §6.2's Combat stage is not a tab: its outputs (the health attribute, the
    /// weapon/effect/loot defs, hitbox windows) are components and assets with no tool of their
    /// own, and a tab that activates nothing and validates nothing is worse than the Inspector.
    ///
    /// Same lifecycle as <see cref="TerrainFlowAsset"/>: created on demand (menu item), upgraded
    /// in place, and otherwise a normal asset whose checklists are data you can edit in the
    /// Inspector. The two flows share the Flows folder and nothing else.
    /// </summary>
    internal static class CharacterFlowAsset
    {
        /// <summary>Same folder as the Terrain flow — referenced rather than re-typed so there is
        /// one definition of where flows live.</summary>
        internal static string FlowFolder => TerrainFlowAsset.FlowFolder;

        internal static string CharacterFlowPath => FlowFolder + "/Character.asset";

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

        /// <summary>§6.1 stages 1-6 plus the §6.2 AI tail. Checklists are trimmed to what
        /// M0-M7 actually ship; where a §6 requirement has no validator behind it (every part
        /// bound, required clip names, ranges sane) the line says so rather than implying the
        /// badge checks it.</summary>
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
                },
                new FlowStage
                {
                    id = FlowValidators.CharacterPhysicsStageId,
                    title = "Physics",
                    // Ragdoll setup is Inspector work on a component, and hurt/hitbox volumes are
                    // drawn with the Draw tool from stage 1. Neither is a tool this tab should
                    // steal — the checklist points at both instead.
                    toolTypeName = string.Empty,
                    description =
                        "Give the character a body. RagdollDriver generates one capsule per rig " +
                        "bone and a hinge per parent-child pair from the REST pose the moment it " +
                        "is switched on, so the ragdoll follows the rig rather than being " +
                        "authored twice. Props and debris use the same EntityBody chassis.",
                    checklist = new List<string>
                    {
                        "Add a RagdollDriver to the entity root and point rig at the Skeleton's ShapeRig. " +
                        "That one reference is the whole setup — capsules and hinge limits are derived.",
                        "boneRadiusScale / minBoneRadius size the capsules against bone length; " +
                        "hingeLowerDegrees / hingeUpperDegrees are limits RELATIVE to the rest angle.",
                        "Set animator so the driver can pause posing while ragdolling and resume after.",
                        "Ragdoll is play-mode only: the bodies are created on StartRagdoll and destroyed " +
                        "on StopRagdoll, so there is nothing to see (and nothing to break) in edit mode.",
                        "Destructible props: DestructibleShape on the drawn shape, with impactSpeedThreshold " +
                        "for impact breaks (the old HP-death seam retired with HealthComponent).",
                        "§6.1 also wants sensor hit/hurt volumes and collision categories. Draw them with " +
                        "the Draw tool and set the layers on the shape definition; the badge does not " +
                        "check either — every shape has some layer, so there is nothing honest to test.",
                        "Done when a RagdollDriver has a rig assigned.",
                        "Tools ▸ Draw To Play ▸ Verify M5 Ragdoll + Destruction builds a worked example: " +
                        "Space toggles the ragdoll, the crate fragments on impact."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.CharacterBehaviorStageId,
                    title = "Behavior",
                    // The state tree editor is an EditorWindow, not an EditorTool, so FlowWindow
                    // opens it directly for this stage id (see FlowWindow).
                    toolTypeName = string.Empty,
                    description =
                        "Behavior as states (§7). An ability is a STATE, not a tag bundle, so one " +
                        "ability's exit transitions straight into the next through the same " +
                        "machinery the AI uses — combos, recovery, stagger interrupts. Opening " +
                        "this tab opens the entity's state tree in the State Tree Editor — the " +
                        "window edits the EXACT asset the runner runs.",
                    checklist = new List<string>
                    {
                        "Clicking this tab opens the entity's tree in the State Tree Editor; with no " +
                        "tree yet it creates one under Assets/DrawToPlay/Trees and assigns it to the " +
                        "runner in one undo step.",
                        "Add states, pick tasks and conditions from the dropdowns, and wire " +
                        "transitions in the inspector: target state, condition, and the Interrupt " +
                        "toggle (re-tested every tick, cancels running tasks).",
                        "There is NO bake step. Every edit writes into the runtime StateTreeAsset " +
                        "itself — what the window shows is what the next Play runs.",
                        "In play mode the window tints the active state green (previous amber) — the " +
                        "single most useful behavior-debugging feature.",
                        "The Graph Toolkit canvas remains an optional visualization with its own " +
                        "menu; it authors a separate graph asset and needs its bake step.",
                        "§6.1 wants a mover archetype and an aim rig too; neither is ported yet, so a " +
                        "player entity's movement is still game code.",
                        "Done when a StateTreeRunner in the scene has a tree assigned."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.EnemyAIStageId,
                    title = "AI",
                    toolTypeName = string.Empty,
                    description =
                        "§6.2's enemy tail: the SAME state tree editor as Behavior. An enemy tree " +
                        "is state-tree-first — perception conditions drive the transitions and " +
                        "abilities sit inside states — where a player's is flow-tree-first, driven " +
                        "by input intents. Same asset, same editor, same runner.",
                    checklist = new List<string>
                    {
                        "Clicking this tab opens the entity's tree in the State Tree Editor, exactly " +
                        "like Behavior does.",
                        "Zombie / Brute / Archer ship as preset trees: Tools ▸ Draw To Play ▸ Create " +
                        "Enemy Preset Trees. Read one before authoring your own — they are the ported " +
                        "enemies/*.gd archetypes, ranges and timings included.",
                        "Perception is a condition: TargetDetected acquires the nearest living health " +
                        "pool of another team, TargetInRange measures it, LineOfSight raycasts to it.",
                        "Interrupts are what make an enemy feel alive: a transition with " +
                        "checkWhileRunning is re-tested every tick and cancels the running tasks " +
                        "(OnExit with Cancelled), which is how a chase becomes an attack mid-stride.",
                        "Ranges are in world units — Godot pixels ÷ 32. The presets keep the source " +
                        "number visible in the code that builds them.",
                        "§6.2 also wants min < max range checks and a death clip in Animate; neither " +
                        "cross-check exists yet, so the badge stops at \"the entry state does " +
                        "something\".",
                        "Done when the tree's entry state has tasks or transitions.",
                        "Tools ▸ Draw To Play ▸ Verify M7b Direct Editor proves the window renders a " +
                        "preset's states and wiring; edit a range there, press Play, the behavior " +
                        "changes — same asset, no bake."
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

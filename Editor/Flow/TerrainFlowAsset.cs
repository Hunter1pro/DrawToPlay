using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The built-in Terrain <see cref="FlowDefinition"/> (draw-tool-port-brief.md §6.4:
    /// Sculpt → Paint → Decorate → Collision → Gameplay). M1 shipped the two tabs whose tools
    /// existed (Sculpt, Collision); M2 completes the flow by adding Paint and Decorate — whose
    /// tools now exist — plus a toolless Gameplay stub, exactly as predicted: by appending
    /// stages to the asset, never by touching window code.
    ///
    /// The asset is created on demand (menu item or the button in an empty
    /// <see cref="FlowWindow"/>) so merely having the code in the project never writes files.
    /// Once it exists it is a normal asset: edit the checklists in the Inspector, they are
    /// data. A flow written by an earlier milestone is upgraded in place — missing built-in
    /// stages are inserted in canonical order and nothing already in the asset is rewritten,
    /// so edited checklists survive.
    /// </summary>
    internal static class TerrainFlowAsset
    {
        internal static string FlowFolder => DrawToPlayFolders.Flows;
        internal const string TerrainFlowPath = FlowFolder + "/Terrain.asset";

        [MenuItem("Tools/Draw To Play/Create Terrain Flow")]
        private static void CreateTerrainFlowMenuItem()
        {
            var asset = CreateOrLoad();
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>The Terrain flow asset if it is already in the project, else null. Used by
        /// the window to auto-load without ever creating files behind the user's back. An asset
        /// authored by an earlier milestone gains the built-in stages it is missing (additive
        /// only — see <see cref="EnsureStages"/>).</summary>
        internal static FlowDefinition LoadIfPresent()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FlowDefinition>(TerrainFlowPath);
            if (asset != null && EnsureStages(asset))
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            return asset;
        }

        /// <summary>Load the Terrain flow, creating it (folders included) the first time.
        /// Returns null only when the folder could not be created.</summary>
        internal static FlowDefinition CreateOrLoad()
        {
            var existing = LoadIfPresent();
            if (existing != null)
                return existing;

            if (EnsureFolder(FlowFolder) != FlowFolder)
                return null;

            var asset = ScriptableObject.CreateInstance<FlowDefinition>();
            asset.flowName = "Terrain";
            asset.stages = BuildStages();

            AssetDatabase.CreateAsset(asset, TerrainFlowPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // --- content ----------------------------------------------------------------------

        /// <summary>The full §6.4 stage order: Sculpt → Paint → Decorate → Collision → Gameplay.
        /// Every stage but Gameplay has a tool behind it as of M2.</summary>
        private static List<FlowStage> BuildStages()
        {
            return new List<FlowStage>
            {
                new FlowStage
                {
                    id = FlowValidators.TerrainSculptStageId,
                    title = "Sculpt",
                    // typeof(...) instead of a literal so a rename cannot silently orphan the
                    // stage; the field stays a plain string because flows are data.
                    toolTypeName = typeof(DrawShapeTool).FullName,
                    description =
                        "Blob draw / carve / holes with the Draw Shape tool. The level is a set of " +
                        "drawn blobs — one GameObject with a DrawnShapeRenderer each.",
                    checklist = new List<string>
                    {
                        "Drag with LMB to draw; overlapping the selected blob unions into it.",
                        "Ctrl/Cmd-drag carves. In Circle/Rect mode Shift-drag inside a blob punches a hole.",
                        "Force New (Draw overlay) makes every stroke its own blob instead of merging.",
                        "A stroke that misses the selected blob spawns a new sibling that inherits its style.",
                        "Done when at least one blob has geometry."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.TerrainPaintStageId,
                    title = "Paint",
                    toolTypeName = typeof(PaintShapeTool).FullName,
                    description =
                        "Three-slot texture brush with feathered blending. The mask is an RGBA " +
                        "image stored on the blob's asset: R/G/B are the weights of texture " +
                        "slots 1-3, A is paint coverage. Brush-over-the-edge sculpting stays on, " +
                        "so painting past the outline can also reshape it.",
                    checklist = new List<string>
                    {
                        "Add Paint (Draw-to-Play overlay) gives the selected blob a ShapePaint component.",
                        "Assign Paint Texture 2 (and optionally 3) on the blob's DrawnShapeAsset — " +
                        "slot 1 is the existing fill texture/colour.",
                        "Pick a slot (1/2/3) in the Paint section, then drag with LMB to paint it in.",
                        "Shift-drag erases: it lowers coverage only, it never repaints a slot.",
                        "[ and ] shrink / grow the brush (Godot's ±2 px, clamped to 0.015625..4 wu).",
                        "Softness feathers the brush edge; a soft edge is what blends two materials.",
                        "Each stroke is one undo step — the mask and any edge sculpting revert together.",
                        "Done when a blob carries a painted mask and at least one extra paint texture."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.TerrainDecorateStageId,
                    title = "Decorate",
                    toolTypeName = typeof(StampTool).FullName,
                    description =
                        "Scatter stamps over the terrain. Stamps are prefab-aware (§6.4.3): a " +
                        "stamped lantern brings its own light and body, because the stamp IS the " +
                        "prefab. Plain textures work too and become a GameObject with a " +
                        "SpriteRenderer.",
                    checklist = new List<string>
                    {
                        "Point the Stamps overlay at a folder (default Assets/DrawToPlay/Stamps) " +
                        "and press Rescan — textures and prefabs both show up in the grid.",
                        "Click a thumbnail to arm it; clicking it again disarms. Only one stamp is " +
                        "ever armed, and arming switches you to the Stamp tool.",
                        "Drag with LMB to scatter: one copy per Spacing world units along the drag.",
                        "Random Flip mirrors about half the copies; Scale Min/Max sets the uniform " +
                        "random size. Placement jitter is fixed at ± Spacing * 0.2.",
                        "Stamps parent under the SELECTED drawn shape, so moving the blob moves its " +
                        "decoration. With nothing selected they land at the scene root.",
                        "One drag = one undo step, named for the number of stamps it placed.",
                        "Esc disarms the stamp.",
                        "Done when at least one stamp has been scattered into the scene."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.TerrainCollisionStageId,
                    title = "Collision",
                    // No tool of its own: collision is authored in the Inspector plus the scene
                    // overlay, and the tab must not steal the Draw tool from the user.
                    toolTypeName = string.Empty,
                    description =
                        "Per blob: derive PhysicsCore2D collision from the drawing. Chain vs solid " +
                        "decomposition and per-chain surface feel live on the TerrainBlob component; " +
                        "the debug overlay draws the generated geometry over the art so " +
                        "collision-vs-visual drift is visible.",
                    checklist = new List<string>
                    {
                        "Add a TerrainBlob component to every blob that should be solid — select the " +
                        "blobs and use \"Add TerrainBlob\" in the Draw-to-Play scene overlay.",
                        "Chain mode = one-sided walkable surface (outer + hole rings as chain loops). " +
                        "Pick it for ground you stand on.",
                        "Solid mode = filled interior (convex decomposition). Pick it when things must " +
                        "not tunnel through the blob's inside.",
                        "Set per-chain friction and bounciness on the chain definition's surface material " +
                        "(solid mode reads the shape definition instead).",
                        "The collision debug overlay is switched on when you open this tab — the drawn " +
                        "outline is the raw ring, so wobble-styled art will not match it exactly.",
                        "Collision rebuilds live on every sculpt gesture, in edit and play mode.",
                        "Surface categories (ground / wall / one-way / water) are a later milestone.",
                        "Done when every blob with geometry has a TerrainBlob."
                    }
                },
                new FlowStage
                {
                    id = FlowValidators.TerrainGameplayStageId,
                    title = "Gameplay",
                    // No tool and no validator: the stage exists so the flow reads as the
                    // complete §6.4 pipeline instead of stopping at Collision.
                    toolTypeName = string.Empty,
                    description =
                        "Spawn markers, zones and room metadata (§6.4.5). Nothing here is built " +
                        "yet — the tab is a placeholder so the terrain pipeline reads end to end, " +
                        "and it carries no badge because no validator claims the stage.",
                    checklist = new List<string>
                    {
                        "spawn markers / zones — future milestone.",
                        "Planned: player start, exits, room/zone ScriptableObjects (ports of " +
                        "room_def / zone_rules / level_node_def).",
                        "Planned: this is where the procgen graph tooling lands."
                    }
                }
            };
        }

        /// <summary>Add any built-in stage the asset does not have yet, in canonical order, and
        /// report whether anything changed. Purely additive: an existing stage object is never
        /// replaced, so checklists the user edited (and any stage they added themselves) survive.
        /// A new stage is inserted immediately after the nearest earlier canonical stage that IS
        /// present, which is what keeps Paint/Decorate landing between Sculpt and Collision on an
        /// M1-era asset.</summary>
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

        /// <summary>Create every missing folder along an "Assets/a/b" path (same walk as
        /// DrawShapeTool.EnsureDrawnFolder). Returns the deepest folder that exists.</summary>
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

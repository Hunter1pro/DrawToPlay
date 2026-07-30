using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The built-in Terrain <see cref="FlowDefinition"/> (draw-tool-port-brief.md §6.4:
    /// Sculpt → Paint → Decorate → Collision → Gameplay). M1 ships the two tabs whose tools
    /// exist — Sculpt and Collision; Paint/Decorate/Gameplay arrive with M2 and later and are
    /// added by appending stages to the asset, not by touching window code.
    ///
    /// The asset is created on demand (menu item or the button in an empty
    /// <see cref="FlowWindow"/>) so merely having the code in the project never writes files.
    /// Once it exists it is a normal asset: edit the checklists in the Inspector, they are
    /// data.
    /// </summary>
    internal static class TerrainFlowAsset
    {
        internal const string FlowFolder = "Assets/DrawToPlay/Flows";
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
        /// the window to auto-load without ever creating files behind the user's back.</summary>
        internal static FlowDefinition LoadIfPresent()
        {
            return AssetDatabase.LoadAssetAtPath<FlowDefinition>(TerrainFlowPath);
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

        /// <summary>M1 scope: exactly the two stages whose tooling exists.</summary>
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
                }
            };
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

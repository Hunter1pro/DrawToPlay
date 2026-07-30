using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Scene-view panel replacing the Godot plugin's toolbar `HBoxContainer` (_refresh_bar):
    /// which tool is live, the Draw-tool geometry mode (Free / Circle / Rect = `_shape_opt`),
    /// the Force New toggle (`_force_new_btn`) and a shortcut to create an empty drawn shape.
    /// All state lives in <see cref="DrawToolSettings"/> so the tools stay stateless about it.
    ///
    /// M1 adds a Collision section — the toggle for <see cref="CollisionDebugOverlay"/> and a
    /// one-click "give this drawing a body" — which has no Godot counterpart because the Godot
    /// tool had no collision at all (draw-tool-port-brief.md §5).
    /// </summary>
    [Overlay(typeof(SceneView),
        k_OverlayId,
        k_DisplayName,
        defaultDisplay = true,
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel,
        defaultWidth = k_DefaultWidth,
        defaultHeight = k_DefaultHeight)]
    internal sealed class DrawToPlayOverlay : Overlay
    {
        private const string k_OverlayId = "Scene View/Draw To Play";
        private const string k_DisplayName = "Draw To Play";
        private const float k_DefaultWidth = 232f;
        private const float k_DefaultHeight = 232f;

        /// <summary>Foldout open/closed state. The debug toggle itself lives in EditorPrefs
        /// under <see cref="CollisionDebugOverlay.EnabledPrefKey"/>, shared with the Flow
        /// window; this key is purely this panel's cosmetics.</summary>
        private const string k_CollisionFoldoutKey = "PowerOfFire.DrawToPlay.CollisionFoldoutOpen";

        private Label m_StatusLabel;
        private EnumField m_ShapeModeField;
        private Toggle m_ForceNewToggle;
        private Toggle m_DebugCollisionToggle;
        private Button m_AddTerrainBlobButton;

        public override void OnCreated()
        {
            ToolManager.activeToolChanged += RefreshStatus;
            Selection.selectionChanged += RefreshStatus;
        }

        public override void OnWillBeDestroyed()
        {
            ToolManager.activeToolChanged -= RefreshStatus;
            Selection.selectionChanged -= RefreshStatus;
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { width = new StyleLength(k_DefaultWidth) } };

            m_StatusLabel = new Label { style = { paddingBottom = 6f, left = 2f, whiteSpace = WhiteSpace.Normal } };

            m_ShapeModeField = new EnumField("Shape", DrawToolSettings.shapeMode)
            {
                tooltip = "Free = freehand scribble, Circle / Rect = anchor drag. " +
                          "Ctrl/Cmd drag carves; in Circle/Rect, Shift carves too (drag inside a shape = hole)."
            };
            m_ShapeModeField.RegisterValueChangedCallback(changeEvent =>
            {
                DrawToolSettings.shapeMode = (DrawToolSettings.ShapeMode)changeEvent.newValue;
                SceneView.RepaintAll();
            });

            m_ForceNewToggle = new Toggle("Force New")
            {
                value = DrawToolSettings.forceNew,
                tooltip = "Every stroke becomes its own drawn shape instead of sculpting the selected one."
            };
            m_ForceNewToggle.RegisterValueChangedCallback(changeEvent =>
            {
                DrawToolSettings.forceNew = changeEvent.newValue;
                SceneView.RepaintAll();
            });

            var newShapeButton = new Button(() => DrawToPlayMenu.CreateDrawnShape(null))
            {
                text = "New Drawn Shape",
                tooltip = "Create an empty DrawnShapeRenderer and select it, ready to draw into."
            };

            var drawToolButton = new Button(ToolManager.SetActiveTool<DrawShapeTool>)
            {
                text = "Activate Draw Tool"
            };

            root.Add(m_StatusLabel);
            root.Add(m_ShapeModeField);
            root.Add(m_ForceNewToggle);
            root.Add(newShapeButton);
            root.Add(drawToolButton);
            root.Add(BuildCollisionSection());

            RefreshStatus();
            return root;
        }

        /// <summary>The M1 Collision section: see what physics really got, and give a drawing a
        /// body without leaving the scene view.</summary>
        private VisualElement BuildCollisionSection()
        {
            var foldout = new Foldout
            {
                text = "Collision",
                value = EditorPrefs.GetBool(k_CollisionFoldoutKey, true)
            };
            foldout.RegisterValueChangedCallback(changeEvent =>
            {
                // ChangeEvent<bool> from the toggle inside bubbles up to the foldout, so the
                // foldout must only react to its own.
                if (changeEvent.target == foldout)
                    EditorPrefs.SetBool(k_CollisionFoldoutKey, changeEvent.newValue);
            });

            m_DebugCollisionToggle = new Toggle("Debug Overlay")
            {
                value = CollisionDebugOverlay.enabled,
                tooltip = "Draw the geometry TerrainBlob handed to PhysicsCore2D over the art: " +
                          "convex pieces filled per-colour in Solid mode, bold loops with " +
                          "one-sided normal ticks in Chain mode. The gap you see against the " +
                          "outline is the render wobble, which is collision-free by design."
            };
            m_DebugCollisionToggle.RegisterValueChangedCallback(changeEvent =>
            {
                CollisionDebugOverlay.enabled = changeEvent.newValue;
            });

            m_AddTerrainBlobButton = new Button(AddTerrainBlobToSelection)
            {
                text = "Add TerrainBlob",
                tooltip = "Give every selected drawn shape a static body derived from its curve. " +
                          "Shapes that already have one are skipped."
            };

            foldout.Add(m_DebugCollisionToggle);
            foldout.Add(m_AddTerrainBlobButton);
            return foldout;
        }

        /// <summary>One undo step for the whole click, per the project's gesture-undo rule.</summary>
        private void AddTerrainBlobToSelection()
        {
            var selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add TerrainBlob");

            for (int i = 0; i < selection.Length; i++)
            {
                var gameObject = selection[i];
                if (gameObject == null)
                    continue;
                if (gameObject.GetComponent<DrawnShapeRenderer>() == null)
                    continue;
                if (gameObject.GetComponent<TerrainBlob>() != null)
                    continue;

                Undo.AddComponent<TerrainBlob>(gameObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            RefreshStatus();
            SceneView.RepaintAll();
        }

        /// <summary>Selected drawn shapes still missing a TerrainBlob.</summary>
        private static int CountBlobCandidates()
        {
            var selection = Selection.gameObjects;
            if (selection == null)
                return 0;

            int count = 0;
            for (int i = 0; i < selection.Length; i++)
            {
                var gameObject = selection[i];
                if (gameObject == null)
                    continue;
                if (gameObject.GetComponent<DrawnShapeRenderer>() == null)
                    continue;
                if (gameObject.GetComponent<TerrainBlob>() != null)
                    continue;
                count++;
            }
            return count;
        }

        private void RefreshStatus()
        {
            // Each element is guarded on its own: the callbacks registered in OnCreated can
            // fire before CreatePanelContent has built any of them.
            if (m_StatusLabel != null)
            {
                var activeToolType = ToolManager.activeToolType;
                string mode;
                if (activeToolType == typeof(DrawShapeTool))
                    mode = "Draw";
                else if (activeToolType == typeof(TransformShapeTool))
                    mode = "Transform";
                else
                    mode = "None";

                var active = Selection.activeGameObject;
                var renderer = active != null ? active.GetComponent<DrawnShapeRenderer>() : null;
                var shapeName = renderer != null ? renderer.name : "none selected";
                var blobState = renderer == null
                    ? "-"
                    : renderer.GetComponent<TerrainBlob>() != null ? "yes" : "no";

                m_StatusLabel.text = $"Tool: {mode}\nShape: {shapeName}\nBody: {blobState}";
            }

            if (m_AddTerrainBlobButton != null)
                m_AddTerrainBlobButton.SetEnabled(CountBlobCandidates() > 0);

            // The same EditorPrefs key is driven from the Flow window's Collision stage, so
            // re-read it rather than trusting the toggle's last known value.
            if (m_DebugCollisionToggle != null && m_DebugCollisionToggle.value != CollisionDebugOverlay.enabled)
                m_DebugCollisionToggle.SetValueWithoutNotify(CollisionDebugOverlay.enabled);
        }
    }
}

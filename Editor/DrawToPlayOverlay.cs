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
        private const float k_DefaultHeight = 148f;

        private Label m_StatusLabel;
        private EnumField m_ShapeModeField;
        private Toggle m_ForceNewToggle;

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

            RefreshStatus();
            return root;
        }

        private void RefreshStatus()
        {
            if (m_StatusLabel == null)
                return;

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

            m_StatusLabel.text = $"Tool: {mode}\nShape: {shapeName}";
        }
    }
}

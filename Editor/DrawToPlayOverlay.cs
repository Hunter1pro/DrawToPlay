using System.Collections.Generic;
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
    ///
    /// M2 adds a Paint section: the brush texture slot (Godot `_tex_opt`), the brush radius
    /// readout the Godot toolbar showed as `_size_label`, the softness the Godot node only
    /// exposed in the inspector, and the two buttons that get a shape ready to paint.
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
        private const float k_DefaultHeight = 348f;

        /// <summary>Foldout open/closed state. The debug toggle itself lives in EditorPrefs
        /// under <see cref="CollisionDebugOverlay.EnabledPrefKey"/>, shared with the Flow
        /// window; this key is purely this panel's cosmetics.</summary>
        private const string k_CollisionFoldoutKey = "PowerOfFire.DrawToPlay.CollisionFoldoutOpen";

        /// <summary>Paint foldout open/closed state — cosmetics only; the armed slot lives in
        /// <see cref="DrawToolSettings.paintSlot"/> and the brush values on the asset.</summary>
        private const string k_PaintFoldoutKey = "PowerOfFire.DrawToPlay.PaintFoldoutOpen";

        private Label m_StatusLabel;
        private EnumField m_ShapeModeField;
        private Toggle m_ForceNewToggle;
        private Toggle m_DebugCollisionToggle;
        private Button m_AddTerrainBlobButton;
        private DropdownField m_PaintSlotField;
        private Slider m_BrushRadiusSlider;
        private Slider m_BrushSoftnessSlider;
        private Label m_BrushSizeLabel;
        private Button m_AddPaintButton;
        private Button m_PaintToolButton;

        public override void OnCreated()
        {
            ToolManager.activeToolChanged += RefreshStatus;
            Selection.selectionChanged += RefreshStatus;
            DrawToolSettings.paintStateChanged += RefreshStatus;
            Undo.undoRedoPerformed += RefreshStatus;
        }

        public override void OnWillBeDestroyed()
        {
            ToolManager.activeToolChanged -= RefreshStatus;
            Selection.selectionChanged -= RefreshStatus;
            DrawToolSettings.paintStateChanged -= RefreshStatus;
            Undo.undoRedoPerformed -= RefreshStatus;
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
            root.Add(BuildPaintSection());
            root.Add(BuildCollisionSection());

            RefreshStatus();
            return root;
        }

        /// <summary>The M2 Paint section — Godot's brush toolbar (`_tex_opt` + `_size_label`)
        /// plus the two "make this shape paintable" affordances Unity needs because the mask
        /// lives on a separate component.</summary>
        private VisualElement BuildPaintSection()
        {
            var foldout = new Foldout
            {
                text = "Paint",
                value = EditorPrefs.GetBool(k_PaintFoldoutKey, true)
            };
            foldout.RegisterValueChangedCallback(changeEvent =>
            {
                // Child field events bubble up to the foldout, so only react to its own.
                if (changeEvent.target == foldout)
                    EditorPrefs.SetBool(k_PaintFoldoutKey, changeEvent.newValue);
            });

            m_PaintSlotField = new DropdownField("Slot", BuildSlotChoices(SelectedAsset()), DrawToolSettings.paintSlot)
            {
                tooltip = "Brush texture slot (fillTexture / paintTexture2 / paintTexture3) — " +
                          "mask channels R/G/B, soft edges blend the slots together."
            };
            m_PaintSlotField.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.target != m_PaintSlotField)
                    return;
                DrawToolSettings.paintSlot = m_PaintSlotField.index;
                SceneView.RepaintAll();
            });

            m_BrushRadiusSlider = new Slider("Radius", DrawToolSettings.BrushRadiusMin, DrawToolSettings.BrushRadiusMax)
            {
                value = DrawToolSettings.BrushRadiusMin,
                tooltip = "Brush radius in world units, stored on the selected shape's asset. " +
                          "[ and ] resize it by 2 px while the Paint tool is active."
            };
            m_BrushRadiusSlider.RegisterValueChangedCallback(changeEvent => ApplyBrushRadius(changeEvent.newValue));

            m_BrushSizeLabel = new Label { style = { paddingBottom = 2f, left = 2f, opacity = 0.7f } };

            m_BrushSoftnessSlider = new Slider("Softness", 0f, 1f)
            {
                value = 0.5f,
                tooltip = "Feather width as a fraction of the radius (feather = max(radius * softness, 0.5 px))."
            };
            m_BrushSoftnessSlider.RegisterValueChangedCallback(changeEvent => ApplyBrushSoftness(changeEvent.newValue));

            m_AddPaintButton = new Button(AddPaintToSelection)
            {
                text = "Add Paint",
                tooltip = "Give every selected drawn shape a ShapePaint component (the paint mask + layer). " +
                          "The Paint tool adds it on the first stroke too."
            };

            m_PaintToolButton = new Button(ActivatePaintTool)
            {
                text = "Activate Paint Tool",
                tooltip = "Paint the selected shape: drag to paint, Shift to erase, [ / ] to resize."
            };

            foldout.Add(m_PaintSlotField);
            foldout.Add(m_BrushRadiusSlider);
            foldout.Add(m_BrushSizeLabel);
            foldout.Add(m_BrushSoftnessSlider);
            foldout.Add(m_AddPaintButton);
            foldout.Add(m_PaintToolButton);
            return foldout;
        }

        /// <summary>Slot names, decorated with the texture actually assigned to that slot on the
        /// selected asset so "Tex 2" reads as the material it paints.</summary>
        private static List<string> BuildSlotChoices(DrawnShapeAsset asset)
        {
            var choices = new List<string>(DrawToolSettings.PaintSlotCount);
            for (int slot = 0; slot < DrawToolSettings.PaintSlotCount; slot++)
            {
                var texture = SlotTexture(asset, slot);
                choices.Add(texture != null
                    ? $"{DrawToolSettings.PaintSlotLabel(slot)} - {texture.name}"
                    : DrawToolSettings.PaintSlotLabel(slot));
            }

            return choices;
        }

        private static Texture2D SlotTexture(DrawnShapeAsset asset, int slot)
        {
            if (asset == null)
                return null;
            switch (slot)
            {
                case 0: return asset.fillTexture;
                case 1: return asset.paintTexture2;
                case 2: return asset.paintTexture3;
                default: return null;
            }
        }

        private static DrawnShapeRenderer SelectedRenderer()
        {
            var active = Selection.activeGameObject;
            return active != null ? active.GetComponent<DrawnShapeRenderer>() : null;
        }

        private static DrawnShapeAsset SelectedAsset()
        {
            var renderer = SelectedRenderer();
            return renderer != null ? renderer.asset : null;
        }

        private void ApplyBrushRadius(float value)
        {
            var asset = SelectedAsset();
            if (asset == null)
                return;

            var radius = DrawToolSettings.ClampBrushRadius(value);
            if (Mathf.Approximately(radius, asset.brushRadius))
                return;

            Undo.RecordObject(asset, "Brush Radius");
            asset.brushRadius = radius;
            EditorUtility.SetDirty(asset);
            RefreshStatus();
            SceneView.RepaintAll();
        }

        private void ApplyBrushSoftness(float value)
        {
            var asset = SelectedAsset();
            if (asset == null)
                return;

            var softness = Mathf.Clamp01(value);
            if (Mathf.Approximately(softness, asset.brushSoftness))
                return;

            Undo.RecordObject(asset, "Brush Softness");
            asset.brushSoftness = softness;
            EditorUtility.SetDirty(asset);
            SceneView.RepaintAll();
        }

        /// <summary>One undo step for the whole click, per the project's gesture-undo rule.</summary>
        private void AddPaintToSelection()
        {
            var selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Paint");

            for (int i = 0; i < selection.Length; i++)
            {
                var gameObject = selection[i];
                if (gameObject == null)
                    continue;
                if (gameObject.GetComponent<DrawnShapeRenderer>() == null)
                    continue;
                if (gameObject.GetComponent<ShapePaint>() != null)
                    continue;

                Undo.AddComponent<ShapePaint>(gameObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            RefreshStatus();
            SceneView.RepaintAll();
        }

        /// <summary>PaintShapeTool is a component tool, so it only exists while a drawn shape is
        /// selected — the button is disabled otherwise and this guard is the safety net.</summary>
        private static void ActivatePaintTool()
        {
            if (SelectedRenderer() == null)
                return;
            ToolManager.SetActiveTool<PaintShapeTool>();
        }

        /// <summary>Selected drawn shapes still missing a ShapePaint.</summary>
        private static int CountPaintCandidates()
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
                if (gameObject.GetComponent<ShapePaint>() != null)
                    continue;
                count++;
            }
            return count;
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
                else if (activeToolType == typeof(PaintShapeTool))
                    mode = "Paint";
                else
                    mode = "None";

                var active = Selection.activeGameObject;
                var renderer = active != null ? active.GetComponent<DrawnShapeRenderer>() : null;
                var shapeName = renderer != null ? renderer.name : "none selected";
                var blobState = renderer == null
                    ? "-"
                    : renderer.GetComponent<TerrainBlob>() != null ? "yes" : "no";

                var paint = renderer != null ? renderer.GetComponent<ShapePaint>() : null;
                var paintState = renderer == null
                    ? "-"
                    : paint == null ? "no" : paint.hasMask ? "yes" : "empty";

                m_StatusLabel.text = $"Tool: {mode}\nShape: {shapeName}\nBody: {blobState}\nPaint: {paintState}";
            }

            if (m_AddTerrainBlobButton != null)
                m_AddTerrainBlobButton.SetEnabled(CountBlobCandidates() > 0);

            RefreshPaintSection();

            // The same EditorPrefs key is driven from the Flow window's Collision stage, so
            // re-read it rather than trusting the toggle's last known value.
            if (m_DebugCollisionToggle != null && m_DebugCollisionToggle.value != CollisionDebugOverlay.enabled)
                m_DebugCollisionToggle.SetValueWithoutNotify(CollisionDebugOverlay.enabled);
        }

        /// <summary>Mirror the brush state back into the panel. The values live on the selected
        /// asset (the tool's bracket keys and undo write them behind the panel's back), so every
        /// control is re-read rather than trusting its last known value.</summary>
        private void RefreshPaintSection()
        {
            var renderer = SelectedRenderer();
            var asset = renderer != null ? renderer.asset : null;

            if (m_PaintSlotField != null)
            {
                var choices = BuildSlotChoices(asset);
                m_PaintSlotField.choices = choices;
                m_PaintSlotField.SetValueWithoutNotify(choices[DrawToolSettings.paintSlot]);
            }

            if (m_BrushRadiusSlider != null)
            {
                m_BrushRadiusSlider.SetEnabled(asset != null);
                if (asset != null)
                    m_BrushRadiusSlider.SetValueWithoutNotify(DrawToolSettings.ClampBrushRadius(asset.brushRadius));
            }

            if (m_BrushSoftnessSlider != null)
            {
                m_BrushSoftnessSlider.SetEnabled(asset != null);
                if (asset != null)
                    m_BrushSoftnessSlider.SetValueWithoutNotify(Mathf.Clamp01(asset.brushSoftness));
            }

            if (m_BrushSizeLabel != null)
            {
                // Godot `_update_label(): "brush %d px"`.
                m_BrushSizeLabel.text = asset != null
                    ? $"brush {Mathf.RoundToInt(DrawToolSettings.ToGodotPixels(asset.brushRadius))} px"
                    : "brush - (no shape asset)";
            }

            if (m_AddPaintButton != null)
                m_AddPaintButton.SetEnabled(CountPaintCandidates() > 0);

            if (m_PaintToolButton != null)
                m_PaintToolButton.SetEnabled(renderer != null);
        }
    }
}

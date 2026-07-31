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
    ///
    /// M3 adds a Rig section — the Godot plugin's Rig button, Setup toggle and Bones menu
    /// (_rig_menu_action lines 520-528): activate the Rig tool, keep bone rests following the
    /// bones while building (Setup mode), snap back to bind pose, bind a shape to the sibling
    /// rig (_bind_selected lines 403-411), and drop a bone's influence on the selected shape.
    ///
    /// M4 adds an Anim section — the rest of that Bones menu (_capture_form lines 564-575) plus
    /// the way into the Pose Sheet: open the sheet, snapshot the current outline as a form
    /// variant, and see at a glance whether AUTO-KEY is actually recording (it only is while the
    /// sheet is open, because the sheet owns the poll and the bound animator).
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
        private const float k_DefaultHeight = 560f;

        /// <summary>Foldout open/closed state. The debug toggle itself lives in EditorPrefs
        /// under <see cref="CollisionDebugOverlay.EnabledPrefKey"/>, shared with the Flow
        /// window; this key is purely this panel's cosmetics.</summary>
        private const string k_CollisionFoldoutKey = "PowerOfFire.DrawToPlay.CollisionFoldoutOpen";

        /// <summary>Paint foldout open/closed state — cosmetics only; the armed slot lives in
        /// <see cref="DrawToolSettings.paintSlot"/> and the brush values on the asset.</summary>
        private const string k_PaintFoldoutKey = "PowerOfFire.DrawToPlay.PaintFoldoutOpen";

        /// <summary>Rig foldout open/closed state — cosmetics only; Setup mode itself lives in
        /// <see cref="RigShapeTool.SetupModePrefKey"/>.</summary>
        private const string k_RigFoldoutKey = "PowerOfFire.DrawToPlay.RigFoldoutOpen";

        /// <summary>Anim foldout open/closed state — cosmetics only; the AUTO-KEY arm itself lives
        /// in <see cref="PoseSheetState.AutoKeyPrefKey"/>, shared with the Pose Sheet.</summary>
        private const string k_AnimFoldoutKey = "PowerOfFire.DrawToPlay.AnimFoldoutOpen";

        /// <summary>Placeholder entry of the exclude-bone dropdown: the field is an action list,
        /// not a value, so it snaps back to this after every pick.</summary>
        private const string k_ExcludePlaceholder = "Exclude a bone...";

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
        private Button m_RigToolButton;
        private Toggle m_SetupModeToggle;
        private Button m_ResetPoseButton;
        private Button m_BindRigButton;
        private DropdownField m_ExcludeBoneField;
        private Label m_RigHintLabel;
        private Button m_CaptureFormButton;
        private Label m_AutoKeyLabel;
        private Label m_AnimHintLabel;

        /// <summary>Bone names behind the exclude dropdown's entries, minus the placeholder — the
        /// labels are decorated, so the pick is resolved by index.</summary>
        private readonly List<string> m_ExcludeBoneNames = new List<string>();

        public override void OnCreated()
        {
            ToolManager.activeToolChanged += RefreshStatus;
            Selection.selectionChanged += RefreshStatus;
            DrawToolSettings.paintStateChanged += RefreshStatus;
            PoseSheetState.changed += RefreshStatus;
            Undo.undoRedoPerformed += RefreshStatus;
        }

        public override void OnWillBeDestroyed()
        {
            ToolManager.activeToolChanged -= RefreshStatus;
            Selection.selectionChanged -= RefreshStatus;
            DrawToolSettings.paintStateChanged -= RefreshStatus;
            PoseSheetState.changed -= RefreshStatus;
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
            root.Add(BuildRigSection());
            root.Add(BuildAnimSection());
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

        // --- Rig (M3) ---------------------------------------------------------------------

        /// <summary>The M3 Rig section — the Godot plugin's Rig button + Setup toggle + Bones
        /// menu, minus the entries that belong to later milestones (Capture Form / Freeze).
        /// "Set rests from pose" (_reset_rests) has no button of its own: Setup mode applies the
        /// very same rule continuously, and Reset Pose is its inverse.</summary>
        private VisualElement BuildRigSection()
        {
            var foldout = new Foldout
            {
                text = "Rig",
                value = EditorPrefs.GetBool(k_RigFoldoutKey, true)
            };
            foldout.RegisterValueChangedCallback(changeEvent =>
            {
                // Child field events bubble up to the foldout, so only react to its own.
                if (changeEvent.target == foldout)
                    EditorPrefs.SetBool(k_RigFoldoutKey, changeEvent.newValue);
            });

            m_RigToolButton = new Button(ActivateRigTool)
            {
                text = "Activate Rig Tool",
                tooltip = "Click a chain of joints along the selected shape; Enter or double-click " +
                          "builds the bone chain on a sibling Skeleton object and binds the shape to it. " +
                          "Re-rigging replaces that shape's previous chain."
            };

            m_SetupModeToggle = new Toggle("Setup Mode")
            {
                value = RigShapeTool.setupMode,
                tooltip = "While on, moving a bone re-stamps its REST in the rig asset and rebuilds " +
                          "the bound skins — the pose you build IS the bind pose. Turn it off before " +
                          "posing/animating, or the pose gets baked into the rest."
            };
            m_SetupModeToggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.target != m_SetupModeToggle)
                    return;
                RigShapeTool.setupMode = changeEvent.newValue;
                RefreshStatus();
            });

            m_ResetPoseButton = new Button(ResetSelectedPose)
            {
                text = "Reset Pose",
                tooltip = "Put every bone of the selected shape's rig back on its rest pose (bind pose)."
            };

            m_BindRigButton = new Button(BindSelectedToSiblingRig)
            {
                text = "Bind To Sibling Rig",
                tooltip = "Bind the selected shape to the rig sitting next to it, without touching its " +
                          "bind list — an empty include list means every bone of that rig deforms it."
            };

            m_ExcludeBoneField = new DropdownField("Bones", new List<string> { k_ExcludePlaceholder }, 0)
            {
                tooltip = "Drop a bone's influence on the selected shape. Unlike Godot's Bones menu " +
                          "(which deletes the polygon's bone weights) this only adds the bone to the " +
                          "asset's excludeBones list, so the weights come back if you remove it again."
            };
            m_ExcludeBoneField.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.target != m_ExcludeBoneField)
                    return;
                ExcludeBoneAt(m_ExcludeBoneField.index);
            });

            m_RigHintLabel = new Label { style = { paddingTop = 2f, left = 2f, opacity = 0.7f, whiteSpace = WhiteSpace.Normal } };

            foldout.Add(m_RigToolButton);
            foldout.Add(m_SetupModeToggle);
            foldout.Add(m_ResetPoseButton);
            foldout.Add(m_BindRigButton);
            foldout.Add(m_ExcludeBoneField);
            foldout.Add(m_RigHintLabel);
            return foldout;
        }

        private static DrawnShapeSkin SelectedSkin()
        {
            var renderer = SelectedRenderer();
            return renderer != null ? renderer.GetComponent<DrawnShapeSkin>() : null;
        }

        /// <summary>The rig the panel acts on: the selected shape's binding first, then any rig
        /// on (or above) the selection so a selected bone counts too.</summary>
        private static ShapeRig SelectedRig()
        {
            var skin = SelectedSkin();
            if (skin != null && skin.rig != null)
                return skin.rig;

            var active = Selection.activeGameObject;
            return active != null ? active.GetComponentInParent<ShapeRig>() : null;
        }

        /// <summary>RigShapeTool is a component tool, so it only exists while a drawn shape is
        /// selected — the button is disabled otherwise and this guard is the safety net.</summary>
        private static void ActivateRigTool()
        {
            if (SelectedRenderer() == null)
                return;
            ToolManager.SetActiveTool<RigShapeTool>();
        }

        private void ResetSelectedPose()
        {
            var rig = SelectedRig();
            if (rig == null)
                return;

            RigShapeTool.ResetRigPose(rig, "Reset Pose");
            RefreshStatus();
        }

        /// <summary>Port of _bind_selected (lines 403-411): bind the selected shape to the rig
        /// next to it. Godot passes no include list, so the bind list is left alone — an empty
        /// one means "every bone of the rig", which is exactly what a manual bind wants.</summary>
        private void BindSelectedToSiblingRig()
        {
            var renderer = SelectedRenderer();
            if (renderer == null)
                return;

            var rig = RigShapeTool.FindSiblingRig(renderer);
            if (rig == null)
            {
                SetRigHint("No rig next to this shape - use the Rig tool to click a chain first.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Bind To Rig");
            int undoGroup = Undo.GetCurrentGroup();

            var skin = RigShapeTool.BindShapeToRig(renderer, rig, null, false, "Bind To Rig");

            Undo.CollapseUndoOperations(undoGroup);

            if (skin == null)
                SetRigHint("That rig has no bones yet - use the Rig tool to click a chain first.");

            RefreshStatus();
            SceneView.RepaintAll();
        }

        /// <summary>Deliberate deviation from Godot's Bones menu (_fill_bones_menu /
        /// _remove_poly_bone lines 413-441), which DELETES the polygon's bone weights: the skin
        /// here is regenerated from the rig every time, so "remove" is expressed as an entry in
        /// the asset's excludeBones list and stays reversible.</summary>
        private void ExcludeBoneAt(int choiceIndex)
        {
            // Index 0 is the placeholder the field snaps back to.
            var boneIndex = choiceIndex - 1;
            if (boneIndex < 0 || boneIndex >= m_ExcludeBoneNames.Count)
            {
                RefreshRigSection();
                return;
            }

            var boneName = m_ExcludeBoneNames[boneIndex];
            var renderer = SelectedRenderer();
            var asset = renderer != null ? renderer.asset : null;
            if (asset == null || string.IsNullOrEmpty(boneName))
            {
                RefreshRigSection();
                return;
            }

            if (asset.excludeBones == null)
                asset.excludeBones = new List<string>();

            if (!asset.excludeBones.Contains(boneName))
            {
                Undo.RecordObject(asset, "Exclude Bone");
                asset.excludeBones.Add(boneName);
                EditorUtility.SetDirty(asset);

                var skin = renderer.GetComponent<DrawnShapeSkin>();
                if (skin != null)
                    skin.RegenerateSkin();
            }

            RefreshRigSection();
            SceneView.RepaintAll();
        }

        /// <summary>Dropdown entries for the bound rig's bones, marked with their exclude state.
        /// Godot's menu labels every entry "✕ name"; already-excluded bones read as such here so
        /// picking one twice is visibly a no-op.</summary>
        private List<string> BuildExcludeChoices(RigAsset rigAsset, DrawnShapeAsset shapeAsset)
        {
            m_ExcludeBoneNames.Clear();

            var choices = new List<string> { k_ExcludePlaceholder };
            if (rigAsset == null || rigAsset.bones == null)
                return choices;

            for (int i = 0; i < rigAsset.bones.Count; i++)
            {
                var name = rigAsset.bones[i].name;
                if (string.IsNullOrEmpty(name))
                    continue;

                var excluded = shapeAsset != null && shapeAsset.excludeBones != null &&
                               shapeAsset.excludeBones.Contains(name);
                m_ExcludeBoneNames.Add(name);
                choices.Add(excluded ? $"- {name} (excluded)" : $"x {name}");
            }

            return choices;
        }

        private void SetRigHint(string text)
        {
            if (m_RigHintLabel != null)
                m_RigHintLabel.text = text ?? string.Empty;
        }

        /// <summary>Mirror the rig state back into the panel. Setup mode, the bind and the
        /// exclude list are all written from elsewhere (the tool, undo, the inspector), so every
        /// control is re-read instead of trusting its last known value.</summary>
        private void RefreshRigSection()
        {
            var renderer = SelectedRenderer();
            var skin = renderer != null ? renderer.GetComponent<DrawnShapeSkin>() : null;
            var rig = SelectedRig();
            var rigAsset = rig != null ? rig.rig : null;
            var shapeAsset = renderer != null ? renderer.asset : null;

            if (m_RigToolButton != null)
                m_RigToolButton.SetEnabled(renderer != null);

            if (m_SetupModeToggle != null && m_SetupModeToggle.value != RigShapeTool.setupMode)
                m_SetupModeToggle.SetValueWithoutNotify(RigShapeTool.setupMode);

            if (m_ResetPoseButton != null)
                m_ResetPoseButton.SetEnabled(rigAsset != null && rigAsset.bones != null && rigAsset.bones.Count > 0);

            if (m_BindRigButton != null)
                m_BindRigButton.SetEnabled(renderer != null && RigShapeTool.FindSiblingRig(renderer) != null);

            if (m_ExcludeBoneField != null)
            {
                var boundAsset = skin != null && skin.rig != null ? skin.rig.rig : null;
                var choices = BuildExcludeChoices(boundAsset, shapeAsset);
                m_ExcludeBoneField.choices = choices;
                m_ExcludeBoneField.SetValueWithoutNotify(choices[0]);
                m_ExcludeBoneField.SetEnabled(shapeAsset != null && choices.Count > 1);
            }

            if (m_RigHintLabel != null)
            {
                if (renderer == null)
                    SetRigHint("Select a drawn shape to rig it.");
                else if (skin == null || skin.rig == null)
                    SetRigHint(rig != null ? "Not bound - use Bind To Sibling Rig." : "No rig yet - use the Rig tool.");
                else if (RigShapeTool.setupMode)
                    SetRigHint("Setup mode: bone moves become the new REST pose.");
                else
                    SetRigHint(string.Empty);
            }
        }

        // --- Anim (M4) --------------------------------------------------------------------

        /// <summary>The M4 Anim section: the way into the Pose Sheet, the Godot Bones-menu entry
        /// that has nothing to do with bones (_capture_form lines 564-575), and an honest readout
        /// of whether AUTO-KEY is recording — it only records while the Pose Sheet is open, since
        /// the sheet owns the poll and the animator binding.</summary>
        private VisualElement BuildAnimSection()
        {
            var foldout = new Foldout
            {
                text = "Anim",
                value = EditorPrefs.GetBool(k_AnimFoldoutKey, true)
            };
            foldout.RegisterValueChangedCallback(changeEvent =>
            {
                // Child field events bubble up to the foldout, so only react to its own.
                if (changeEvent.target == foldout)
                    EditorPrefs.SetBool(k_AnimFoldoutKey, changeEvent.newValue);
            });

            var openButton = new Button(PoseSheetWindow.Open)
            {
                text = "Open Pose Sheet",
                tooltip = "Key, retime and scrub pose columns on the selected rig's PoseAnimator. " +
                          "AUTO-KEY and the play preview live there."
            };

            m_CaptureFormButton = new Button(CaptureFormOnSelection)
            {
                text = "Capture Form",
                tooltip = "Snapshot the selected shape's CURRENT outline as a form variant, then " +
                          "key its 'morph' channel to blend to it. Deformation keyframes without " +
                          "a single bone - the Spine-style trick the Godot toolbar called 'Form'."
            };

            m_AutoKeyLabel = new Label { style = { paddingTop = 2f, left = 2f, whiteSpace = WhiteSpace.Normal } };
            m_AnimHintLabel = new Label { style = { paddingTop = 2f, left = 2f, opacity = 0.7f, whiteSpace = WhiteSpace.Normal } };

            foldout.Add(openButton);
            foldout.Add(m_CaptureFormButton);
            foldout.Add(m_AutoKeyLabel);
            foldout.Add(m_AnimHintLabel);
            return foldout;
        }

        /// <summary>Port of terrain_paint.gd _capture_form (lines 564-575): append the live curve
        /// to the shape's morph targets as ONE undo step, then say which variant it became.
        /// curve_shape_2d.gd's `capture_form()` is the two lines in the middle — it lives here
        /// rather than on the renderer because the renderer never mutates its own asset.</summary>
        private void CaptureFormOnSelection()
        {
            var renderer = SelectedRenderer();
            var asset = renderer != null ? renderer.asset : null;
            if (asset == null)
            {
                SetAnimHint("Select a drawn shape with an asset to capture its form.");
                return;
            }

            if (asset.curve == null || asset.curve.pointCount < 3)
            {
                SetAnimHint("This shape has no outline yet - draw one first.");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Capture Form Variant");

            Undo.RecordObject(asset, "Capture Form Variant");
            var targets = asset.morphTargets != null
                ? new List<DrawnCurve>(asset.morphTargets)
                : new List<DrawnCurve>();
            targets.Add(asset.curve.Clone());
            asset.morphTargets = targets;
            EditorUtility.SetDirty(asset);

            // The blend range grew, so the render mesh (and through geometryChanged the skin and
            // paint layers) is rebuilt even though morphWeight has not moved yet.
            renderer.Regenerate();

            Undo.CollapseUndoOperations(undoGroup);

            SetAnimHint($"Form captured (variant {targets.Count}) - key '{renderer.name}:morph' to blend to it.");
            RefreshStatus();
            SceneView.RepaintAll();
        }

        private void SetAnimHint(string text)
        {
            if (m_AnimHintLabel != null)
                m_AnimHintLabel.text = text ?? string.Empty;
        }

        /// <summary>The auto-key state line. "Armed" and "recording" are different things here:
        /// the arm is an EditorPref, but nothing polls while the Pose Sheet is closed.</summary>
        private void RefreshAnimSection()
        {
            var renderer = SelectedRenderer();

            if (m_CaptureFormButton != null)
                m_CaptureFormButton.SetEnabled(renderer != null && renderer.asset != null);

            if (m_AutoKeyLabel == null)
                return;

            if (!PoseSheetState.autoKey)
            {
                m_AutoKeyLabel.text = "Auto-key: off";
                return;
            }

            m_AutoKeyLabel.text = PoseSheetState.windowOpen
                ? $"Auto-key: RECORDING - {PoseSheetState.status}"
                : "Auto-key: armed, but the Pose Sheet is closed - nothing is recording.";
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
                else if (activeToolType == typeof(RigShapeTool))
                    mode = "Rig";
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

                var skin = renderer != null ? renderer.GetComponent<DrawnShapeSkin>() : null;
                var rigState = renderer == null
                    ? "-"
                    : skin == null || skin.rig == null ? "no" : skin.rig.name;

                // The animator is looked up from the SELECTION (not the shape): a bone, the rig
                // root or the actor are all valid things to have selected while animating.
                var animator = active != null ? active.GetComponentInParent<PoseAnimator>() : null;
                var clip = animator != null ? animator.Clip() : null;
                var animState = animator == null
                    ? "-"
                    : clip == null ? "no clip" : $"{clip.name} ({clip.poseCount})";

                m_StatusLabel.text = $"Tool: {mode}\nShape: {shapeName}\nBody: {blobState}\n" +
                                     $"Paint: {paintState}\nRig: {rigState}\nClip: {animState}";
            }

            if (m_AddTerrainBlobButton != null)
                m_AddTerrainBlobButton.SetEnabled(CountBlobCandidates() > 0);

            RefreshPaintSection();
            RefreshRigSection();
            RefreshAnimSection();

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

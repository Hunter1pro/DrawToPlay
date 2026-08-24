using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Scene-view panel replacing the Godot plugin's "Stamps" dock (terrain_paint.gd _enter_tree
    /// lines 1206-1242 plus _rescan / _disarm, lines 1046-1086): a folder to scan, a thumbnail
    /// grid whose buttons ARM one stamp at a time, and the scatter parameters (random flip,
    /// scale range, spacing) that <see cref="StampTool"/> reads while dragging.
    ///
    /// The static half of this class is the shared, EditorPrefs-backed stamp state — the direct
    /// counterpart of the Godot dock's control values (`_flip_check`, `_scale_min`, `_scale_max`,
    /// `_spacing`) plus the transient `_armed` / `_armed_btn` pair. Kept here rather than in
    /// <see cref="DrawToolSettings"/> so the M2 stamp round owns its own preference keys.
    ///
    /// Godot deviations, all documented in the M2 report:
    ///  - Godot arms a stamp and the plugin's input router immediately prefers `_stamp_input`
    ///    (line 178-179). Unity has no such router, so arming ACTIVATES <see cref="StampTool"/>;
    ///    the explicit button below does the same thing for a stamp that is already armed.
    ///  - Godot scans one flat directory (`DirAccess.get_files_at`). This scans recursively via
    ///    the AssetDatabase so stamps can be organised in sub-folders; the 60-entry cap is kept.
    ///  - Godot's dock only accepts textures. Prefabs are accepted here too (brief §4: "a stamped
    ///    lantern brings its light + body") and preview through AssetPreview.
    /// </summary>
    [Overlay(typeof(SceneView),
        k_OverlayId,
        k_DisplayName,
        defaultDisplay = true,
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Bottom,
        defaultLayout = Layout.Panel,
        defaultWidth = k_DefaultWidth,
        defaultHeight = k_DefaultHeight)]
    internal sealed class StampOverlay : Overlay
    {
        private const string k_OverlayId = "Scene View/Draw To Play Stamps";
        private const string k_DisplayName = "Stamps";
        private const float k_DefaultWidth = 246f;
        private const float k_DefaultHeight = 300f;

        // --- shared state (the Godot dock's control values) -----------------------------------

        private const string k_FolderKey = "PowerOfFire.DrawToPlay.StampFolder";
        private const string k_FlipKey = "PowerOfFire.DrawToPlay.StampRandomFlip";
        private const string k_ScaleMinKey = "PowerOfFire.DrawToPlay.StampScaleMin";
        private const string k_ScaleMaxKey = "PowerOfFire.DrawToPlay.StampScaleMax";
        private const string k_SpacingKey = "PowerOfFire.DrawToPlay.StampSpacing";

        /// <summary>Godot `_folder_edit.text` ("res://content/art/terrain"), relocated to this
        /// project's own convention.</summary>
        internal static string DefaultFolder => DrawToPlayFolders.Stamps;

        /// <summary>Godot `_spin(4.0, 200.0, 2.0, 24.0, "scatter spacing px")` — px ÷ 32, so
        /// 24 px becomes 0.75 world units and the 4..200 px range becomes 0.125..6.25 wu.</summary>
        internal const float DefaultSpacing = 0.75f;
        internal const float MinSpacing = 0.125f;
        internal const float MaxSpacing = 6.25f;

        /// <summary>Godot `_spin(0.1, 8.0, 0.1, 1.0, "min/max scale")` — a multiplier, unitless,
        /// so it ports unchanged.</summary>
        internal const float MinScale = 0.1f;
        internal const float MaxScale = 8f;

        /// <summary>Godot `if shown >= 60: break` in _rescan.</summary>
        internal const int MaxEntries = 60;

        /// <summary>Godot `_flip_check.button_pressed = true` — random flip defaults ON.</summary>
        private const bool k_DefaultRandomFlip = true;

        /// <summary>Scan folder (Godot `_folder_edit`).</summary>
        internal static string folderPath
        {
            get => EditorPrefs.GetString(k_FolderKey, DefaultFolder);
            set => EditorPrefs.SetString(k_FolderKey, string.IsNullOrEmpty(value) ? DefaultFolder : value);
        }

        /// <summary>Mirror half the stamps horizontally (Godot `_flip_check`).</summary>
        internal static bool randomFlip
        {
            get => EditorPrefs.GetBool(k_FlipKey, k_DefaultRandomFlip);
            set => EditorPrefs.SetBool(k_FlipKey, value);
        }

        /// <summary>Lower end of the uniform scale range (Godot `_scale_min`).</summary>
        internal static float scaleMin
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(k_ScaleMinKey, 1f), MinScale, MaxScale);
            set => EditorPrefs.SetFloat(k_ScaleMinKey, Mathf.Clamp(value, MinScale, MaxScale));
        }

        /// <summary>Upper end of the uniform scale range (Godot `_scale_max`). Godot takes
        /// min()/max() of the pair at scatter time, so the two may legally be inverted.</summary>
        internal static float scaleMax
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(k_ScaleMaxKey, 1f), MinScale, MaxScale);
            set => EditorPrefs.SetFloat(k_ScaleMaxKey, Mathf.Clamp(value, MinScale, MaxScale));
        }

        /// <summary>Minimum distance between two placements, in WORLD units (Godot `_spacing`,
        /// viewport px). Also the jitter base: jitter = ±spacing * 0.2 per axis.</summary>
        internal static float spacing
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(k_SpacingKey, DefaultSpacing), MinSpacing, MaxSpacing);
            set => EditorPrefs.SetFloat(k_SpacingKey, Mathf.Clamp(value, MinSpacing, MaxSpacing));
        }

        // Transient, exactly like Godot's `_armed` — arming is a gesture, not a preference.
        private static UnityEngine.Object s_ArmedStamp;
        private static string s_ArmedPath = string.Empty;

        /// <summary>Raised whenever the armed stamp changes, so every open Stamps panel (there
        /// is one per SceneView) can re-highlight its grid.</summary>
        internal static event Action armedChanged;

        /// <summary>The armed stamp asset (Texture2D or prefab GameObject), or null.</summary>
        internal static UnityEngine.Object armedStamp => s_ArmedStamp;

        /// <summary>Project path of <see cref="armedStamp"/>; empty when nothing is armed.</summary>
        internal static string armedPath => s_ArmedPath;

        /// <summary>True while a stamp is armed — <see cref="StampTool"/> is inert otherwise,
        /// which is what keeps normal scene picking working when the tool is left active.</summary>
        internal static bool isArmed => s_ArmedStamp != null;

        /// <summary>Arm one stamp, exclusively (Godot's toggled handler unpresses the previous
        /// button). Also puts the user in the stamp tool, which is what Godot's input router
        /// does implicitly the moment `_armed` is non-null.</summary>
        internal static void Arm(UnityEngine.Object asset, string path)
        {
            if (asset == null)
            {
                Disarm();
                return;
            }

            s_ArmedStamp = asset;
            s_ArmedPath = path ?? string.Empty;
            armedChanged?.Invoke();

            if (ToolManager.activeToolType != typeof(StampTool))
                ToolManager.SetActiveTool<StampTool>();

            SceneView.RepaintAll();
        }

        /// <summary>Port of _disarm (line 1049): clear the armed stamp and every button's
        /// pressed state. Called by Esc in the tool and by Rescan.</summary>
        internal static void Disarm()
        {
            if (s_ArmedStamp == null && s_ArmedPath.Length == 0)
                return;

            s_ArmedStamp = null;
            s_ArmedPath = string.Empty;
            armedChanged?.Invoke();
            SceneView.RepaintAll();
        }

        // --- panel ----------------------------------------------------------------------------

        /// <summary>How often the panel re-asks AssetPreview for prefab thumbnails. Previews are
        /// generated asynchronously, so the first GetAssetPreview after a scan usually misses.</summary>
        private const long k_PreviewPollMilliseconds = 150;

        private const float k_ThumbnailSize = 54f;

        private static readonly Color k_ArmedBorderColor = new Color(1f, 0.72f, 0.3f, 1f);

        private readonly List<StampEntry> m_Entries = new List<StampEntry>();

        private VisualElement m_Root;
        private VisualElement m_Grid;
        private TextField m_FolderField;
        private Button m_CreateFolderButton;
        private Label m_StatusLabel;

        /// <summary>One scanned stamp asset and the button that arms it.</summary>
        private sealed class StampEntry
        {
            public string path;
            public UnityEngine.Object asset;
            public Button button;
            public Image image;
            public bool previewPending;
        }

        public override void OnCreated()
        {
            armedChanged += RefreshArmedState;
        }

        public override void OnWillBeDestroyed()
        {
            armedChanged -= RefreshArmedState;
            m_Entries.Clear();
        }

        public override VisualElement CreatePanelContent()
        {
            m_Root = new VisualElement { style = { width = new StyleLength(k_DefaultWidth) } };

            m_FolderField = new TextField("Folder")
            {
                value = folderPath,
                tooltip = "Project folder scanned for stamps (Godot `_folder_edit`). Textures and " +
                          "prefabs are both picked up, sub-folders included."
            };
            // Commit on Enter / focus loss rather than per keystroke: the field is normalised
            // (empty falls back to the default) and re-read on every status refresh, which would
            // otherwise fight the user mid-word.
            m_FolderField.isDelayed = true;
            m_FolderField.RegisterValueChangedCallback(changeEvent =>
            {
                folderPath = changeEvent.newValue;
                RefreshStatus();
            });

            m_CreateFolderButton = new Button(CreateFolder)
            {
                text = "Create Folder",
                tooltip = "Create the folder above, including any missing parents."
            };

            var rescanButton = new Button(Rescan)
            {
                text = "Rescan",
                tooltip = "Re-read the folder and rebuild the grid. Disarms the current stamp, " +
                          "exactly like Godot's Scan button."
            };

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            m_CreateFolderButton.style.flexGrow = 1f;
            rescanButton.style.flexGrow = 1f;
            buttonRow.Add(m_CreateFolderButton);
            buttonRow.Add(rescanButton);

            var flipToggle = new Toggle("Random Flip")
            {
                value = randomFlip,
                tooltip = "Mirror roughly half the stamps horizontally (Godot `rnd flip`)."
            };
            flipToggle.RegisterValueChangedCallback(changeEvent => randomFlip = changeEvent.newValue);

            var scaleMinField = new FloatField("Scale Min") { value = scaleMin };
            scaleMinField.tooltip = "Lower end of the uniform random scale applied to each stamp.";
            scaleMinField.RegisterValueChangedCallback(changeEvent =>
            {
                scaleMin = changeEvent.newValue;
                scaleMinField.SetValueWithoutNotify(scaleMin);
            });

            var scaleMaxField = new FloatField("Scale Max") { value = scaleMax };
            scaleMaxField.tooltip = "Upper end of the uniform random scale. Inverting the pair is " +
                                    "harmless — the tool takes min()/max() of the two, as Godot does.";
            scaleMaxField.RegisterValueChangedCallback(changeEvent =>
            {
                scaleMax = changeEvent.newValue;
                scaleMaxField.SetValueWithoutNotify(scaleMax);
            });

            var spacingField = new FloatField("Spacing") { value = spacing };
            spacingField.tooltip = "Minimum distance between two placements during a drag, in WORLD " +
                                   "units (0.75 = Godot's 24 px). Also drives the ± spacing * 0.2 jitter.";
            spacingField.RegisterValueChangedCallback(changeEvent =>
            {
                spacing = changeEvent.newValue;
                spacingField.SetValueWithoutNotify(spacing);
                SceneView.RepaintAll();
            });

            m_Grid = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    paddingTop = 4f,
                    paddingBottom = 4f
                }
            };

            m_StatusLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingTop = 2f, left = 2f } };

            var toolButton = new Button(ToolManager.SetActiveTool<StampTool>)
            {
                text = "Activate Stamp Tool",
                tooltip = "Drag with LMB in the scene view to scatter the armed stamp. " +
                          "Esc disarms."
            };

            m_Root.Add(m_FolderField);
            m_Root.Add(buttonRow);
            m_Root.Add(flipToggle);
            m_Root.Add(scaleMinField);
            m_Root.Add(scaleMaxField);
            m_Root.Add(spacingField);
            m_Root.Add(m_Grid);
            m_Root.Add(m_StatusLabel);
            m_Root.Add(toolButton);

            Rescan();

            // Prefab previews arrive asynchronously; this cheap tick fills them in as they land
            // and is a no-op once every entry has a thumbnail.
            m_Root.schedule.Execute(UpdatePendingPreviews).Every(k_PreviewPollMilliseconds);

            return m_Root;
        }

        // --- scanning -------------------------------------------------------------------------

        /// <summary>Port of _rescan (line 1057): clear the grid, disarm, re-read the folder,
        /// build one toggle button per stamp, stop at <see cref="MaxEntries"/>.</summary>
        private void Rescan()
        {
            Disarm();

            m_Entries.Clear();
            if (m_Grid != null)
                m_Grid.Clear();

            var paths = ScanFolder(folderPath);
            for (var i = 0; i < paths.Count; ++i)
            {
                var path = paths[i];
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (!(asset is Texture2D) && !(asset is GameObject))
                    continue;

                var entry = new StampEntry { path = path, asset = asset };
                BuildEntryButton(entry);
                m_Entries.Add(entry);
                m_Grid.Add(entry.button);
            }

            RefreshStatus();
        }

        /// <summary>Every Texture2D and prefab GameObject under the folder, sorted for a stable
        /// grid order and capped at 60. Two FindAssets calls instead of one multi-`t:` filter,
        /// because only the single-type form's behaviour is beyond doubt.</summary>
        private static List<string> ScanFolder(string folder)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return paths;

            var roots = new[] { folder };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AppendGuids(AssetDatabase.FindAssets("t:Texture2D", roots), seen, paths);
            AppendGuids(AssetDatabase.FindAssets("t:GameObject", roots), seen, paths);

            paths.Sort(StringComparer.Ordinal);
            if (paths.Count > MaxEntries)
                paths.RemoveRange(MaxEntries, paths.Count - MaxEntries);
            return paths;
        }

        private static void AppendGuids(string[] guids, HashSet<string> seen, List<string> paths)
        {
            if (guids == null)
                return;

            for (var i = 0; i < guids.Length; ++i)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    continue;
                paths.Add(path);
            }
        }

        /// <summary>Create every missing folder along the configured path (same walk as
        /// DrawShapeTool.EnsureDrawnFolder), then rescan into it.</summary>
        private void CreateFolder()
        {
            var target = folderPath;
            if (string.IsNullOrEmpty(target) || !target.StartsWith("Assets", StringComparison.Ordinal))
                return;

            var segments = target.Split('/');
            var path = segments[0];
            for (var i = 1; i < segments.Length; ++i)
            {
                if (string.IsNullOrEmpty(segments[i]))
                    break;

                var next = $"{path}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    break;
                path = next;
            }

            AssetDatabase.Refresh();
            Rescan();
        }

        // --- grid -----------------------------------------------------------------------------

        /// <summary>Port of the per-stamp Button in _rescan (52x52, icon, tooltip, exclusive
        /// toggle). Clicking the armed entry again disarms, which is Godot's `toggled(false)`.</summary>
        private void BuildEntryButton(StampEntry entry)
        {
            var button = new Button(() =>
            {
                if (s_ArmedStamp == entry.asset)
                    Disarm();
                else
                    Arm(entry.asset, entry.path);
            });

            button.text = string.Empty;
            button.tooltip = BuildTooltip(entry);
            button.style.width = k_ThumbnailSize;
            button.style.height = k_ThumbnailSize;
            button.style.paddingLeft = 2f;
            button.style.paddingRight = 2f;
            button.style.paddingTop = 2f;
            button.style.paddingBottom = 2f;
            button.style.marginLeft = 1f;
            button.style.marginRight = 1f;
            button.style.marginTop = 1f;
            button.style.marginBottom = 1f;

            entry.image = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style = { flexGrow = 1f }
            };
            button.Add(entry.image);

            entry.button = button;
            ApplyPreview(entry);
            ApplyArmedStyle(entry);
        }

        private static string BuildTooltip(StampEntry entry)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
            if (entry.asset is GameObject)
                return $"{name} (prefab) — arms this stamp and switches to the Stamp tool.";

            // A texture with no Sprite sub-asset can still be stamped, but the SpriteRenderer
            // then points at a Sprite created in memory, which does not survive a domain reload.
            var hasSprite = AssetDatabase.LoadAssetAtPath<Sprite>(entry.path) != null;
            return hasSprite
                ? $"{name} (sprite) — arms this stamp and switches to the Stamp tool."
                : $"{name} (texture) — set its Texture Type to Sprite so stamped copies keep a " +
                  "saved sprite reference; otherwise the sprite is rebuilt in memory and is lost " +
                  "on the next script reload.";
        }

        /// <summary>Textures preview as themselves; prefabs go through AssetPreview, which is
        /// asynchronous — the mini thumbnail stands in until the real preview lands.</summary>
        private static void ApplyPreview(StampEntry entry)
        {
            if (entry.asset == null || entry.image == null)
                return;

            if (entry.asset is Texture2D texture)
            {
                entry.image.image = texture;
                entry.previewPending = false;
                return;
            }

            var preview = AssetPreview.GetAssetPreview(entry.asset);
            if (preview != null)
            {
                entry.image.image = preview;
                entry.previewPending = false;
                return;
            }

            entry.image.image = AssetPreview.GetMiniThumbnail(entry.asset);
            entry.previewPending = true;
        }

        private void UpdatePendingPreviews()
        {
            for (var i = 0; i < m_Entries.Count; ++i)
            {
                var entry = m_Entries[i];
                if (!entry.previewPending)
                    continue;
                ApplyPreview(entry);
            }
        }

        private void RefreshArmedState()
        {
            for (var i = 0; i < m_Entries.Count; ++i)
                ApplyArmedStyle(m_Entries[i]);

            RefreshStatus();
        }

        private static void ApplyArmedStyle(StampEntry entry)
        {
            if (entry.button == null)
                return;

            var armed = s_ArmedStamp != null && s_ArmedStamp == entry.asset;
            var width = armed ? 2f : 0f;
            var color = armed ? k_ArmedBorderColor : Color.clear;

            entry.button.style.borderTopWidth = width;
            entry.button.style.borderBottomWidth = width;
            entry.button.style.borderLeftWidth = width;
            entry.button.style.borderRightWidth = width;
            entry.button.style.borderTopColor = color;
            entry.button.style.borderBottomColor = color;
            entry.button.style.borderLeftColor = color;
            entry.button.style.borderRightColor = color;
        }

        private void RefreshStatus()
        {
            var folder = folderPath;
            var folderExists = !string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder);

            if (m_CreateFolderButton != null)
                m_CreateFolderButton.SetEnabled(!folderExists);

            if (m_FolderField != null && m_FolderField.value != folder)
                m_FolderField.SetValueWithoutNotify(folder);

            if (m_StatusLabel == null)
                return;

            if (!folderExists)
            {
                m_StatusLabel.text = "Folder does not exist yet — Create Folder, then drop stamp " +
                                     "textures or prefabs into it and Rescan.";
                return;
            }

            if (m_Entries.Count == 0)
            {
                m_StatusLabel.text = "No stamps found. Drop textures or prefabs into the folder and Rescan.";
                return;
            }

            var armedName = isArmed
                ? System.IO.Path.GetFileNameWithoutExtension(s_ArmedPath)
                : "none";
            var capped = m_Entries.Count >= MaxEntries ? $" (capped at {MaxEntries})" : string.Empty;
            m_StatusLabel.text = $"{m_Entries.Count} stamp(s){capped}\nArmed: {armedName}";
        }
    }
}

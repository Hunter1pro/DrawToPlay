using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Shared, EditorPrefs-backed Pose Sheet state — the M4 counterpart of
    /// <see cref="DrawToolSettings"/>. The Pose Sheet window owns the editing session; this
    /// class holds the two pieces of it that outlive a window instance (the AUTO-KEY arm and
    /// the timeline snap) plus the status line, so the scene-view overlay can report what the
    /// sheet is doing without holding a reference to the window.
    ///
    /// Godot keeps all of this on the pose_sheet.gd Control itself (`_auto_btn.button_pressed`,
    /// `_snap_spin.value`, `_status.text`) because that panel is always alive inside the plugin
    /// dock. A Unity EditorWindow is created and destroyed freely, so the state moves here and
    /// the arm survives a close/reopen and a domain reload.
    /// </summary>
    public static class PoseSheetState
    {
        /// <summary>EditorPrefs key behind <see cref="autoKey"/>. Public so the overlay (and a
        /// later Flow stage) can drive the same switch.</summary>
        public const string AutoKeyPrefKey = "PowerOfFire.DrawToPlay.PoseAutoKey";

        private const string k_SnapPrefKey = "PowerOfFire.DrawToPlay.PoseSnap";

        /// <summary>Snap spin range — pose_sheet.gd lines 78-83 (`min 0.01, max 2.0, value 0.1`),
        /// in SECONDS (the Godot sheet is already in seconds, so nothing converts here).</summary>
        public const float SnapMin = 0.01f;
        public const float SnapMax = 2f;
        public const float SnapDefault = 0.1f;

        /// <summary>Clip-length spin range — pose_sheet.gd lines 60-64.</summary>
        public const float ClipLengthMin = 0.1f;
        public const float ClipLengthMax = 600f;

        /// <summary>Raised whenever a value the overlay mirrors changes (arm, status, the sheet
        /// opening or closing). The Godot stand-in is the panel simply redrawing itself.</summary>
        public static event Action changed;

        public static void NotifyChanged() => changed?.Invoke();

        /// <summary>AUTO-KEY: while armed AND the Pose Sheet is open, any rig change keys itself
        /// at the playhead (pose_sheet.gd `_auto_btn` + `_autokey_poll`). Nothing polls while the
        /// window is closed — the sheet owns the poll because it owns the bound animator.</summary>
        public static bool autoKey
        {
            get => EditorPrefs.GetBool(AutoKeyPrefKey, false);
            set
            {
                if (EditorPrefs.GetBool(AutoKeyPrefKey, false) == value)
                    return;
                EditorPrefs.SetBool(AutoKeyPrefKey, value);
                NotifyChanged();
            }
        }

        /// <summary>Timeline snap in seconds (`_snap_spin`), also the ruler's tick step.</summary>
        public static float snap
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(k_SnapPrefKey, SnapDefault), SnapMin, SnapMax);
            set
            {
                var snapped = Mathf.Clamp(value, SnapMin, SnapMax);
                if (Mathf.Approximately(snapped, snap))
                    return;
                EditorPrefs.SetFloat(k_SnapPrefKey, snapped);
                NotifyChanged();
            }
        }

        /// <summary>True while a Pose Sheet window exists (set by the window's OnEnable/OnDisable).
        /// The overlay needs it to tell "armed" from "armed and actually recording".</summary>
        public static bool windowOpen { get; internal set; }

        private static string s_Status = string.Empty;

        /// <summary>The sheet's `_status` label, mirrored so the overlay can show it.</summary>
        public static string status
        {
            get => s_Status ?? string.Empty;
            internal set
            {
                var text = value ?? string.Empty;
                if (string.Equals(text, s_Status, StringComparison.Ordinal))
                    return;
                s_Status = text;
                NotifyChanged();
            }
        }

        /// <summary>Godot `snappedf(value, step)` — round to the nearest multiple.</summary>
        public static float SnapTo(float value)
        {
            var step = snap;
            return step > 0f ? Mathf.Round(value / step) * step : value;
        }

        /// <summary>Port of terrain_paint.gd `_any_anim_playing` (lines 504-512), narrowed to the
        /// types this toolset owns: true while ANY <see cref="PoseAnimator"/> in the open stage is
        /// playing. The rule it guards is Godot's: never bake or auto-key while an animation
        /// preview is running, or the preview's own output gets recorded as authoring input.
        ///
        /// Godot also checks AnimationPlayer; Unity's equivalent (an Animator previewing in the
        /// editor) has no supported "is previewing" query outside the Animation window, so it is
        /// deliberately not checked — see the M4 report.</summary>
        public static bool AnyPreviewPlaying()
        {
            var stage = StageUtility.GetCurrentStageHandle();
            var animators = stage.IsValid()
                ? stage.FindComponentsOfType<PoseAnimator>()
                : Array.Empty<PoseAnimator>();

            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null && animators[i].playing)
                    return true;
            }

            return false;
        }
    }
}

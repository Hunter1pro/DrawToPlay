using System.IO;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Scene-view readout of what <see cref="ActiveStateHighlight"/> is tracking: which runner, which
    /// state it is in, and which state it came from.
    ///
    /// This is the second half of the M7 highlight. The node tint is the real answer and lives in the
    /// graph window; this panel exists because the graph window is often CLOSED while the game runs,
    /// and because a runner whose tree was built by <c>StateTreePresets</c> rather than by a graph has
    /// no node to tint at all — it still has an active state worth watching. It also makes the tint's
    /// state legible: "tinting" versus "reporting only".
    ///
    /// It follows the panel conventions already in the project (see <c>Editor/DrawToPlayOverlay.cs</c>):
    /// UI Toolkit content, repainted from an event rather than polled.
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
    internal sealed class ActiveStateHighlightOverlay : Overlay
    {
        private const string k_OverlayId = "Scene View/Draw To Play AI State";
        private const string k_DisplayName = "AI State";
        private const float k_DefaultWidth = 236f;
        private const float k_DefaultHeight = 116f;

        private Toggle m_EnabledToggle;
        private Label m_RunnerLabel;
        private Label m_StateLabel;
        private Label m_SourceLabel;

        public override void OnCreated()
        {
            ActiveStateHighlight.changed += Refresh;
            Selection.selectionChanged += Refresh;
        }

        public override void OnWillBeDestroyed()
        {
            ActiveStateHighlight.changed -= Refresh;
            Selection.selectionChanged -= Refresh;
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { width = new StyleLength(k_DefaultWidth) } };

            m_EnabledToggle = new Toggle("Highlight In Graph") { value = ActiveStateHighlight.enabled };
            m_EnabledToggle.tooltip = "Tint the active state's node in the state tree graph window "
                + "while the game runs.";
            m_EnabledToggle.RegisterValueChangedCallback(evt => ActiveStateHighlight.enabled = evt.newValue);
            root.Add(m_EnabledToggle);

            m_RunnerLabel = NewLine(root, 6f);
            m_StateLabel = NewLine(root, 2f);
            m_SourceLabel = NewLine(root, 2f);

            Refresh();
            return root;
        }

        private static Label NewLine(VisualElement parent, float paddingTop)
        {
            var label = new Label
            {
                style =
                {
                    paddingTop = paddingTop,
                    left = 2f,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            parent.Add(label);
            return label;
        }

        private void Refresh()
        {
            if (m_RunnerLabel == null)
                return;

            if (m_EnabledToggle != null)
                m_EnabledToggle.SetValueWithoutNotify(ActiveStateHighlight.enabled);

            StateTreeRunner runner = ActiveStateHighlight.runner;
            if (runner == null)
            {
                m_RunnerLabel.text = EditorApplication.isPlaying
                    ? "No state tree running."
                    : "Enter play mode to follow a state tree.";
                m_StateLabel.text = string.Empty;
                m_SourceLabel.text = string.Empty;
                return;
            }

            m_RunnerLabel.text = "Runner: " + runner.gameObject.name;

            string active = ActiveStateHighlight.activeNodeId;
            string previous = ActiveStateHighlight.previousNodeId;
            m_StateLabel.text = string.IsNullOrEmpty(active)
                ? "State: (stopped)"
                : string.IsNullOrEmpty(previous)
                    ? "State: " + active
                    : $"State: {previous} → {active}";

            string path = ActiveStateHighlight.graphPath;
            m_SourceLabel.text = string.IsNullOrEmpty(path)
                ? "No graph for this tree (reporting only)."
                : ActiveStateHighlight.isTinting
                    ? "Tinting in " + Path.GetFileName(path)
                    : "Graph " + Path.GetFileName(path) + " has no node for this state.";
        }
    }
}

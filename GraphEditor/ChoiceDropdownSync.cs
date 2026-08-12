using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// MAKES A REFRESHED DROPDOWN ACTUALLY APPEAR — the last mile of
    /// <see cref="ChoicePortRefresh"/>, and the one that cannot be done through the model.
    ///
    /// THE PROBLEM, MEASURED. Changing a Registry Entry node's registry updates its port model
    /// correctly and settles in one pass — the Entry pin really does carry the new registry's rows.
    /// The graph VIEW does not follow: a port's inline editor is built once and never rebuilt for a
    /// change of choices. <c>Node.DefineNode</c> does not do it, and neither does registering the
    /// change on the graph model — GraphTopology, Data, NeedsRedraw, Style and Layout were each
    /// tried and none rebuilds a <see cref="DropdownField"/>. Until the graph is closed and
    /// reopened, the author is picking from the previous catalog with nothing on screen saying so,
    /// and every value it offers is wrong for the registry they just chose. That is how an author
    /// gets locked out of their own node.
    ///
    /// SO THE WIDGET IS RECONCILED DIRECTLY. Walk the open graph windows, and for every dropdown
    /// whose port carries a choice list, replace the widget's list when it disagrees. The link
    /// back to the model is the <c>PortImp</c> element three levels above the field, which exposes
    /// its <c>PortModel</c>. Nothing here decides what the choices ARE — that is the model's job,
    /// already done — this only stops the screen from lying about them.
    ///
    /// It is a small, throttled sweep over widgets that already exist, and every step is guarded:
    /// a window shape this cannot read is left exactly as it is, which is the behaviour before any
    /// of this existed.
    /// </summary>
    [InitializeOnLoad]
    internal static class ChoiceDropdownSync
    {
        /// <summary>The window whose dropdowns are reconciled, by full type name —
        /// <c>GraphViewEditorWindowImp</c> is internal and cannot be named at compile time.</summary>
        private const string k_GraphWindowTypeName =
            "Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp";

        /// <summary>How often the sweep runs. Fast enough that a registry change looks immediate,
        /// slow enough that it is nowhere near the editor's per-frame work — the sweep only walks
        /// the visual tree of graph windows that are actually open.</summary>
        private const double k_Interval = 0.25;

        private static double s_NextSweep;

        static ChoiceDropdownSync()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < s_NextSweep)
                return;
            s_NextSweep = EditorApplication.timeSinceStartup + k_Interval;

            if (s_PickerAttached.Count > 512)
                s_PickerAttached.RemoveWhere(field => field.panel == null);

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.GetType().FullName != k_GraphWindowTypeName)
                    continue;

                try
                {
                    s_Graph = RefreshGraphOf(window);
                    s_Host = window;
                    Sweep(window.rootVisualElement);
                }
                catch (Exception)
                {
                    // A window mid-rebuild throws rather than answering; the next sweep is 250ms
                    // away and a console line every quarter second would be its own bug.
                }
            }
        }

        /// <summary>
        /// Run the model-side refresh for the graph this window is showing.
        ///
        /// WHY NOT FROM <c>OnGraphChanged</c>, where it also lives. That hook does fire, but not
        /// dependably AFTER the edit it is reacting to: changing a Registry Entry's registry
        /// updates the pin, and the pass that should notice reads the pin before the new value has
        /// landed, decides nothing is stale, and never looks again. The author is then left with
        /// the previous catalog until something else changes. Running the same idempotent pass on
        /// a timer removes the ordering question entirely — it costs one comparison per node when
        /// nothing has moved, and it settles in a single pass when something has.
        /// </summary>
        /// <param name="window">A graph window.</param>
        private static Graph RefreshGraphOf(EditorWindow window)
        {
            Graph graph = GraphOf(window);
            if (graph == null)
                return null;

            var nodes = new List<INode>(graph.GetNodes());
            ChoicePortRefresh.Refresh(nodes);
            return graph;
        }

        /// <summary>The graph whose window is being swept — what the picker's rows are described
        /// from. Set for the duration of one window's sweep and never read outside it.</summary>
        private static Graph s_Graph;

        /// <summary>The window that window is — passed to the picker so it anchors against the
        /// GRAPH rather than against whatever happens to be focused, which once the picker is open
        /// is the picker (see <c>StateTreeNodePicker.ScreenRectOf</c>).</summary>
        private static EditorWindow s_Host;

        /// <summary>The graph a window is showing, matched by the asset whose file name is the
        /// window's title, and remembered so the lookup happens once per title.</summary>
        /// <param name="window">A graph window.</param>
        /// <returns>The loaded graph — the same instance the window edits — or null.</returns>
        private static Graph GraphOf(EditorWindow window)
        {
            string title = window.titleContent.text;
            if (string.IsNullOrEmpty(title))
                return null;

            if (!s_PathByTitle.TryGetValue(title, out string path))
            {
                path = null;
                foreach (string candidate in AssetDatabase.GetAllAssetPaths())
                {
                    if (!candidate.EndsWith("." + TaskGraph.Extension, StringComparison.OrdinalIgnoreCase)
                        && !candidate.EndsWith("." + StateTreeGraph.Extension, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(candidate), title,
                        StringComparison.Ordinal))
                        continue;
                    path = candidate;
                    break;
                }
                s_PathByTitle[title] = path;
            }
            if (string.IsNullOrEmpty(path))
                return null;

            return path.EndsWith("." + TaskGraph.Extension, StringComparison.OrdinalIgnoreCase)
                ? (Graph)GraphDatabase.LoadGraph<TaskGraph>(path)
                : GraphDatabase.LoadGraph<StateTreeGraph>(path);
        }

        /// <summary>Window title → graph path, so the asset scan happens once per graph rather
        /// than four times a second. A title that matches nothing is remembered as nothing.</summary>
        private static readonly Dictionary<string, string> s_PathByTitle =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Reconcile every dropdown under one element.</summary>
        /// <param name="element">Root to walk.</param>
        private static void Sweep(VisualElement element)
        {
            if (element is DropdownField dropdown)
                Reconcile(dropdown);

            foreach (VisualElement child in element.Children())
                Sweep(child);
        }

        /// <summary>
        /// Give one dropdown the list its port actually carries.
        /// </summary>
        /// <param name="dropdown">The widget on screen.</param>
        private static void Reconcile(DropdownField dropdown)
        {
            IReadOnlyList<string> offered = OfferedFor(dropdown);
            if (offered == null || offered.Count == 0)
                return;

            // BEFORE the up-to-date check: a settled graph is the common case, and a handler
            // attached only when the list changes would never attach at all.
            AttachPicker(dropdown);

            List<string> shown = dropdown.choices;
            if (shown != null && Same(shown, offered))
                return;

            dropdown.choices = new List<string>(offered);

            // A ROW THE NEW LIST DOES NOT OFFER IS A LEFTOVER from the registry the author just
            // switched away from. It cannot be picked again, it is not a value in the new catalog,
            // and leaving it makes the node bake to "which 'M21Levels' has no row for" — a
            // complaint about a choice nobody made.
            //
            // Cleared THROUGH THE WIDGET, with notify, on purpose: that is the same path a person
            // picking the empty row takes, so Graph Toolkit writes the port itself and the model
            // and the screen cannot end up disagreeing. Writing the model directly from here is
            // what made an earlier version show a row the graph did not hold.
            //
            // It converges: the empty choice is always first, so the next sweep finds a value the
            // list offers and does nothing.
            if (Contains(offered, dropdown.value))
                return;

            dropdown.value = offered[0];
        }

        /// <summary>
        /// Make a pin's dropdown open the PROJECT'S picker instead of the native menu.
        ///
        /// WHY. A native dropdown is a flat strip of names — the same limitation that made the
        /// Inspector's registry field worth replacing, and worse here because a canvas has no room
        /// to say what a row is. The picker gives search, the rows' own group paths as collapsible
        /// categories, a description line and the registry each row came from. Choosing a row on
        /// the canvas then works exactly like choosing one in the Inspector, which is the point.
        ///
        /// HOW. The click is intercepted before the field's own menu opens (trickle-down, and the
        /// event is stopped), and the pick is written back through <c>value</c> WITH notify — the
        /// same path a native selection takes, so Graph Toolkit stores it and nothing here has to
        /// know how a port is written.
        ///
        /// Registered once per widget: the sweep runs four times a second and a handler added on
        /// every pass would open a stack of pickers on the first click.
        /// </summary>
        /// <param name="dropdown">A pin's dropdown that this system supplied the choices for.</param>
        private static void AttachPicker(DropdownField dropdown)
        {
            if (!s_PickerAttached.Add(dropdown))
                return;

            EditorWindow host = s_Host;
            Graph graph = s_Graph;

            dropdown.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopImmediatePropagation();
                evt.PreventDefault();

                // A SECOND CLICK DISMISSES, the way any dropdown does. Without this the picker
                // closes on losing focus and is immediately reopened by this handler, which reads
                // as the window jumping rather than as a toggle.
                if (StateTreeNodePicker.isOpen)
                {
                    StateTreeNodePicker.CloseOpen();
                    return;
                }

                List<string> choices = dropdown.choices;
                if (choices == null || choices.Count == 0)
                    return;

                StateTreeNodePicker.ShowItems(
                    StateTreeNodePicker.ScreenRectOf(dropdown, host),
                    RegistryPickerItems.For(choices, graph),
                    payload => dropdown.value = payload as string ?? string.Empty,
                    "Pick Row", "GraphRow");
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Widgets whose click is already ours — see <see cref="AttachPicker"/>. Keyed by
        /// the element, and swept of dead entries when it grows, because a graph reopened many
        /// times would otherwise accumulate them.</summary>
        private static readonly HashSet<DropdownField> s_PickerAttached =
            new HashSet<DropdownField>();

        /// <summary>The choices the port behind this widget carries, or null when the widget is
        /// not a port editor this system owns.</summary>
        private static IReadOnlyList<string> OfferedFor(DropdownField dropdown)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic
                | BindingFlags.Public;

            // The port element is a few levels up; the walk is bounded so an unexpected hierarchy
            // costs nothing.
            VisualElement walk = dropdown.parent;
            for (int hop = 0; hop < 5 && walk != null; hop++, walk = walk.parent)
            {
                PropertyInfo portModel = walk.GetType().GetProperty("PortModel", any);
                object model = portModel?.GetValue(walk);
                if (model == null)
                    continue;

                return PortChoices.OfferedByModel(model);
            }
            return null;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}

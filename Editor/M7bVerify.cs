using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M7b exit criterion, the half a machine can check: the State Tree Editor opens a real
    /// <see cref="StateTreeAsset"/> and renders the states and wiring that are actually in it.
    ///
    /// WHAT M7b CORRECTED. M7 shipped a Graph Toolkit frontend that authors a separate graph
    /// document and BAKES it into the runtime tree. That indirection was the mistake: what you
    /// edit is not what runs, every change costs a bake, and a tree that did not come from a graph
    /// — a preset, an Inspector-built tree, anything a designer already has — has no editor at
    /// all. The direct editor edits the runtime asset itself, so "the exact states" and "the thing
    /// on the runner" are one file. The graph frontend stays as an optional visualisation, still
    /// reachable through its own menus; nothing here touches it.
    ///
    /// WHAT THIS COMMAND PROVES (Tools ▸ Draw To Play ▸ Verify M7b Direct Editor):
    /// <list type="number">
    /// <item>the Zombie preset exists (building it first if it does not) — five states with the
    /// transition graph <c>StateTreePresets.BuildZombie</c> documents;</item>
    /// <item>every transition in it RESOLVES: its <c>targetNodeId</c> names a node that is really
    /// in the tree, so no wiring row can be pointing at a state that was renamed or deleted;</item>
    /// <item>the editor window, opened on that asset, renders a row for each of the five states —
    /// read out of the window's own visual tree, not out of the model it was handed.</item>
    /// </list>
    /// Failures are logged with the specific state or transition at fault; nothing is mutated
    /// except the preset, which is rebuilt only when it is missing.
    ///
    /// WHAT IT CANNOT PROVE, AND HOW YOU CHECK IT BY HAND. That the editor writes back into the
    /// asset the runner runs is a play-mode observation, so it is a manual acceptance step:
    /// <list type="number">
    /// <item>Run <b>Tools ▸ Draw To Play ▸ Verify M6 State Trees</b> once. It builds
    /// <c>Demo/M6AIDemo.unity</c> and (re)builds the Zombie preset the scene points at.</item>
    /// <item>Open <c>Assets/DrawToPlay/Presets/Zombie.asset</c> in the State Tree Editor —
    /// double-click it, or select the zombie in the scene and click the <b>AI</b> tab in the Flow
    /// window.</item>
    /// <item>Select the <c>chase</c> state ("Pursue"). Its first transition is the interrupt to
    /// <c>windup</c> with <i>Check While Running</i> on, carrying a
    /// <see cref="TargetInRangeCondition"/> whose <c>range</c> is <b>0.6875</b> — zombie.gd's
    /// 22 px reach ÷ 32. Change it to <b>3</b>. (The strike's own reach is the
    /// <see cref="AttackTask"/> <c>range</c> on the <c>slash</c> state, next to it.)</item>
    /// <item>Run <b>Tools ▸ Draw To Play ▸ Play M6 Demo Scene</b>. Use <i>Play</i>, NOT
    /// <i>Verify M6</i> — verify rebuilds the preset from code and would throw the edit away.</item>
    /// <item>The zombie now plants and telegraphs its swing from three units out instead of
    /// closing to arm's length. Nothing was baked, nothing was exported, and there is no second
    /// asset: the file you edited is the file the runner deep-copies on StartTree. Stop, set it
    /// back to 0.6875, Play again, and it closes in again.</item>
    /// </list>
    /// One honest caveat on "immediately": the runner deep-copies the tree in StartTree, so an
    /// edit lands on the next Play (or the next <c>StartTree</c>), not retroactively into a fight
    /// already in progress. That copy is what stops two runners sharing one task's timer state,
    /// and it is M6 runtime behaviour this milestone deliberately did not touch.
    /// </summary>
    internal static class M7bVerify
    {
        private const string k_ZombiePresetPath = "Assets/DrawToPlay/Presets/Zombie.asset";

        /// <summary>zombie.gd's five ported states (<c>StateTreePresets.BuildZombie</c>): idle,
        /// chase, windup, slash, recover. Hard-coded because the number is the assertion — a
        /// window that renders "however many the model has" proves nothing.</summary>
        private const int k_ExpectedStateCount = 5;

        /// <summary>How many editor ticks to give the window before reading its visual tree.
        /// CreateGUI runs when the window is first shown and UI Toolkit work can be scheduled a
        /// frame later, so the scan retries rather than racing it.</summary>
        private const int k_MaxUiSettleTicks = 20;

        /// <summary>The label the inspector puts on a transition's target picker. Matched literally
        /// because it is the label a USER reads: if it is ever renamed, the row count drops to zero
        /// and this command fails loudly rather than passing on an assumption.</summary>
        private const string k_TargetFieldLabel = "Target";

        /// <summary>What the picker shows for a transition with no target at all.</summary>
        private const string k_NoTargetLabel = "<none>";

        [MenuItem("Tools/Draw To Play/Verify M7b Direct Editor")]
        public static void Verify()
        {
            var report = new StringBuilder();
            var failures = new List<string>();

            var tree = LoadOrBuildZombie(report);
            if (tree == null)
            {
                Debug.LogError("[M7b] FAIL — the Zombie preset could not be loaded or built. " +
                               "Run Tools ▸ Draw To Play ▸ Create Enemy Preset Trees and look at " +
                               "the Console for why it failed.");
                return;
            }

            var states = CheckStates(tree, report, failures);
            CheckWiring(tree, states, report, failures);

            // The window is opened even when the model checks already failed: seeing what it does
            // render with a broken tree is more use than not opening it.
            var window = StateTreeEditorWindow.Open(tree);

            ScanWindowWhenReady(window, tree, states, report, failures, 0);
        }

        // --- model checks ---------------------------------------------------------------------

        /// <summary>The Zombie preset, built first when it is absent. Rebuilding is deliberately
        /// NOT done when it is present: <c>AssetDatabase.CreateAsset</c> replaces the file, which
        /// would discard exactly the hand edit the manual acceptance step above asks you to
        /// make.</summary>
        private static StateTreeAsset LoadOrBuildZombie(StringBuilder report)
        {
            var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(k_ZombiePresetPath);
            if (tree != null)
            {
                report.AppendLine($"  preset      : loaded {k_ZombiePresetPath} (left as it is — a " +
                                  "rebuild would discard hand edits).");
                return tree;
            }

            report.AppendLine($"  preset      : {k_ZombiePresetPath} was missing — built it.");
            StateTreePresets.BuildZombie();
            return AssetDatabase.LoadAssetAtPath<StateTreeAsset>(k_ZombiePresetPath);
        }

        /// <summary>The tree's states, in authored order. Empty when the tree has no usable root,
        /// which is itself recorded as a failure.</summary>
        private static List<StateTreeNodeAsset> CheckStates(StateTreeAsset tree, StringBuilder report,
            List<string> failures)
        {
            var states = new List<StateTreeNodeAsset>();

            if (tree.root == null)
            {
                failures.Add("the tree has no root node.");
                report.AppendLine("  states      : none — root is null.");
                return states;
            }

            for (var i = 0; i < tree.root.children.Count; ++i)
            {
                var child = tree.root.children[i];
                if (child == null)
                {
                    failures.Add($"root child {i} is a null reference (a sub-asset was deleted " +
                                 "without the list being cleared).");
                    continue;
                }

                states.Add(child);
            }

            if (states.Count != k_ExpectedStateCount)
            {
                failures.Add($"expected {k_ExpectedStateCount} states under the root, found " +
                             $"{states.Count}.");
            }

            report.AppendLine($"  states      : {states.Count}/{k_ExpectedStateCount} — " +
                              $"{DescribeStates(states)}");
            return states;
        }

        /// <summary>Every transition on every state has to name a node that is in the tree. This is
        /// what "the wiring rows resolve their targets" means at the model level: a row whose
        /// target does not resolve is a transition the runner logs an error on and refuses to
        /// take, and it is the one fault a rename or a delete can introduce silently.</summary>
        private static void CheckWiring(StateTreeAsset tree, List<StateTreeNodeAsset> states,
            StringBuilder report, List<string> failures)
        {
            var index = BuildIdIndex(tree);
            var total = 0;
            var resolved = 0;

            for (var i = 0; i < states.Count; ++i)
            {
                var state = states[i];
                for (var t = 0; t < state.transitions.Count; ++t)
                {
                    ++total;
                    var transition = state.transitions[t];
                    if (transition == null)
                    {
                        failures.Add($"'{state.nodeId}' transition {t} is null.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(transition.targetNodeId))
                    {
                        failures.Add($"'{state.nodeId}' transition {t} has an empty target.");
                        continue;
                    }

                    if (!index.Contains(transition.targetNodeId))
                    {
                        failures.Add($"'{state.nodeId}' transition {t} targets " +
                                     $"'{transition.targetNodeId}', which is not a node in this " +
                                     "tree.");
                        continue;
                    }

                    ++resolved;
                }
            }

            if (total == 0)
                failures.Add("the tree has no transitions at all.");

            report.AppendLine($"  wiring      : {resolved}/{total} transitions resolve their " +
                              $"target. {DescribeWiring(states)}");
        }

        /// <summary>The set of ids a transition may legally name — every node in the tree, root
        /// included, which is exactly what <c>StateTreeRunner.BuildIndex</c> registers. The walk is
        /// <see cref="StateTreeEditorOps.CollectNodes"/> so this check and the editor's own target
        /// dropdown are looking at the same set.</summary>
        private static HashSet<string> BuildIdIndex(StateTreeAsset tree)
        {
            var index = new HashSet<string>();
            var nodes = StateTreeEditorOps.CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                if (!string.IsNullOrEmpty(nodes[i].nodeId))
                    index.Add(nodes[i].nodeId);
            }

            return index;
        }

        // --- window check ---------------------------------------------------------------------

        /// <summary>Read the window's OWN visual tree once it has one. Retried across editor ticks
        /// rather than read straight after <c>Open</c>, and the thing waited on is the CONTENT, not
        /// merely a non-empty root: a <c>TreeView</c> is virtualized, so its rows are bound during
        /// the panel's layout pass and not by <c>CreateGUI</c>. Reading in the frame the window was
        /// created would report "it drew nothing" about a window that simply had not been laid out
        /// yet — a race that fails on a cold open and passes on a warm one, which is the worst kind
        /// of check to have. Each retry asks for a repaint so the wait is not passive.</summary>
        private static void ScanWindowWhenReady(StateTreeEditorWindow window, StateTreeAsset tree,
            List<StateTreeNodeAsset> states, StringBuilder report, List<string> failures, int tick)
        {
            var root = window != null ? window.rootVisualElement : null;
            var settled = root != null && root.childCount > 0 && AllStatesDrawn(root, states);

            if (!settled && tick < k_MaxUiSettleTicks)
            {
                if (window != null)
                    window.Repaint();

                EditorApplication.delayCall += () =>
                    ScanWindowWhenReady(window, tree, states, report, failures, tick + 1);
                return;
            }

            if (root == null || root.childCount == 0)
            {
                failures.Add($"the State Tree Editor built no UI within {k_MaxUiSettleTicks} " +
                             "editor ticks of being opened.");
                report.AppendLine("  window      : empty.");
                Finish(tree, report, failures);
                return;
            }

            if (window.treeAsset != tree)
            {
                var showing = window.treeAsset != null ? window.treeAsset.name : "nothing";
                failures.Add($"the window is showing '{showing}' rather than the asset it was " +
                             "opened on.");
            }

            CheckRenderedStates(window, root, states, report, failures);
            CheckWiringRows(window, root, tree, states, report, failures);

            Finish(tree, report, failures);
        }

        /// <summary>Has every state's row actually been drawn yet? The settle condition, and the
        /// same question <see cref="CheckRenderedStates"/> then reports on in detail.</summary>
        private static bool AllStatesDrawn(VisualElement root, List<StateTreeNodeAsset> states)
        {
            if (states.Count == 0)
                return true;

            var text = new List<string>();
            Harvest(root, text);

            for (var i = 0; i < states.Count; ++i)
            {
                if (!Mentions(text, StateTreeInspectorPane.FormatNode(states[i])))
                    return false;
            }

            return true;
        }

        /// <summary>Two questions, deliberately kept apart. <c>visibleNodeIds</c> is the window
        /// saying which states it put in its tree view; the text harvest is the visual tree saying
        /// what a user would actually read. A row that exists in the first list and not in the
        /// second is a row that failed to draw, which is precisely the failure a model-level check
        /// cannot see.</summary>
        private static void CheckRenderedStates(StateTreeEditorWindow window, VisualElement root,
            List<StateTreeNodeAsset> states, StringBuilder report, List<string> failures)
        {
            var visible = window.visibleNodeIds;
            var text = new List<string>();
            Harvest(root, text);

            var unlisted = new List<string>();
            var undrawn = new List<string>();

            for (var i = 0; i < states.Count; ++i)
            {
                var state = states[i];
                if (!ListContains(visible, state.nodeId))
                    unlisted.Add(state.nodeId);

                if (!Mentions(text, StateTreeInspectorPane.FormatNode(state)))
                    undrawn.Add(state.nodeId);
            }

            if (unlisted.Count > 0)
                failures.Add($"the tree view lists no row for: {string.Join(", ", unlisted)}.");

            if (undrawn.Count > 0)
            {
                failures.Add($"the window draws no label for: {string.Join(", ", undrawn)} " +
                             $"(it shows {text.Count} pieces of text).");
            }

            report.AppendLine($"  window      : {states.Count - unlisted.Count}/{states.Count} " +
                              $"states listed, {states.Count - undrawn.Count}/{states.Count} drawn; " +
                              $"{visible.Count} row(s) in the tree view including the root.");
        }

        /// <summary>Select each state in turn and read its inspector's Target rows back. This is
        /// "the wiring rows resolve their targets" asked of the UI rather than of the model: the
        /// pane renders an unresolvable target as <c>&lt;missing: id&gt;</c>, and a row per
        /// transition is what the state's transition list is supposed to produce, so a count
        /// mismatch catches a row that silently did not render at all.
        ///
        /// The expected label comes from <see cref="StateTreeInspectorPane.FormatNode"/> — the
        /// same formatter the pane uses — so this asserts that the row points at the state the
        /// model says it does, not merely that it says something.</summary>
        private static void CheckWiringRows(StateTreeEditorWindow window, VisualElement root,
            StateTreeAsset tree, List<StateTreeNodeAsset> states, StringBuilder report,
            List<string> failures)
        {
            var nodes = StateTreeEditorOps.CollectNodes(tree);
            var expectedTotal = 0;
            var matched = 0;
            var drawn = 0;

            for (var i = 0; i < states.Count; ++i)
            {
                var state = states[i];
                expectedTotal += state.transitions.Count;

                if (!window.SelectNode(state.nodeId))
                {
                    failures.Add($"the window could not select '{state.nodeId}'.");
                    continue;
                }

                var targets = new List<string>();
                HarvestLabeled(root, k_TargetFieldLabel, targets);
                drawn += targets.Count;

                if (targets.Count != state.transitions.Count)
                {
                    failures.Add($"'{state.nodeId}' has {state.transitions.Count} transition(s) " +
                                 $"but its inspector drew {targets.Count} \"{k_TargetFieldLabel}\" " +
                                 "row(s).");
                    continue;
                }

                for (var t = 0; t < targets.Count; ++t)
                {
                    var expected = ExpectedTargetLabel(nodes, state.transitions[t]);
                    if (string.Equals(targets[t], expected, System.StringComparison.Ordinal))
                    {
                        ++matched;
                        continue;
                    }

                    failures.Add($"'{state.nodeId}' transition {t}: the Target row reads " +
                                 $"\"{targets[t]}\", expected \"{expected}\".");
                }
            }

            report.AppendLine($"  wiring rows : {matched}/{expectedTotal} resolve to the state they " +
                              $"name ({drawn} row(s) drawn across {states.Count} states).");
        }

        /// <summary>What the pane should be showing for a transition: the target state formatted
        /// the pane's own way, or its "no target" / "missing" placeholder.</summary>
        private static string ExpectedTargetLabel(List<StateTreeNodeAsset> nodes,
            StateTreeTransition transition)
        {
            if (transition == null || string.IsNullOrEmpty(transition.targetNodeId))
                return k_NoTargetLabel;

            for (var i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i].nodeId == transition.targetNodeId)
                    return StateTreeInspectorPane.FormatNode(nodes[i]);
            }

            return $"<missing: {transition.targetNodeId}>";
        }

        /// <summary>Collect every piece of text the window is showing: <c>TextElement.text</c>
        /// (labels, buttons, foldout headers) and the current value and label of any string-valued
        /// field (dropdowns, text fields). Both are long-stable UI Toolkit surface, which is the
        /// point — this must not couple to the window's private element names, or it would only
        /// verify that two files agree with each other.</summary>
        private static void Harvest(VisualElement element, List<string> text)
        {
            if (element == null)
                return;

            if (element is TextElement textElement && !string.IsNullOrEmpty(textElement.text))
                text.Add(textElement.text);

            if (element is BaseField<string> stringField)
            {
                if (!string.IsNullOrEmpty(stringField.value))
                    text.Add(stringField.value);
                if (!string.IsNullOrEmpty(stringField.label))
                    text.Add(stringField.label);
            }

            for (var i = 0; i < element.hierarchy.childCount; ++i)
                Harvest(element.hierarchy[i], text);
        }

        /// <summary>Values of every string-valued field carrying <paramref name="label"/>, in
        /// document order — which is the order the inspector builds its transition rows in.</summary>
        private static void HarvestLabeled(VisualElement element, string label, List<string> values)
        {
            if (element == null)
                return;

            if (element is BaseField<string> stringField &&
                string.Equals(stringField.label, label, System.StringComparison.Ordinal))
                values.Add(stringField.value ?? string.Empty);

            for (var i = 0; i < element.hierarchy.childCount; ++i)
                HarvestLabeled(element.hierarchy[i], label, values);
        }

        private static bool ListContains(IReadOnlyList<string> list, string needle)
        {
            if (list == null)
                return false;

            for (var i = 0; i < list.Count; ++i)
            {
                if (string.Equals(list[i], needle, System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool Mentions(List<string> text, string needle)
        {
            if (string.IsNullOrEmpty(needle))
                return false;

            for (var i = 0; i < text.Count; ++i)
            {
                if (text[i] != null && text[i].Contains(needle))
                    return true;
            }

            return false;
        }

        // --- reporting ------------------------------------------------------------------------

        private static void Finish(StateTreeAsset tree, StringBuilder report, List<string> failures)
        {
            var header = failures.Count == 0 ? "PASS" : "FAIL";
            var message = new StringBuilder();
            message.AppendLine($"[M7b] {header} — direct State Tree Editor over " +
                               $"'{tree.name}' ({k_ZombiePresetPath}).");
            message.Append(report);

            if (failures.Count > 0)
            {
                message.AppendLine($"  failures    : {failures.Count}");
                for (var i = 0; i < failures.Count; ++i)
                    message.AppendLine($"    - {failures[i]}");
            }

            message.Append("  manual step : edit the chase→windup TargetInRangeCondition range " +
                           "(0.6875 → 3) in the window, then Tools ▸ Draw To Play ▸ Play M6 Demo " +
                           "Scene — NOT Verify M6, which rebuilds the preset. The zombie should " +
                           "swing from three units out. No bake; same asset.");

            if (failures.Count == 0)
                Debug.Log(message.ToString(), tree);
            else
                Debug.LogError(message.ToString(), tree);
        }

        private static string DescribeStates(List<StateTreeNodeAsset> states)
        {
            if (states.Count == 0)
                return "(none)";

            var builder = new StringBuilder();
            for (var i = 0; i < states.Count; ++i)
            {
                if (builder.Length > 0)
                    builder.Append(", ");
                builder.Append(states[i].nodeId);
            }

            return builder.ToString();
        }

        private static string DescribeWiring(List<StateTreeNodeAsset> states)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < states.Count; ++i)
            {
                var state = states[i];
                for (var t = 0; t < state.transitions.Count; ++t)
                {
                    var transition = state.transitions[t];
                    if (transition == null)
                        continue;

                    if (builder.Length > 0)
                        builder.Append(", ");

                    builder.Append(state.nodeId).Append("→").Append(transition.targetNodeId);
                    if (transition.checkWhileRunning)
                        builder.Append("!");
                    if (transition.condition == null)
                        builder.Append("(any)");
                }
            }

            return builder.Length > 0 ? $"[{builder} — ! = interrupt]" : "[none]";
        }
    }
}

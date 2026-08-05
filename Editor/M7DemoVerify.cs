using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M7 exit-criterion pass: "rebuild the zombie preset entirely in the graph editor with zero
    /// code, save as SO, run it" (draw-tool-port-brief.md §8 M7).
    ///
    /// The criterion is a HUMAN action — you rebuild the tree by hand — and this command exists
    /// to prove the machinery underneath it works before you spend an afternoon on the canvas.
    /// It walks the whole path once, unattended:
    ///
    ///   1. GRAPH — creates Presets/ZombieGraph.&lt;ext&gt; if it is not there, and fills it with
    ///      the same five states, two interrupts and ported timings that
    ///      <see cref="StateTreePresets.BuildZombie"/> builds in code.
    ///   2. BAKE — runs the frontend's bake step, producing the flat runtime
    ///      <see cref="StateTreeAsset"/> the runner reads (§7.3's hard boundary).
    ///   3. RUN — copies the M6 demo scene and re-points its zombie at the BAKED asset. Same
    ///      scene, same zombie, same fight; the only difference is where the tree came from.
    ///      If it behaves identically to <see cref="M6DemoVerify"/>'s scene, the frontend is
    ///      faithful, and that comparison is the point of reusing the scene rather than
    ///      rebuilding one.
    ///
    /// IF A GRAPH ALREADY EXISTS AT THE PATH, THIS NEVER TOUCHES IT. Step 1 only authors into an
    /// EMPTY graph. Rebuild the zombie by hand, run this, and it bakes and runs YOUR graph —
    /// which is the exit criterion end to end, with the programmatic build reduced to what it is
    /// for: a fallback so the pipeline can be tested without you.
    ///
    /// WHY EVERYTHING GOES THROUGH <see cref="StateTreeGraphBridge"/>. This assembly does not
    /// reference the graph frontend or Graph Toolkit; §7.3 makes that split a hard boundary and
    /// §8's risk list expects the experimental API to churn. So the frontend is resolved by name
    /// at runtime and every failure is a console error naming the exact missing symbol, while
    /// M0-M6 keep working. See the bridge's class comment for the full reasoning — including why
    /// step 1 is best-effort: in 0.4.0-exp.2 the public API can read a graph but not write one.
    /// </summary>
    internal static class M7DemoVerify
    {
        private const string k_PresetFolder = "Assets/DrawToPlay/Presets";
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";

        /// <summary>The graph. Sits with the preset trees because it IS the zombie preset,
        /// authored the M7 way — the two are meant to be read side by side.</summary>
        private const string k_GraphName = "ZombieGraph";

        /// <summary>The standalone export path (m7 conventions: "alongside the graph asset, name
        /// + _Baked.asset"). Only used as a fallback: when the frontend bakes on IMPORT the tree
        /// is the graph file's own main asset and no second file exists — which is the better
        /// arrangement, since a graph and its tree can then never drift apart.</summary>
        private const string k_BakedPath = k_PresetFolder + "/" + k_GraphName + "_Baked.asset";

        private const string k_ScenePath = k_DemoFolder + "/M7GraphDemo.unity";

        /// <summary>The M6 scene, duplicated rather than rebuilt. Literal rather than a reference
        /// because <see cref="M6DemoVerify"/>'s paths are private, and prying them open to share
        /// one string would couple two demos that are otherwise independent.</summary>
        private const string k_M6ScenePath = k_DemoFolder + "/M6AIDemo.unity";

        private const string k_DaggersPath = k_PresetFolder + "/Daggers.asset";

        // --- ported zombie constants -----------------------------------------------------------
        // The same numbers StateTreePresets.BuildZombie uses, written the same way (the Godot
        // export over the project's 32 px = 1 wu) so a diff between the two files is a diff
        // between the two FRONTENDS, not between two sets of magic numbers.

        private const float k_PixelsPerUnit = 32f;

        /// <summary>enemy_base.gd <c>sight_range</c> 130 px.</summary>
        private const float k_SightRange = 130f / k_PixelsPerUnit;

        /// <summary>zombie.gd <c>near_range</c> 22 px.</summary>
        private const float k_NearRange = 22f / k_PixelsPerUnit;

        /// <summary>Half the attack range: deliberately unreachable, so the chase can only be
        /// left through the interrupt and the Cancelled path runs every cycle.</summary>
        private const float k_StopRange = k_NearRange * 0.5f;

        /// <summary>enemy_base.gd <c>pursue(target, 40.0)</c>.</summary>
        private const float k_ChaseSpeed = 40f / k_PixelsPerUnit;

        private const float k_IdlePulse = 0.2f;
        private const float k_SlashWindup = 0.4f;
        private const float k_SlashActive = 0.15f;
        private const float k_RecoverTime = 0.6f;

        private const string k_TelegraphCue = "zombie_slash_telegraph";

        // --- shape pins ------------------------------------------------------------------------
        // The frontend's class and port names, in ONE place. Everything else in this file is
        // written against these.
        //
        // The first entry of each list is the name the frontend actually ships (read off
        // GraphEditor/StateTreeFlowPorts.cs, StateNode.cs, TransitionNode.cs and EntryNode.cs);
        // the rest are alternates, so a rename on that side degrades to "resolved by the second
        // candidate" rather than to a failure. When NOTHING matches, the error prints every type
        // the frontend actually shipped and every port the node declares — a mismatch is a
        // one-line fix here rather than a hunt.
        //
        // Parameter names are not guessed at all. LibraryParameterPorts states the rule: "a
        // parameter port's name IS the field's name, byte for byte", so every name below is a
        // field of the matching M6 library class, verbatim.

        private static readonly string[] k_EntryNodeNames =
            { "EntryNode", "StateTreeEntryNode", "StartNode" };

        private static readonly string[] k_StateNodeNames =
            { "StateNode", "StateTreeStateNode", "StateContextNode" };

        private static readonly string[] k_TransitionNodeNames =
            { "TransitionNode", "StateTransitionNode", "StateTreeTransitionNode" };

        /// <summary>State identity (StateNode.NodeIdPortName): a string input port, not an
        /// option. Node options cannot be written programmatically in this Graph Toolkit, which
        /// is exactly why the frontend puts every authored value on a port.</summary>
        private const string k_OptionNodeId = "nodeId";

        private const string k_OptionDisplayName = "displayName";

        /// <summary>TransitionNode.CheckWhileRunningPortName — a bool input port carrying
        /// <see cref="StateTreeTransition.checkWhileRunning"/>.</summary>
        private const string k_OptionCheckWhileRunning = "checkWhileRunning";

        /// <summary>TransitionNode.OrderPortName — authoring-only ascending sort key deciding
        /// which of a state's transitions is evaluated first.</summary>
        private const string k_OptionOrder = "order";

        // Flow ports. Untyped by design on the frontend's side (an untyped input takes many wires,
        // a typed one takes exactly one — a state has to accept every transition that targets it),
        // so nothing about them is inferable from types and the names have to be right.
        private static readonly string[] k_StateInPortNames = { "in", "enter", "input" };

        private static readonly string[] k_StateOutPortNames = { "out", "exit", "output" };

        private static readonly string[] k_TransitionInPortNames = { "from", "in", "source" };

        private static readonly string[] k_TransitionOutPortNames = { "to", "out", "target" };

        /// <summary>The entry node's single output (EntryNode.EntryPortName).</summary>
        private static readonly string[] k_EntryOutPortNames = { "entry", "out", "to" };

        /// <summary>The transition's condition slot, typed
        /// <see cref="StateTreeConditionAsset"/> so only a condition node fits it.</summary>
        private static readonly string[] k_ConditionInPortNames = { "condition", "gate" };

        /// <summary>The condition node's output, typed with the concrete condition class.</summary>
        private static readonly string[] k_ConditionOutPortNames = { "condition", "out", "result" };

        // --- menus -------------------------------------------------------------------------

        [MenuItem("Tools/Draw To Play/Verify M7 Graph Frontend")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!StateTreeGraphBridge.TryResolve(out var resolveError))
            {
                Debug.LogError($"[M7] {resolveError}");
                return;
            }

            StateTreeGraphBridge.EnsureFolder(k_PresetFolder);

            // The weapon first: creating it can write assets, and doing that in the middle of
            // building a graph would run an import over the very file being authored.
            WeaponDefAsset daggers = EnsureDaggers();

            var graphPath = $"{k_PresetFolder}/{k_GraphName}.{StateTreeGraphBridge.extension}";
            var graph = EnsureGraph(graphPath, daggers, out var graphError);
            if (graph == null)
            {
                Debug.LogError($"[M7] {graphError}");
                return;
            }

            var baked = ResolveBakedTree(graph, graphPath, out var bakeError);
            if (baked == null)
            {
                Debug.LogError($"[M7] {bakeError}");
                StateTreeGraphBridge.OpenGraphAsset(graphPath);
                return;
            }

            if (!BuildScene(baked, out var sceneError))
            {
                Debug.LogError($"[M7] {sceneError}");
                return;
            }

            // Open the graph as well as the scene: the second half of the exit criterion is
            // "active state highlights during play", and the highlight is a tint on a node in
            // this window. Leaving it open is the difference between seeing it and being told
            // about it.
            StateTreeGraphBridge.OpenGraphAsset(graphPath);

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath));
            Debug.Log($"[M7] {graphPath} → {AssetDatabase.GetAssetPath(baked)} → {k_ScenePath}. " +
                      "Press Play (or Tools ▸ Draw To Play ▸ Play M7 Demo Scene): the zombie " +
                      "should fight exactly as it does in the M6 scene, from a tree that came " +
                      "out of the graph editor — and the state it is in should light up on the " +
                      "canvas as it goes.");
        }

        /// <summary>Open the demo scene and press Play. No start-scene redirection: the
        /// StartupScene hijack (and the counter-bypass every demo menu carried) is removed
        /// (user decision, 2026-08-05) — Play plays the open scene, always.</summary>
        [MenuItem("Tools/Draw To Play/Play M7 Demo Scene")]
        public static void PlayDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) == null)
            {
                Verify();
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) == null)
                    return;
            }
            else if (SceneManager.GetActiveScene().path != k_ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            }

            EditorApplication.EnterPlaymode();
        }


        // --- step 1: the graph -------------------------------------------------------------

        /// <summary>Load the graph at the path, creating it when absent, and author the zombie
        /// into it when (and only when) it is empty.</summary>
        private static object EnsureGraph(string graphPath, WeaponDefAsset daggers,
            out string error)
        {
            error = null;

            var existing = AssetDatabase.LoadMainAssetAtPath(graphPath) != null;
            var graph = existing
                ? StateTreeGraphBridge.LoadGraph(graphPath, out error)
                : StateTreeGraphBridge.CreateGraph(graphPath, out error);

            if (graph == null)
            {
                if (string.IsNullOrEmpty(error))
                    error = $"Could not {(existing ? "load" : "create")} the graph at {graphPath}.";
                return null;
            }

            var nodeCount = StateTreeGraphBridge.NodeCount(graph);
            if (nodeCount > 0)
            {
                Debug.Log($"[M7] {graphPath} already has {nodeCount} nodes — baking it as-is. " +
                          "Delete the asset if you want the built-in zombie back.");
                return graph;
            }

            var problems = new List<string>();
            if (!AuthorZombie(graph, daggers, problems))
            {
                StateTreeGraphBridge.OpenGraphAsset(graphPath);
                error = BuildAuthoringFailureMessage(graphPath, problems);
                return null;
            }

            StateTreeGraphBridge.SaveGraph(graph);
            AssetDatabase.SaveAssets();
            return graph;
        }

        /// <summary>
        /// The zombie, as a graph. Structurally identical to
        /// <see cref="StateTreePresets.BuildZombie"/> — read that method's comment for WHY the
        /// tree has this shape; this one only says how the shape is expressed in nodes:
        /// <code>
        ///   entry ─▶ idle ──(detect, interrupt)──▶ chase ──(in reach, interrupt)──▶ windup
        ///             ▲                              │                                │
        ///             │                              └──(target lost)──▶ idle         ▼
        ///             └──────────── recover ◀──────────── slash ◀──────────────────────┘
        /// </code>
        /// Each state is a context node holding its tasks as blocks; each arrow is a transition
        /// node carrying an optional condition node. Transition ORDER matters (the runner takes
        /// the first whose condition passes), and the order of these calls is that order —
        /// whether the baker can preserve it from a graph is the one thing this build exercises
        /// that a hand-authored graph would not think to check.
        /// </summary>
        private static bool AuthorZombie(object graph, WeaponDefAsset daggers,
            List<string> problems)
        {
            if (!StateTreeGraphBridge.Authoring.IsAvailable(graph, out var authoringError))
            {
                problems.Add(authoringError);
                return false;
            }

            // Only the state node is required. The baker accepts a graph with no entry marker
            // ("else the first state in creation order" — idle is created first here) and a
            // transition that is nothing but a condition node on the wire ("all three shapes bake
            // identically"), so neither of those classes existing is a reason to fail.
            var stateType = ResolveNodeType(k_StateNodeNames, "the state node", problems);
            var entryType = ResolveOptionalNodeType(k_EntryNodeNames);
            var transitionType = ResolveOptionalNodeType(k_TransitionNodeNames);

            var waitType = ResolveTaskType("Wait", problems);
            var chaseType = ResolveTaskType("ChaseTarget", problems);
            var faceType = ResolveTaskType("FaceTarget", problems);
            var cueType = ResolveTaskType("FireCue", problems);
            var attackType = ResolveTaskType("Attack", problems);
            var detectedType = ResolveConditionType("TargetDetected", problems);
            var inRangeType = ResolveConditionType("TargetInRange", problems);

            if (problems.Count > 0)
                return false;

            // Laid out left to right in the order the fight happens, one column per state, so a
            // graph nobody has arranged still reads as the cycle it is.
            var idle = AddState(graph, stateType, "idle", "Idle", new Vector2(0f, 0f), problems);
            var chase = AddState(graph, stateType, "chase", "Pursue", new Vector2(320f, 0f), problems);
            var windup = AddState(graph, stateType, "windup", "Slash Windup", new Vector2(640f, 0f), problems);
            var slash = AddState(graph, stateType, "slash", "Slash", new Vector2(960f, 0f), problems);
            var recover = AddState(graph, stateType, "recover", "Recover", new Vector2(1280f, 0f), problems);

            if (problems.Count > 0)
                return false;

            // Entry → idle, when the palette has an entry marker. Mirrors ResolveEntryNode landing
            // on children[0] in the built preset. Without one the baker falls back to the first
            // state in creation order, which is idle, so this is a nicety rather than a
            // requirement — and a failure to wire it must not sink the build.
            if (entryType != null)
            {
                var entry = StateTreeGraphBridge.Authoring.AddNode(graph, entryType,
                    new Vector2(-260f, 0f), out var entryError);
                if (entry == null)
                    Debug.LogWarning($"[M7] Entry node not created ({entryError}); the baker will " +
                                     "use the first state (idle) as the entry.");
                else
                    ConnectFlow(graph, entry, k_EntryOutPortNames, idle, k_StateInPortNames,
                        problems);
            }

            // IDLE — a heartbeat and the detection interrupt. The wait exists so the node has
            // something to be busy with; detection is checked every tick regardless.
            AddBlock(graph, idle, waitType, problems, (k_OptionSeconds, k_IdlePulse));
            AddTransition(graph, transitionType, idle, chase, detectedType, true, 10,
                new Vector2(160f, -180f), problems, (k_OptionDetectRange, k_SightRange));
            AddTransition(graph, transitionType, idle, idle, null, false, 20,
                new Vector2(160f, 180f), problems);

            // CHASE — pursue at 40 px/s. stopRange is half the reach, so the interrupt is the
            // only way out and the Cancelled path runs every cycle.
            AddBlock(graph, chase, chaseType, problems,
                (k_OptionMoveSpeed, k_ChaseSpeed), (k_OptionStopRange, k_StopRange));
            AddTransition(graph, transitionType, chase, windup, inRangeType, true, 10,
                new Vector2(480f, -180f), problems, (k_OptionRange, k_NearRange));
            AddTransition(graph, transitionType, chase, windup, inRangeType, false, 20,
                new Vector2(480f, 180f), problems, (k_OptionRange, k_NearRange));
            AddTransition(graph, transitionType, chase, idle, null, false, 30,
                new Vector2(480f, 320f), problems);

            // WINDUP — 0.4 s planted, facing the target, telegraphing. Godot pulses the body
            // tint here; a tint is a cue, so it ports as a cue emission.
            AddBlock(graph, windup, faceType, problems);
            AddBlock(graph, windup, cueType, problems, (k_OptionCueName, k_TelegraphCue));
            AddBlock(graph, windup, waitType, problems, (k_OptionSeconds, k_SlashWindup));
            AddTransition(graph, transitionType, windup, slash, null, false, 10,
                new Vector2(800f, 180f), problems);

            // SLASH — the hit lands on the first tick and the wait holds the state open for the
            // ported active window: tasks in a node run in PARALLEL.
            AddBlock(graph, slash, attackType, problems,
                (k_OptionWeapon, daggers), (k_OptionRange, k_NearRange));
            AddBlock(graph, slash, waitType, problems, (k_OptionSeconds, k_SlashActive));
            AddTransition(graph, transitionType, slash, recover, null, false, 10,
                new Vector2(1120f, 180f), problems);

            // RECOVER — then back to IDLE to re-decide, which is what lets a zombie whose target
            // vanished mid-recovery stand down instead of swinging at nothing.
            AddBlock(graph, recover, waitType, problems, (k_OptionSeconds, k_RecoverTime));
            AddTransition(graph, transitionType, recover, idle, null, false, 10,
                new Vector2(1440f, 320f), problems);

            return problems.Count == 0;
        }

        // Option names: the serialized field names of the M6 library components, verbatim
        // (Runtime/StateTree/Library/*.cs), because m7 conventions pin the node options to mirror
        // them exactly.
        private const string k_OptionSeconds = "seconds";
        private const string k_OptionMoveSpeed = "moveSpeed";
        private const string k_OptionStopRange = "stopRange";
        private const string k_OptionCueName = "cueName";
        private const string k_OptionWeapon = "weapon";
        private const string k_OptionRange = "range";
        private const string k_OptionDetectRange = "detectRange";

        /// <summary>A state: one context node carrying its identity.</summary>
        private static object AddState(object graph, Type stateType, string nodeId,
            string displayName, Vector2 position, List<string> problems)
        {
            var state = StateTreeGraphBridge.Authoring.AddNode(graph, stateType, position,
                out var error);
            if (state == null)
            {
                problems.Add(error);
                return null;
            }

            SetOption(state, k_OptionNodeId, nodeId, problems);
            SetOption(state, k_OptionDisplayName, displayName, problems);
            StateTreeGraphBridge.Authoring.DefineNode(state);
            return state;
        }

        /// <summary>A task: one block inside a state's context. Options are applied before
        /// DefineNode so an option that changes the node's topology has taken effect by the time
        /// anything reads its ports.</summary>
        private static void AddBlock(object graph, object state, Type blockType,
            List<string> problems, params (string name, object value)[] options)
        {
            if (state == null)
                return;

            var block = StateTreeGraphBridge.Authoring.AddBlock(graph, state, blockType,
                out var error);
            if (block == null)
            {
                problems.Add(error);
                return;
            }

            for (var i = 0; i < options.Length; ++i)
                SetOption(block, options[i].name, options[i].value, problems);

            StateTreeGraphBridge.Authoring.DefineNode(block);
        }

        /// <summary>
        /// An arrow. <paramref name="checkWhileRunning"/> true is an INTERRUPT — re-evaluated
        /// every tick before the state's tasks, so firing it cancels them (OnExit with
        /// Cancelled). False is a completion transition, taken only once every task in the state
        /// has finished.
        ///
        /// The baker accepts three shapes for this and bakes them identically: state → state
        /// (unconditional), state → condition → state, and state → transition node → state with
        /// the condition wired into the transition. This picks the richest one the palette
        /// actually shipped, which is what lets the command work against a frontend that chose to
        /// put <c>checkWhileRunning</c> on the condition node instead of on a transition node of
        /// its own.
        /// </summary>
        private static void AddTransition(object graph, Type transitionType, object from,
            object to, Type conditionType, bool checkWhileRunning, int order, Vector2 position,
            List<string> problems, params (string name, object value)[] conditionOptions)
        {
            if (from == null || to == null)
                return;

            object condition = null;
            if (conditionType != null)
            {
                condition = StateTreeGraphBridge.Authoring.AddNode(graph, conditionType,
                    position + new Vector2(-140f, 110f), out var conditionError);
                if (condition == null)
                {
                    problems.Add(conditionError);
                    return;
                }

                for (var i = 0; i < conditionOptions.Length; ++i)
                {
                    SetOption(condition, conditionOptions[i].name, conditionOptions[i].value,
                        problems);
                }

                StateTreeGraphBridge.Authoring.DefineNode(condition);
            }

            if (transitionType != null)
            {
                var transition = StateTreeGraphBridge.Authoring.AddNode(graph, transitionType,
                    position, out var error);
                if (transition == null)
                {
                    problems.Add(error);
                    return;
                }

                // Interrupt or not, state the intent: a transition node whose flag is left at its
                // default reads as "nobody decided" on the canvas.
                SetOptionIfPresent(transition, k_OptionCheckWhileRunning, checkWhileRunning,
                    checkWhileRunning ? problems : null);

                // Evaluation order, written down rather than left to the order the wires happened
                // to be drawn in. The runner takes the FIRST matching transition, so "in range →
                // attack" ahead of the unconditional "→ idle" is not a preference, it is the
                // behaviour. Authoring-only: it has no runtime field, and a palette without the
                // port still bakes correctly from wire order.
                SetOptionIfPresent(transition, k_OptionOrder, order, null);
                StateTreeGraphBridge.Authoring.DefineNode(transition);

                var flowIn = ConnectFlow(graph, from, k_StateOutPortNames, transition,
                    k_TransitionInPortNames, problems);
                ConnectFlow(graph, transition, k_TransitionOutPortNames, to, k_StateInPortNames,
                    problems);

                if (condition != null)
                {
                    // Any free input on the transition works: the baker scans every input of a
                    // transition node for a condition-typed upstream and skips the one the state
                    // wire arrived on. Excluding that port here only keeps the two wires from
                    // sharing a slot, where FirstConnectedPort could return the state instead.
                    ConnectFlow(graph, condition, k_ConditionOutPortNames, transition,
                        k_ConditionInPortNames, problems, flowIn);
                }

                return;
            }

            if (condition != null)
            {
                // No transition node in the palette: the condition sits on the wire and carries
                // the interrupt flag itself. Only an INTERRUPT needs the option to exist — a
                // completion transition is the absence of the flag.
                SetOptionIfPresent(condition, k_OptionCheckWhileRunning, checkWhileRunning,
                    checkWhileRunning ? problems : null);
                StateTreeGraphBridge.Authoring.DefineNode(condition);

                ConnectFlow(graph, from, k_StateOutPortNames, condition, k_ConditionInPortNames,
                    problems);
                ConnectFlow(graph, condition, k_ConditionOutPortNames, to, k_StateInPortNames,
                    problems);
                return;
            }

            ConnectFlow(graph, from, k_StateOutPortNames, to, k_StateInPortNames, problems);
        }

        private static void SetOption(object node, string name, object value, List<string> problems)
        {
            if (node == null)
                return;

            if (!StateTreeGraphBridge.Authoring.SetOption(node, name, value, out var error))
                problems.Add(error);
        }

        /// <summary>Set an option that the palette is allowed not to have.
        /// <paramref name="problems"/> null = the caller can live without it.</summary>
        private static void SetOptionIfPresent(object node, string name, object value,
            List<string> problems)
        {
            if (node == null)
                return;

            if (!StateTreeGraphBridge.Authoring.SetOption(node, name, value, out var error) &&
                problems != null)
                problems.Add(error);
        }

        /// <summary>Wire two nodes and return the input port used, so a caller wiring a second
        /// edge into the same node can avoid it.</summary>
        private static string ConnectFlow(object graph, object from, string[] fromCandidates,
            object to, string[] toCandidates, List<string> problems, string excludeInput = null)
        {
            if (from == null || to == null)
                return null;

            var fromPort = ResolvePortName(from, fromCandidates, true, null);
            var toPort = ResolvePortName(to, toCandidates, false, excludeInput);

            if (fromPort == null || toPort == null)
            {
                problems.Add(
                    $"Could not find a port to connect {from.GetType().Name} → " +
                    $"{to.GetType().Name}. Tried [{string.Join(", ", fromCandidates)}] on the " +
                    $"output side and [{string.Join(", ", toCandidates)}] on the input side. " +
                    $"Source declares {StateTreeGraphBridge.DescribePorts(from)}; destination " +
                    $"declares {StateTreeGraphBridge.DescribePorts(to)}.");
                return null;
            }

            if (!StateTreeGraphBridge.Authoring.Connect(graph, from, fromPort, to, toPort,
                    out var error))
                problems.Add(error);

            return toPort;
        }

        /// <summary>Pick a port by name, then by elimination. The candidate list says what this
        /// command MEANT; the positional fallback is what makes it survive a palette that spelled
        /// the same port differently, and it is safe here because the baker reads the graph by
        /// structure rather than by port name — any input on a transition node will do.</summary>
        private static string ResolvePortName(object node, string[] candidates, bool output,
            string exclude)
        {
            var names = StateTreeGraphBridge.PortNames(node, output);
            if (names.Count == 0)
                return null;

            for (var i = 0; i < candidates.Length; ++i)
            {
                if (names.Contains(candidates[i]) &&
                    !string.Equals(candidates[i], exclude, StringComparison.Ordinal))
                    return candidates[i];
            }

            for (var i = 0; i < names.Count; ++i)
            {
                if (!string.Equals(names[i], exclude, StringComparison.Ordinal))
                    return names[i];
            }

            return null;
        }

        // --- type resolution -----------------------------------------------------------------

        private static Type ResolveNodeType(string[] candidates, string role, List<string> problems)
        {
            var assembly = StateTreeGraphBridge.graphType != null
                ? StateTreeGraphBridge.graphType.Assembly
                : null;
            if (assembly == null)
            {
                problems.Add("The graph frontend assembly is not loaded.");
                return null;
            }

            for (var i = 0; i < candidates.Length; ++i)
            {
                var type = assembly.GetType(
                    $"{StateTreeGraphBridge.FrontendNamespace}.{candidates[i]}", false);
                if (type != null && !type.IsAbstract)
                    return type;
            }

            problems.Add($"Could not find {role}: tried " +
                         $"[{string.Join(", ", candidates)}] in {StateTreeGraphBridge.FrontendNamespace}. " +
                         $"The frontend ships: {StateTreeGraphBridge.DescribeFrontendTypes()}");
            return null;
        }

        /// <summary>A library task's block node. <paramref name="stem"/> is the library class
        /// name without its "Task" suffix (Wait, ChaseTarget, Attack…). "<c>{stem}TaskNode</c>"
        /// leads because that is the convention the baker documents and falls back on
        /// ("Name the block class after its task (ChaseTargetTaskNode → ChaseTargetTask)").</summary>
        private static Type ResolveTaskType(string stem, List<string> problems)
        {
            return ResolveNodeType(new[]
            {
                stem + "TaskNode", stem + "TaskBlock", stem + "Block", stem + "Node", stem + "Task"
            }, $"the {stem} task node", problems);
        }

        /// <summary>A library condition's node. <paramref name="stem"/> is the class name without
        /// its "Condition" suffix (TargetDetected, TargetInRange…).</summary>
        private static Type ResolveConditionType(string stem, List<string> problems)
        {
            return ResolveNodeType(new[]
            {
                stem + "ConditionNode", stem + "Condition", stem + "Node", stem + "ConditionBlock"
            }, $"the {stem} condition node", problems);
        }

        /// <summary>Type lookup that reports nothing when it misses — for the parts of the shape
        /// the baker treats as optional (the entry marker, the dedicated transition node).</summary>
        private static Type ResolveOptionalNodeType(string[] candidates)
        {
            var ignored = new List<string>();
            return ResolveNodeType(candidates, string.Empty, ignored);
        }

        private static string BuildAuthoringFailureMessage(string graphPath, List<string> problems)
        {
            var message =
                $"Could not build the zombie graph at {graphPath} programmatically. This does " +
                "NOT block the milestone: rebuild the zombie by hand in the graph window (which " +
                "is the M7 exit criterion), then run this command again — it bakes and runs an " +
                "existing graph untouched. What went wrong:";

            for (var i = 0; i < problems.Count; ++i)
                message += $"\n  • {problems[i]}";

            return message;
        }

        // --- step 2: the bake --------------------------------------------------------------

        /// <summary>
        /// Get the runtime tree out of the graph.
        ///
        /// The shipped route is the IMPORTER — a ScriptedImporter on the graph extension bakes on
        /// every import and makes the tree the graph file's main asset, the pattern both Graph
        /// Toolkit samples use for runtime data. So this re-imports (forced: the graph was just
        /// written, and a cached artifact would bake this morning's version) and reads the tree
        /// back off the file.
        ///
        /// The explicit-baker route is tried second, for a frontend that offers a Bake method
        /// returning the tree instead of — or as well as — importing. Either way the asset must
        /// end up IN the project, because a scene can only reference something on disk.
        /// </summary>
        private static StateTreeAsset ResolveBakedTree(object graph, string graphPath,
            out string error)
        {
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceUpdate);
            var imported = FindTreeAt(graphPath);
            if (imported != null)
            {
                error = null;
                return imported;
            }

            var baked = StateTreeGraphBridge.Bake(graph, k_BakedPath, out var bakeError);
            if (baked != null && AssetDatabase.Contains(baked))
            {
                error = null;
                return baked;
            }

            var standalone = FindTreeAt(k_BakedPath);
            if (standalone != null)
            {
                error = null;
                return standalone;
            }

            error = $"Importing {graphPath} produced no {nameof(StateTreeAsset)}, and no saved " +
                    $"tree was found at {k_BakedPath} either. The console above should carry the " +
                    "bake errors — every one of them is a fault in the graph, not in this " +
                    $"command. ({bakeError})";
            return null;
        }

        private static StateTreeAsset FindTreeAt(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                return null;

            var objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (var i = 0; i < objects.Length; ++i)
            {
                if (objects[i] is StateTreeAsset tree)
                    return tree;
            }

            return null;
        }

        // --- step 3: the scene -------------------------------------------------------------

        /// <summary>
        /// The M6 demo scene with one field changed.
        ///
        /// It is a COPY rather than a rebuild on purpose. The claim being tested is "a baked
        /// graph runs like the hand-built preset", and the only way to test that honestly is to
        /// change exactly one thing between the two runs. A second scene builder would drift
        /// from M6's the first time either was touched, and then a behaviour difference would
        /// mean nothing. Read <see cref="M6DemoVerify"/>'s class comment for what the scene
        /// contains and what to watch for — every word of it applies here.
        /// </summary>
        private static bool BuildScene(StateTreeAsset baked, out string error)
        {
            error = null;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_M6ScenePath) == null)
            {
                // Also (re)builds the M6 preset tree and demo assets. Only ever runs when the
                // scene is missing, so a customised M6 scene is never overwritten by an M7 run.
                M6DemoVerify.Verify();
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_M6ScenePath) == null)
            {
                error = $"The M6 demo scene ({k_M6ScenePath}) could not be built, and the M7 " +
                        "scene is a copy of it. Run Tools ▸ Draw To Play ▸ Verify M6 State Trees " +
                        "first.";
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) != null)
                AssetDatabase.DeleteAsset(k_ScenePath);

            if (!AssetDatabase.CopyAsset(k_M6ScenePath, k_ScenePath))
            {
                error = $"Could not copy {k_M6ScenePath} to {k_ScenePath}.";
                return false;
            }

            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            var runner = FindRunner(scene);
            if (runner == null)
            {
                error = $"{k_ScenePath} has no StateTreeRunner to re-point at the baked tree.";
                return false;
            }

            runner.data = baked;
            EditorUtility.SetDirty(runner);
            Selection.activeGameObject = runner.gameObject;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static StateTreeRunner FindRunner(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; ++i)
            {
                var runner = roots[i].GetComponentInChildren<StateTreeRunner>(true);
                if (runner != null)
                    return runner;
            }

            return null;
        }

        // --- assets ------------------------------------------------------------------------

        /// <summary>The daggers the slash swings. Shared with the M6 preset rather than
        /// duplicated: same weapon, same 1 damage, so the demo's arithmetic (3 HP, three hits)
        /// is the same too. <see cref="StateTreePresets.BuildZombie"/> creates it when absent and
        /// never overwrites it, so borrowing it this way cannot clobber a retuned def.</summary>
        private static WeaponDefAsset EnsureDaggers()
        {
            var daggers = AssetDatabase.LoadAssetAtPath<WeaponDefAsset>(k_DaggersPath);
            if (daggers != null)
                return daggers;

            StateTreePresets.BuildZombie();
            return AssetDatabase.LoadAssetAtPath<WeaponDefAsset>(k_DaggersPath);
        }
    }
}

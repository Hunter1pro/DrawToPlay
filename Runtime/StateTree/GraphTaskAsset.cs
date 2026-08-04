using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The instruction set of a <see cref="GraphTaskAsset"/> program. Two families, told apart by
    /// whether the node has exec pins:
    /// <list type="bullet">
    /// <item>EXEC nodes (<see cref="Branch"/>..<see cref="FireCue"/>) sit on the control-flow chain.
    /// <c>GraphTaskNode.exec</c> holds their out-pin targets as node indices, -1 = end of chain.</item>
    /// <item>DATA nodes (<see cref="ConstFloat"/>..<see cref="GetParamBool"/>) are never stepped: they
    /// are PULLED by whichever exec node names them in <c>GraphTaskNode.data</c>, and re-evaluated
    /// fresh on every pull (no caching — a blackboard read must see the write an earlier node in the
    /// same chain just made).</item>
    /// </list>
    /// Member ORDER is part of the cross-agent contract (the baker writes these as ints into a
    /// serialized asset): append new kinds at the end, never insert.
    /// </summary>
    public enum GraphTaskNodeKind
    {
        /// <summary>data[0] = bool; exec[0] = true branch, exec[1] = false branch.</summary>
        Branch,

        /// <summary>stringValue = key; data[0] = float source, or floatValue when unwired.</summary>
        SetBlackboardFloat,

        /// <summary>stringValue = key; data[0] = string source, or stringValue2 when unwired.</summary>
        SetBlackboardString,

        /// <summary>Ticks <c>task</c> on the parent context. LATENT — see
        /// <see cref="GraphTaskAsset"/>. exec[0] = after Success, exec[1] = after Failure.</summary>
        DoTask,

        /// <summary>LATENT hold for floatValue seconds (or data[0]); then exec[0].</summary>
        Wait,

        /// <summary>Terminate the tick with Success.</summary>
        ReturnSuccess,

        /// <summary>Terminate the tick with Failure.</summary>
        ReturnFailure,

        /// <summary>Terminate the tick with Running (the task stays alive, next tick restarts at
        /// <c>tickEntry</c>).</summary>
        ReturnRunning,

        /// <summary>stringValue = cue name, payload {"owner"}; then exec[0].</summary>
        FireCue,

        /// <summary>floatValue.</summary>
        ConstFloat,

        /// <summary>stringValue.</summary>
        ConstString,

        /// <summary>floatValue != 0.</summary>
        ConstBool,

        /// <summary>stringValue = key; missing key reads 0.</summary>
        GetBlackboardFloat,

        /// <summary>stringValue = key; missing key reads "".</summary>
        GetBlackboardString,

        /// <summary>stringValue = key; true when the blackboard holds it.</summary>
        HasBlackboardKey,

        /// <summary><c>condition.Evaluate(context)</c>; a null condition reads true.</summary>
        EvaluateCondition,

        /// <summary>data[0] op data[1], op from stringValue ("&lt;", "&lt;=", "&gt;", "&gt;=", "==",
        /// "!="); unwired rhs falls back to floatValue.</summary>
        CompareFloat,

        /// <summary>data[0] &amp;&amp; data[1].</summary>
        BoolAnd,

        /// <summary>data[0] || data[1].</summary>
        BoolOr,

        /// <summary>!data[0].</summary>
        BoolNot,

        /// <summary>Valid inside the OnExit chain: 0 Success, 1 Failure, 2 Cancelled, as a
        /// float.</summary>
        ExitStatus,

        // ---- appended in M7f (graph variables as overridable task parameters). New kinds go
        // BELOW this line, never between the ones above: the ints are already in baked assets.

        /// <summary>stringValue = parameter name; reads the override if the state that runs this
        /// graph set one, otherwise the graph's own default.</summary>
        GetParamFloat,

        /// <summary>stringValue = parameter name; the override-or-default string.</summary>
        GetParamString,

        /// <summary>stringValue = parameter name; the override-or-default bool (the parameter's
        /// float value != 0).</summary>
        GetParamBool
    }

    /// <summary>Value kinds a <see cref="GraphTaskParameter"/> can carry. Order is serialized —
    /// append only, like <see cref="GraphTaskNodeKind"/>.</summary>
    public enum GraphTaskParameterKind
    {
        Float,
        String,
        Bool
    }

    /// <summary>
    /// One authored variable of a logic graph, surfaced as a PARAMETER of the task that runs it.
    /// The graph carries the default; the state that uses the graph may override it
    /// (<see cref="GraphTaskParameterOverride"/>) — the Blueprint instance model, which is what
    /// makes one authored "chase" graph usable at three different speeds without three graphs.
    ///
    /// Flat record for the same reason <see cref="GraphTaskNode"/> is one: it serializes into the
    /// baked asset with no polymorphism. A Bool parameter rides in <see cref="floatValue"/>
    /// (!= 0), so the override rows need no third field and a variable retyped between Float and
    /// Bool keeps its value.
    /// </summary>
    [Serializable]
    public sealed class GraphTaskParameter
    {
        /// <summary>The graph variable's name — unique per graph, and the key both the
        /// <c>GetParam*</c> nodes and the override rows resolve against.</summary>
        public string name;

        public GraphTaskParameterKind kind;

        /// <summary>Default for Float, and for Bool (!= 0).</summary>
        public float floatValue;

        /// <summary>Default for String.</summary>
        public string stringValue;

        /// <summary>
        /// Stable IDENTITY of this declaration, and the ONLY thing an override row binds to (M7h).
        /// The <see cref="name"/> is a label the author retypes; the identity is what survives them
        /// doing it, so renaming "speed" to "moveSpeed" keeps every state that had tuned it rather
        /// than silently dropping them all back to the default — the failure mode name-keyed
        /// overrides have, and the reason this field exists.
        ///
        /// The name stays the RUNTIME key: the blackboard and the <c>GetParam*</c> nodes are read
        /// by hand-typed name, so the identity is an authoring-time concern that never appears in a
        /// running tree's data.
        ///
        /// Written ONCE, when the row is created — <c>System.Guid.NewGuid().ToString("N")</c> for a
        /// sub-tree declaration, the baker's per-variable identity for a graph one — and never
        /// regenerated, or a rename would orphan exactly what it is here to protect. A declaration
        /// with no id cannot be overridden at all (nothing can bind to it); it still resolves and
        /// still seeds, because that is the name's job, not the id's.
        ///
        /// Appended LAST deliberately: Unity serialization is by field name, but leaving the
        /// existing four where they are keeps every serialized asset diff-clean until something
        /// actually writes an id.
        /// </summary>
        public string id;
    }

    /// <summary>
    /// One state's override of one graph parameter. Declared here rather than beside
    /// <see cref="RunGraphTask"/> (which carries the list) so the interpreter's whole contract —
    /// program, parameters, overrides — lives in one file and compiles on its own.
    ///
    /// <see cref="enabled"/> is the override itself, not a value: an unchecked row means "use the
    /// graph default", which keeps a state that only overrides one of five parameters from freezing
    /// the other four at whatever they happened to be when it was configured.
    ///
    /// BINDING IS BY <see cref="id"/> ONLY (M7h). <see cref="name"/> is carried for display — an
    /// override row has to say what it overrides even when the declaration it points at is gone —
    /// and is never compared. Both appliers (<see cref="GraphTaskAsset.ApplyOverrides"/> and
    /// <see cref="RunSubTreeTask"/>) route every match through <see cref="Matches"/>, so the two
    /// cannot drift apart on what "this row overrides that parameter" means.
    /// </summary>
    [Serializable]
    public sealed class GraphTaskParameterOverride
    {
        /// <summary>Display name of the parameter this row overrides — what the inspector shows,
        /// including for a stale row whose declaration no longer exists. NOT a matching key: see
        /// <see cref="id"/>.</summary>
        public string name;

        /// <summary>False = fall through to the graph default.</summary>
        public bool enabled;

        /// <summary>Value for a Float parameter, and for a Bool one (!= 0).</summary>
        public float floatValue;

        /// <summary>Value for a String parameter.</summary>
        public string stringValue;

        /// <summary><see cref="GraphTaskParameter.id"/> of the declaration this row overrides —
        /// copied from the declaration when the row is created and never rewritten, which is what
        /// lets the declaration be renamed underneath it. Empty means the row is bound to nothing:
        /// it is stale, reported once and skipped, exactly like an id that names a declaration that
        /// has since been deleted.</summary>
        public string id;

        /// <summary>
        /// THE matching rule, in one place because two appliers implement it and a disagreement
        /// between them would show as an override that the inspector says is live and the runtime
        /// ignores. A row binds to a declaration when both carry the SAME non-empty id — no name
        /// comparison anywhere, so a rename cannot break the binding and cannot forge one either.
        ///
        /// A declaration with no NAME is deliberately unmatchable: it is an inspector row mid-edit
        /// rather than a parameter, it is skipped by everything that seeds or reads parameters
        /// (<c>GraphTaskAsset.EffectiveParameters</c>, <c>RunSubTreeTask.SeedParameters</c>),
        /// and letting a row bind to one would make "is this row live?" answer differently in the
        /// two directions this rule is asked in.
        /// </summary>
        public bool Matches(GraphTaskParameter parameter)
        {
            return parameter != null
                && !string.IsNullOrEmpty(id)
                && !string.IsNullOrEmpty(parameter.name)
                && string.Equals(parameter.id, id, StringComparison.Ordinal);
        }

        /// <summary>The declaration this row overrides, or null for STALE — the row is bound to
        /// nothing (no id) or to something that no longer exists. Ids are unique per declaration
        /// list, so the first match is the only one.</summary>
        public GraphTaskParameter Declaration(List<GraphTaskParameter> declared)
        {
            if (declared == null || string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < declared.Count; i++)
            {
                if (Matches(declared[i]))
                    return declared[i];
            }
            return null;
        }

        /// <summary>The row that supplies <paramref name="parameter"/>'s value, or null for "use
        /// the declared default". The LAST enabled match wins, which is what a dictionary built
        /// from the rows would do — duplicates are an inspector accident, and the applier and the
        /// inspector must not disagree about which of them the author sees take effect.</summary>
        public static GraphTaskParameterOverride EnabledFor(List<GraphTaskParameterOverride> rows,
            GraphTaskParameter parameter)
        {
            if (rows == null)
                return null;
            GraphTaskParameterOverride found = null;
            for (int i = 0; i < rows.Count; i++)
            {
                GraphTaskParameterOverride row = rows[i];
                if (row != null && row.enabled && row.Matches(parameter))
                    found = row;
            }
            return found;
        }

        /// <summary>How a stale row is named in a diagnostic: the label the author sees in the
        /// inspector, falling back to the id (and then to the row's position) when there is none —
        /// a warning nobody can trace back to a row is not worth logging.</summary>
        public string Describe(int index)
        {
            if (!string.IsNullOrEmpty(name))
                return "'" + name + "'";
            if (!string.IsNullOrEmpty(id))
                return "id " + id;
            return "the unbound row at index " + index;
        }
    }

    /// <summary>
    /// One node of a baked logic graph. A FLAT record rather than a class hierarchy on purpose: the
    /// whole program is one <c>List&lt;GraphTaskNode&gt;</c> inside a single asset, so it survives
    /// Unity serialization with no <c>[SerializeReference]</c> polymorphism, deep-copies with the
    /// asset for free, and is diffable in YAML. Unused fields for a given
    /// <see cref="GraphTaskNodeKind"/> simply stay at their defaults.
    ///
    /// Pins are NODE INDICES into that same list, never object references, which is what keeps the
    /// program a value: -1 means "end of chain" on <see cref="exec"/> and "unwired" on
    /// <see cref="data"/>, and both arrays may be short or null (a missing pin reads as -1).
    /// </summary>
    [Serializable]
    public sealed class GraphTaskNode
    {
        public GraphTaskNodeKind kind;

        /// <summary>Constants, compare right-hand side, wait seconds.</summary>
        public float floatValue;

        /// <summary>Blackboard keys, cue names, compare operator.</summary>
        public string stringValue;

        /// <summary>Secondary string constant — the unwired default of
        /// <see cref="GraphTaskNodeKind.SetBlackboardString"/>, whose <see cref="stringValue"/> is
        /// already spent on the key.</summary>
        public string stringValue2;

        /// <summary>Object constants (unused by the v1 instruction set, reserved so the baker can
        /// carry an object-typed graph variable without a schema change).</summary>
        public UnityEngine.Object objectValue;

        /// <summary><see cref="GraphTaskNodeKind.EvaluateCondition"/> payload (a sub-asset).</summary>
        public StateTreeConditionAsset condition;

        /// <summary><see cref="GraphTaskNodeKind.DoTask"/> payload (a sub-asset; may itself be a
        /// <see cref="GraphTaskAsset"/>).</summary>
        public StateTreeTaskAsset task;

        /// <summary>Exec out-pin targets, as node indices. -1 = end of chain.</summary>
        public int[] exec;

        /// <summary>Data in-pin sources, as node indices. -1 = unwired (the node's own default).</summary>
        public int[] data;
    }

    /// <summary>
    /// Runs a BLUEPRINT-STYLE LOGIC GRAPH as one state-tree task — the second authored-task flavour
    /// beside <see cref="RunSubTreeTask"/>. A sub-tree composes STATES; this composes STATEMENTS:
    /// branch on the blackboard, write it, call library tasks, wait, and return a status. It is the
    /// surface for "the logic inside one state", where a whole nested state machine is too much
    /// ceremony.
    ///
    /// EXECUTION MODEL (UE Blueprint, deliberately):
    /// <list type="bullet">
    /// <item>Three entry points — <see cref="enterEntry"/>, <see cref="tickEntry"/>,
    /// <see cref="exitEntry"/> — index the chain that runs from each of the task lifecycle hooks.</item>
    /// <item>An exec chain that reaches the end WITHOUT a Return node leaves the task
    /// <see cref="StateTreeStatus.Running"/>. A graph task stays alive until it explicitly returns,
    /// so "do a bit of work every tick forever" is the default shape and needs no keep-alive node.</item>
    /// <item>LATENT nodes (<see cref="GraphTaskNodeKind.DoTask"/>, <see cref="GraphTaskNodeKind.Wait"/>)
    /// can span ticks. When one stays Running the whole graph returns Running and the NEXT tick
    /// resumes AT THAT NODE — it does not re-walk the chain from <see cref="tickEntry"/>, so work
    /// upstream of a wait happens once, not once per frame. Latents are illegal in the enter/exit
    /// chains (there is no tick to resume into): one error, then they pass through on exec[0].</item>
    /// </list>
    ///
    /// PARAMETERS (M7f): the graph's authored variables bake into <see cref="parameters"/> and
    /// become the knobs of the task, read by the <c>GetParam*</c> data nodes. The graph holds the
    /// default and each state that runs it may override any of them
    /// (<see cref="ApplyOverrides"/>) — one authored graph, many tunings, which is the whole
    /// point of a graph being a reusable asset rather than a copy per user.
    ///
    /// CANCELLED PROPAGATION, the reason this is not a script node: <see cref="OnExit"/> runs the
    /// exit chain and THEN hands its own status to whatever latent task is mid-flight, so an
    /// interrupt in the owning tree reaches a library task running three composition levels down as
    /// <see cref="StateTreeStatus.Cancelled"/> — nav goals, timers and spawned VFX tear down exactly
    /// as they do for an inline task.
    ///
    /// GUARDS, all three because each catches a different authoring mistake and none of them can
    /// catch the others: a <see cref="stepBudget"/> on exec steps per tick (a loop with no latent in
    /// it would otherwise hang the editor), a <see cref="maxDataDepth"/> on the recursive data pull
    /// (a data cycle), and a <see cref="maxDepth"/> nesting counter on the shared context (a graph
    /// whose DoTask runs the graph itself — one click away in a graph editor). Each logs ONCE per
    /// running instance and degrades to a defined value instead of throwing.
    /// </summary>
    [StateTreeCategory("Tasks/Composite", "Run a logic graph as this task")]
    public sealed class GraphTaskAsset : StateTreeTaskAsset
    {
        /// <summary>Exec steps allowed per tick before the chain is declared a runaway loop. 512 is
        /// far past any authored chain and still instant to burn through.</summary>
        public const int stepBudget = 512;

        /// <summary>Recursive data-pull depth limit (a pure-data cycle guard).</summary>
        public const int maxDataDepth = 64;

        /// <summary>Tolerance of the "==" / "!=" compare operators. Float equality on authored
        /// values is otherwise a coin flip.</summary>
        public const float compareEpsilon = 1e-4f;

        /// <summary>domainContext key holding the current graph-task nesting depth.
        /// Underscore-prefixed so it can never collide with authored domain keys.</summary>
        public const string depthKey = "__graphTaskDepth";

        /// <summary>Nesting limit for graph tasks running graph tasks.</summary>
        public const int maxDepth = 32;

        /// <summary>The flat program. Indices into this list are the only wiring there is.</summary>
        public List<GraphTaskNode> nodes = new List<GraphTaskNode>();

        /// <summary>Node index the OnEnter chain starts at, -1 for none.</summary>
        public int enterEntry = -1;

        /// <summary>Node index the OnTick chain starts at. -1 means the task Succeeds
        /// immediately.</summary>
        public int tickEntry = -1;

        /// <summary>Node index the OnExit chain starts at, -1 for none.</summary>
        public int exitEntry = -1;

        /// <summary>The graph's variables, baked as the task's parameters with their authored
        /// defaults. Empty for every program baked before M7f, which is exactly what "no
        /// parameters" means — the extension is additive by design.</summary>
        public List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

        // ---------------------------------------------------------------- instance state
        // None of it is serialized, so Object.Instantiate (the runner's DeepCopy) hands every copy
        // a clean slate — the same guarantee every other task relies on.

        /// <summary>Latent nodes currently mid-flight, and their accumulated wait time. Parallel
        /// lists rather than a dictionary: at most one entry is live at a time (arming a latent
        /// returns from the chain immediately), and this way there is nothing to allocate.</summary>
        private readonly List<int> m_LatentNodes = new List<int>();

        private readonly List<float> m_LatentElapsed = new List<float>();

        /// <summary>Node the next tick resumes at, -1 when the chain is not suspended.</summary>
        private int m_LatentNode = -1;

        /// <summary>What <see cref="GraphTaskNodeKind.ExitStatus"/> reads; only ever non-zero while
        /// the exit chain runs.</summary>
        private float m_ExitStatusValue;

        /// <summary>Per-node private copies of the DoTask / EvaluateCondition sub-assets — see
        /// <see cref="TaskCopy"/>.</summary>
        private StateTreeTaskAsset[] m_TaskCopies;

        private StateTreeConditionAsset[] m_ConditionCopies;

        /// <summary>The values the <c>GetParam*</c> nodes read: the graph defaults with this
        /// activation's enabled overrides copied over them, keyed by parameter name. Built lazily,
        /// so a graph reached as a DoTask child — which nobody hands overrides to — still reads its
        /// own defaults, and deliberately NOT cleared by <see cref="ResetActivation"/>:
        /// <see cref="ApplyOverrides"/> runs BEFORE OnEnter, and a reset that threw the overrides
        /// away would silently run every graph at its defaults.</summary>
        private Dictionary<string, GraphTaskParameter> m_Parameters;

        private bool m_Aborted;
        private bool m_DepthPushed;
        private int m_PreviousDepth;

        // One flag per diagnostic, per instance: a state re-entered every frame must not turn an
        // authoring mistake into a console flood.
        private bool m_LatentMisuseLogged;
        private bool m_BudgetLogged;
        private bool m_DataDepthLogged;
        private bool m_BadIndexLogged;
        private bool m_UnknownOverrideLogged;
        private bool m_MissingParameterLogged;

        // ---------------------------------------------------------------- parameters

        /// <summary>
        /// Establishes what the <c>GetParam*</c> nodes read for this activation: every parameter at
        /// its graph default, with each ENABLED override copied over the top. Called by the wrapper
        /// that owns this instance (<see cref="RunGraphTask"/>) after the per-activation copy is
        /// made and before <see cref="OnEnter"/>, so the enter chain already sees the state's
        /// values.
        ///
        /// The effective values are private COPIES of the parameters, never the parameter objects
        /// themselves, so two states overriding one authored graph can never write into each
        /// other's values — or into the asset on disk — however shallow the copy that produced
        /// this instance was.
        ///
        /// Calling it again re-derives everything from the defaults first: applying a second set of
        /// overrides must not leave the first set's values standing.
        ///
        /// Rows bind to declarations BY ID (M7h), through
        /// <see cref="GraphTaskParameterOverride.Matches"/> — never by name — so a graph variable
        /// renamed after a state was tuned keeps that state's value, and a row that happens to
        /// carry a name matching some other parameter cannot capture it.
        /// </summary>
        /// <param name="overrides">The owning state's override rows; null, empty, or all-disabled
        /// means "the graph defaults". A row bound to a parameter this graph no longer declares —
        /// or bound to nothing at all — is reported once and ignored: a graph gets re-authored long
        /// after the states that use it were configured, and that must not stop them running.</param>
        public void ApplyOverrides(List<GraphTaskParameterOverride> overrides)
        {
            m_Parameters = null;
            Dictionary<string, GraphTaskParameter> effective = EffectiveParameters();
            if (overrides == null)
                return;

            string unknown = null;
            for (int i = 0; i < overrides.Count; i++)
            {
                GraphTaskParameterOverride row = overrides[i];
                if (row == null || !row.enabled)
                    continue;

                // The AUTHORED list resolves the identity; the effective set (keyed by the runtime
                // name) holds the copy the GetParam* pulls read, so the row's value lands where the
                // graph will look for it.
                GraphTaskParameter declared = row.Declaration(parameters);
                GraphTaskParameter parameter = null;
                if (declared != null)
                    effective.TryGetValue(declared.name, out parameter);
                if (parameter == null)
                {
                    unknown = unknown == null
                        ? row.Describe(i)
                        : unknown + ", " + row.Describe(i);
                    continue;
                }

                // Only the field the parameter's KIND actually shows: a row left over from when the
                // variable was a float must not smuggle a number into a string parameter.
                if (parameter.kind == GraphTaskParameterKind.String)
                    parameter.stringValue = row.stringValue;
                else
                    parameter.floatValue = row.floatValue;
            }

            if (unknown == null || m_UnknownOverrideLogged)
                return;
            m_UnknownOverrideLogged = true;
            Debug.LogWarning($"GraphTaskAsset '{name}': override for {unknown} — this graph " +
                "declares no parameter with that id (deleted, or the row was never bound). Using " +
                "the graph default.", this);
        }

        /// <summary>The effective set, built from the authored defaults on first use.</summary>
        private Dictionary<string, GraphTaskParameter> EffectiveParameters()
        {
            if (m_Parameters != null)
                return m_Parameters;

            m_Parameters = new Dictionary<string, GraphTaskParameter>();
            if (parameters == null)
                return m_Parameters;

            for (int i = 0; i < parameters.Count; i++)
            {
                GraphTaskParameter authored = parameters[i];
                if (authored == null || string.IsNullOrEmpty(authored.name))
                    continue;
                m_Parameters[authored.name] = new GraphTaskParameter
                {
                    name = authored.name,
                    kind = authored.kind,
                    floatValue = authored.floatValue,
                    stringValue = authored.stringValue,
                    // Carried so the copy is a faithful stand-in for the declaration; the identity
                    // itself is resolved against the authored list, which is the one an editor and
                    // a baker both write.
                    id = authored.id
                };
            }
            return m_Parameters;
        }

        /// <summary>The effective parameter a <c>GetParam*</c> node names, or null when the program
        /// declares no such parameter. That can only be a BAKER bug — the node and the parameter
        /// list come out of the same graph — so it is an error rather than a warning, reported once
        /// and read as the type default.</summary>
        private GraphTaskParameter Parameter(string parameterName)
        {
            GraphTaskParameter parameter;
            if (!string.IsNullOrEmpty(parameterName)
                && EffectiveParameters().TryGetValue(parameterName, out parameter))
                return parameter;

            if (!m_MissingParameterLogged)
            {
                m_MissingParameterLogged = true;
                Debug.LogError($"GraphTaskAsset '{name}': a GetParam node reads parameter " +
                    $"'{parameterName}', which this program does not declare. Reading the type " +
                    "default.", this);
            }
            return null;
        }

        private float ParameterFloat(string parameterName)
        {
            GraphTaskParameter parameter = Parameter(parameterName);
            return parameter == null ? 0f : parameter.floatValue;
        }

        private string ParameterString(string parameterName)
        {
            GraphTaskParameter parameter = Parameter(parameterName);
            return parameter == null || parameter.stringValue == null
                ? string.Empty
                : parameter.stringValue;
        }

        // ---------------------------------------------------------------- task lifecycle

        public override void OnEnter(StateTreeContext context)
        {
            ResetActivation();

            if (context != null)
            {
                int depth = ReadDepth(context);
                if (depth >= maxDepth)
                {
                    Debug.LogError($"GraphTaskAsset '{name}': logic graphs nested deeper than " +
                        $"{maxDepth} (a graph that runs itself?), aborting", this);
                    m_Aborted = true;
                    return;
                }
                m_PreviousDepth = depth;
                context.domainContext[depthKey] = depth + 1;
                m_DepthPushed = true;
            }

            if (enterEntry >= 0)
                RunChain(context, 0f, enterEntry, false);
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            // A refused entry cannot run: fail so the owning state can transition away instead of
            // hanging on a task that will never do anything (RunSubTreeTask precedent).
            if (m_Aborted)
                return StateTreeStatus.Failure;

            int start = IsLatentArmed() ? m_LatentNode : tickEntry;
            if (start < 0)
                return StateTreeStatus.Success;

            // Cleared before the walk, re-armed by the latent node itself if it stays Running.
            m_LatentNode = -1;
            return RunChain(context, deltaTime, start, true);
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            m_ExitStatusValue = StatusCode(status);

            // The exit chain runs FIRST and the latent child is torn down after, so an exit chain
            // can still observe (and act on) the fact that something was mid-flight.
            if (!m_Aborted && exitEntry >= 0)
                RunChain(context, 0f, exitEntry, false);

            for (int i = m_LatentNodes.Count - 1; i >= 0; i--)
            {
                int index = m_LatentNodes[i];
                GraphTaskNode node = NodeAt(index);
                if (node == null || node.kind != GraphTaskNodeKind.DoTask)
                    continue;
                StateTreeTaskAsset running = TaskCopy(index, node, false);
                if (running != null)
                    running.OnExit(context, status);
            }

            m_LatentNodes.Clear();
            m_LatentElapsed.Clear();
            m_LatentNode = -1;
            ReleaseCopies();

            if (m_DepthPushed && context != null)
            {
                if (m_PreviousDepth > 0)
                    context.domainContext[depthKey] = m_PreviousDepth;
                else
                    context.domainContext.Remove(depthKey);
            }
            m_DepthPushed = false;
            m_Aborted = false;
            m_ExitStatusValue = 0f;
        }

        /// <summary>Backstop for a copy that is destroyed without a final OnExit (domain reload,
        /// a runner torn down mid-state). The normal path has already emptied the arrays.</summary>
        private void OnDestroy()
        {
            ReleaseCopies();
        }

        private void ResetActivation()
        {
            m_LatentNodes.Clear();
            m_LatentElapsed.Clear();
            m_LatentNode = -1;
            m_ExitStatusValue = 0f;
            m_Aborted = false;
            m_DepthPushed = false;
            m_PreviousDepth = 0;
        }

        // ---------------------------------------------------------------- the interpreter

        /// <summary>
        /// Walks the exec chain from <paramref name="start"/> until a Return node, a latent node
        /// that stays Running, the end of the chain, or the step budget.
        /// </summary>
        /// <param name="latentsAllowed">False for the enter/exit chains, which have no tick to
        /// resume into — a latent there is an authoring error, reported once and stepped over.</param>
        /// <returns>The status for THIS tick. Meaningful only for the tick chain; the enter and
        /// exit chains discard it.</returns>
        private StateTreeStatus RunChain(StateTreeContext context, float deltaTime, int start,
            bool latentsAllowed)
        {
            int index = start;
            int steps = 0;

            while (index >= 0)
            {
                if (steps >= stepBudget)
                {
                    if (!m_BudgetLogged)
                    {
                        m_BudgetLogged = true;
                        Debug.LogError($"GraphTaskAsset '{name}': exec chain exceeded {stepBudget} " +
                            "steps in one tick — a loop with no Wait/DoTask in it. Failing the task.",
                            this);
                    }
                    return StateTreeStatus.Failure;
                }
                steps++;

                GraphTaskNode node = NodeAt(index);
                if (node == null)
                {
                    if (!m_BadIndexLogged)
                    {
                        m_BadIndexLogged = true;
                        Debug.LogError($"GraphTaskAsset '{name}': exec pin points at node {index}, " +
                            "which does not exist. Ending the chain.", this);
                    }
                    return StateTreeStatus.Running;
                }

                int next;
                switch (node.kind)
                {
                    case GraphTaskNodeKind.Branch:
                        next = ExecPin(node, PullBool(context, node, 0, 0) ? 0 : 1);
                        break;

                    case GraphTaskNodeKind.SetBlackboardFloat:
                        if (context != null && !string.IsNullOrEmpty(node.stringValue))
                            context.blackboard[node.stringValue] =
                                PullFloat(context, node, 0, node.floatValue, 0);
                        next = ExecPin(node, 0);
                        break;

                    case GraphTaskNodeKind.SetBlackboardString:
                        if (context != null && !string.IsNullOrEmpty(node.stringValue))
                            context.blackboard[node.stringValue] =
                                PullString(context, node, 0, node.stringValue2 ?? string.Empty, 0);
                        next = ExecPin(node, 0);
                        break;

                    case GraphTaskNodeKind.DoTask:
                    {
                        if (!latentsAllowed)
                        {
                            LogLatentMisuse(node.kind, index);
                            next = ExecPin(node, 0);
                            break;
                        }

                        StateTreeTaskAsset task = TaskCopy(index, node, true);
                        if (task == null)
                        {
                            // An unconfigured DoTask is a wiring hole, not a failure: continue on
                            // the success pin so the rest of the graph is still authorable.
                            next = ExecPin(node, 0);
                            break;
                        }

                        if (LatentSlot(index) < 0)
                        {
                            BeginLatent(index);
                            task.OnEnter(context);
                        }

                        StateTreeStatus result = task.OnTick(context, deltaTime);
                        if (result == StateTreeStatus.Running)
                        {
                            m_LatentNode = index;
                            return StateTreeStatus.Running;
                        }

                        EndLatent(index);
                        task.OnExit(context, result);
                        // Cancelled is not a status a task returns from OnTick, but if one does it
                        // is emphatically not success — route it with Failure.
                        next = ExecPin(node, result == StateTreeStatus.Success ? 0 : 1);
                        break;
                    }

                    case GraphTaskNodeKind.Wait:
                    {
                        if (!latentsAllowed)
                        {
                            LogLatentMisuse(node.kind, index);
                            next = ExecPin(node, 0);
                            break;
                        }

                        int slot = LatentSlot(index);
                        if (slot < 0)
                            slot = BeginLatent(index);

                        // Accumulated deltaTime, never Time.time: the runner is driven at an
                        // arbitrary step by the EditMode tests, and scales with timeScale in play.
                        float duration = PullFloat(context, node, 0, node.floatValue, 0);
                        float elapsed = m_LatentElapsed[slot] + deltaTime;
                        m_LatentElapsed[slot] = elapsed;
                        if (elapsed < duration)
                        {
                            m_LatentNode = index;
                            return StateTreeStatus.Running;
                        }

                        EndLatent(index);
                        next = ExecPin(node, 0);
                        break;
                    }

                    // In the enter/exit chains there is no status to return, so a Return node just
                    // ends the chain — which is exactly what returning from here does.
                    case GraphTaskNodeKind.ReturnSuccess:
                        return StateTreeStatus.Success;
                    case GraphTaskNodeKind.ReturnFailure:
                        return StateTreeStatus.Failure;
                    case GraphTaskNodeKind.ReturnRunning:
                        return StateTreeStatus.Running;

                    case GraphTaskNodeKind.FireCue:
                        if (context != null && !string.IsNullOrEmpty(node.stringValue))
                        {
                            context.EmitCueFired(node.stringValue,
                                new Dictionary<string, object> { { "owner", context.owner } });
                        }
                        next = ExecPin(node, 0);
                        break;

                    default:
                        // A data node reached over an exec pin has nothing to execute and no exec
                        // out — the chain simply ends.
                        next = -1;
                        break;
                }

                index = next;
            }

            // Fell off the end without a Return: the task stays alive. THE Blueprint rule.
            return StateTreeStatus.Running;
        }

        private void LogLatentMisuse(GraphTaskNodeKind kind, int index)
        {
            if (m_LatentMisuseLogged)
                return;
            m_LatentMisuseLogged = true;
            Debug.LogError($"GraphTaskAsset '{name}': {kind} node {index} sits in the OnEnter or " +
                "OnExit chain, which cannot suspend. Treating it as a pass-through.", this);
        }

        // ---------------------------------------------------------------- latent bookkeeping

        private bool IsLatentArmed()
        {
            // Membership, not just the index: a copy that is ticked without ever being entered has
            // an empty latent list, so it can never resume into the middle of a chain.
            return m_LatentNode >= 0 && m_LatentNodes.Contains(m_LatentNode);
        }

        private int LatentSlot(int index) => m_LatentNodes.IndexOf(index);

        private int BeginLatent(int index)
        {
            m_LatentNodes.Add(index);
            m_LatentElapsed.Add(0f);
            return m_LatentNodes.Count - 1;
        }

        private void EndLatent(int index)
        {
            int slot = LatentSlot(index);
            if (slot >= 0)
            {
                m_LatentNodes.RemoveAt(slot);
                m_LatentElapsed.RemoveAt(slot);
            }
            if (m_LatentNode == index)
                m_LatentNode = -1;
        }

        // ---------------------------------------------------------------- data pulls

        private bool PullBool(StateTreeContext context, GraphTaskNode owner, int pin, int depth)
        {
            int source = PinSource(owner, pin, depth);
            return source >= 0 && EvalBool(context, source, nodes[source], depth + 1);
        }

        private float PullFloat(StateTreeContext context, GraphTaskNode owner, int pin,
            float fallback, int depth)
        {
            int source = PinSource(owner, pin, depth);
            return source < 0 ? fallback : EvalFloat(context, source, nodes[source], depth + 1);
        }

        private string PullString(StateTreeContext context, GraphTaskNode owner, int pin,
            string fallback, int depth)
        {
            int source = PinSource(owner, pin, depth);
            return source < 0 ? fallback : EvalString(context, source, nodes[source], depth + 1);
        }

        /// <summary>Index of the node feeding <paramref name="pin"/>, or -1 for "unwired" — which
        /// covers a -1 pin, a missing pin, a dangling index, and the depth guard tripping. The INDEX
        /// rather than the node, because it is also the key of the per-node condition copy.</summary>
        private int PinSource(GraphTaskNode owner, int pin, int depth)
        {
            int[] pins = owner.data;
            if (pins == null || pin < 0 || pin >= pins.Length)
                return -1;
            int index = pins[pin];
            if (index < 0)
                return -1;
            if (depth >= maxDataDepth)
            {
                if (!m_DataDepthLogged)
                {
                    m_DataDepthLogged = true;
                    Debug.LogError($"GraphTaskAsset '{name}': data pull nested deeper than " +
                        $"{maxDataDepth} — a data cycle. Using the unwired default.", this);
                }
                return -1;
            }
            return NodeAt(index) == null ? -1 : index;
        }

        private float EvalFloat(StateTreeContext context, int index, GraphTaskNode node, int depth)
        {
            switch (node.kind)
            {
                case GraphTaskNodeKind.ConstFloat:
                    return node.floatValue;
                case GraphTaskNodeKind.GetBlackboardFloat:
                    return ReadBlackboardFloat(context, node.stringValue);
                case GraphTaskNodeKind.GetParamFloat:
                    return ParameterFloat(node.stringValue);
                case GraphTaskNodeKind.ExitStatus:
                    return m_ExitStatusValue;
                case GraphTaskNodeKind.ConstString:
                case GraphTaskNodeKind.GetBlackboardString:
                case GraphTaskNodeKind.GetParamString:
                    // A string has no numeric reading here; parsing it would make a typo silently
                    // mean zero either way, so it means zero loudly and consistently.
                    return 0f;
                case GraphTaskNodeKind.ConstBool:
                case GraphTaskNodeKind.HasBlackboardKey:
                case GraphTaskNodeKind.EvaluateCondition:
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr:
                case GraphTaskNodeKind.BoolNot:
                case GraphTaskNodeKind.GetParamBool:
                    return EvalBool(context, index, node, depth) ? 1f : 0f;
                default:
                    return 0f;
            }
        }

        private bool EvalBool(StateTreeContext context, int index, GraphTaskNode node, int depth)
        {
            switch (node.kind)
            {
                case GraphTaskNodeKind.ConstBool:
                case GraphTaskNodeKind.ConstFloat:
                    return node.floatValue != 0f;
                case GraphTaskNodeKind.ConstString:
                    return !string.IsNullOrEmpty(node.stringValue);
                case GraphTaskNodeKind.GetBlackboardFloat:
                    return ReadBlackboardFloat(context, node.stringValue) != 0f;
                case GraphTaskNodeKind.GetBlackboardString:
                    return !string.IsNullOrEmpty(ReadBlackboardString(context, node.stringValue));
                // A Bool parameter IS its float (!= 0), so both kinds read the same way — which is
                // also what lets a variable be retyped between them without breaking a graph.
                case GraphTaskNodeKind.GetParamBool:
                case GraphTaskNodeKind.GetParamFloat:
                    return ParameterFloat(node.stringValue) != 0f;
                case GraphTaskNodeKind.GetParamString:
                    return !string.IsNullOrEmpty(ParameterString(node.stringValue));
                case GraphTaskNodeKind.HasBlackboardKey:
                    return context != null && !string.IsNullOrEmpty(node.stringValue)
                        && context.blackboard.ContainsKey(node.stringValue);
                case GraphTaskNodeKind.EvaluateCondition:
                {
                    StateTreeConditionAsset condition = ConditionCopy(index, node);
                    return condition == null || condition.Evaluate(context);
                }
                case GraphTaskNodeKind.CompareFloat:
                {
                    float lhs = PullFloat(context, node, 0, 0f, depth);
                    float rhs = PullFloat(context, node, 1, node.floatValue, depth);
                    return Compare(lhs, rhs, node.stringValue);
                }
                case GraphTaskNodeKind.BoolAnd:
                {
                    // Both operands are pulled before combining — deliberately NOT short-circuiting.
                    // Conditions are allowed side effects (TargetDetectedCondition writes the
                    // blackboard target), so which operands get evaluated must not depend on the
                    // value of another.
                    bool a = PullBool(context, node, 0, depth);
                    bool b = PullBool(context, node, 1, depth);
                    return a && b;
                }
                case GraphTaskNodeKind.BoolOr:
                {
                    bool a = PullBool(context, node, 0, depth);
                    bool b = PullBool(context, node, 1, depth);
                    return a || b;
                }
                case GraphTaskNodeKind.BoolNot:
                    return !PullBool(context, node, 0, depth);
                case GraphTaskNodeKind.ExitStatus:
                    return m_ExitStatusValue != 0f;
                default:
                    return false;
            }
        }

        private string EvalString(StateTreeContext context, int index, GraphTaskNode node, int depth)
        {
            switch (node.kind)
            {
                case GraphTaskNodeKind.ConstString:
                    return node.stringValue ?? string.Empty;
                case GraphTaskNodeKind.GetBlackboardString:
                    return ReadBlackboardString(context, node.stringValue);
                case GraphTaskNodeKind.GetParamString:
                    return ParameterString(node.stringValue);
                case GraphTaskNodeKind.ConstFloat:
                case GraphTaskNodeKind.GetBlackboardFloat:
                case GraphTaskNodeKind.GetParamFloat:
                case GraphTaskNodeKind.ExitStatus:
                    return EvalFloat(context, index, node, depth)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                case GraphTaskNodeKind.ConstBool:
                case GraphTaskNodeKind.HasBlackboardKey:
                case GraphTaskNodeKind.EvaluateCondition:
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr:
                case GraphTaskNodeKind.BoolNot:
                case GraphTaskNodeKind.GetParamBool:
                    return EvalBool(context, index, node, depth) ? "true" : "false";
                default:
                    return string.Empty;
            }
        }

        private static bool Compare(float lhs, float rhs, string op)
        {
            switch (op)
            {
                case "<": return lhs < rhs;
                case "<=": return lhs <= rhs;
                case ">": return lhs > rhs;
                case ">=": return lhs >= rhs;
                case "==": return Mathf.Abs(lhs - rhs) <= compareEpsilon;
                case "!=": return Mathf.Abs(lhs - rhs) > compareEpsilon;
                // An unrecognised operator reads false rather than guessing at equality: the baker
                // only ever writes the six, so this can only be hand-edited data.
                default: return false;
            }
        }

        private static float ReadBlackboardFloat(StateTreeContext context, string key)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return 0f;
            object value;
            if (!context.blackboard.TryGetValue(key, out value))
                return 0f;
            // The blackboard is untyped, and M5/M6 components write ints and bools into it as
            // readily as floats — read all of them rather than making the author care.
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is double d) return (float)d;
            if (value is bool b) return b ? 1f : 0f;
            return 0f;
        }

        private static string ReadBlackboardString(StateTreeContext context, string key)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return string.Empty;
            object value;
            if (!context.blackboard.TryGetValue(key, out value) || value == null)
                return string.Empty;
            string text = value as string;
            return text ?? value.ToString();
        }

        // ---------------------------------------------------------------- sub-asset copies

        /// <summary>
        /// The per-instance copy of a DoTask's task. <see cref="StateTreeAsset.DeepCopy"/> only
        /// duplicates one level — the tasks in a node's own list — so two runners sharing an
        /// authored graph would otherwise TICK THE SAME task object and stomp each other's timers,
        /// exactly the failure the tree-wide deep copy exists to prevent. Copying lazily (on first
        /// reach) keeps a graph with ten branches from instantiating ten tasks to run one, and
        /// <see cref="OnExit"/> releases them, so an entry/exit cycle leaves nothing behind.
        /// </summary>
        private StateTreeTaskAsset TaskCopy(int index, GraphTaskNode node, bool create)
        {
            if (node == null || node.task == null)
                return null;
            if (m_TaskCopies == null)
            {
                if (!create)
                    return null;
                m_TaskCopies = new StateTreeTaskAsset[nodes.Count];
            }
            if (index < 0 || index >= m_TaskCopies.Length)
                return null;

            StateTreeTaskAsset copy = m_TaskCopies[index];
            if (copy == null && create)
            {
                copy = Instantiate(node.task);
                copy.name = node.task.name;
                m_TaskCopies[index] = copy;
            }
            return copy;
        }

        /// <summary>Same isolation for conditions. They are stateless by convention (Evaluate has no
        /// lifecycle), but the tree-wide deep copy duplicates them too, and matching it costs one
        /// array.</summary>
        private StateTreeConditionAsset ConditionCopy(int index, GraphTaskNode node)
        {
            if (node.condition == null)
                return null;
            if (m_ConditionCopies == null)
                m_ConditionCopies = new StateTreeConditionAsset[nodes.Count];
            if (index < 0 || index >= m_ConditionCopies.Length)
                return node.condition;

            StateTreeConditionAsset copy = m_ConditionCopies[index];
            if (copy == null)
            {
                copy = Instantiate(node.condition);
                copy.name = node.condition.name;
                m_ConditionCopies[index] = copy;
            }
            return copy;
        }

        private void ReleaseCopies()
        {
            if (m_TaskCopies != null)
            {
                for (int i = 0; i < m_TaskCopies.Length; i++)
                {
                    DestroyRuntimeCopy(m_TaskCopies[i]);
                    m_TaskCopies[i] = null;
                }
                m_TaskCopies = null;
            }
            if (m_ConditionCopies != null)
            {
                for (int i = 0; i < m_ConditionCopies.Length; i++)
                {
                    DestroyRuntimeCopy(m_ConditionCopies[i]);
                    m_ConditionCopies[i] = null;
                }
                m_ConditionCopies = null;
            }
        }

        /// <summary>Instantiate copies live until domain reload otherwise — same release path as
        /// <see cref="StateTreeAsset.DestroyCopy"/>, play-mode and edit-mode safe.</summary>
        private static void DestroyRuntimeCopy(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        // ---------------------------------------------------------------- small helpers

        private GraphTaskNode NodeAt(int index)
        {
            if (nodes == null || index < 0 || index >= nodes.Count)
                return null;
            return nodes[index];
        }

        private static int ExecPin(GraphTaskNode node, int pin)
        {
            int[] pins = node.exec;
            if (pins == null || pin < 0 || pin >= pins.Length)
                return -1;
            return pins[pin];
        }

        private static float StatusCode(StateTreeStatus status)
        {
            switch (status)
            {
                case StateTreeStatus.Failure: return 1f;
                case StateTreeStatus.Cancelled: return 2f;
                // Success, and Running which never reaches OnExit.
                default: return 0f;
            }
        }

        private static int ReadDepth(StateTreeContext context)
        {
            object value;
            if (context.domainContext.TryGetValue(depthKey, out value) && value is int)
                return (int)value;
            return 0;
        }
    }
}

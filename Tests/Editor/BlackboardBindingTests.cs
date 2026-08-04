using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of BLACKBOARD-SOURCED field bindings (M7k) — the second source a
    /// <see cref="StateTreeFieldBinding"/> row can take its value from, and the one that finally
    /// carries a routed task output into the field of a plain C# task.
    ///
    /// The whole feature is about WHEN, so every case here is about ordering rather than about
    /// values: the row is applied when the state is ENTERED, before its tasks are entered (so a task
    /// that acts in OnEnter acts on it) and after the fired transition routed its outputs (so what
    /// the previous state returned is already on the blackboard). Both halves are only observable
    /// from inside the running copy, which is why every assertion reads
    /// <see cref="StubBoundFieldTask"/>'s log line rather than a field of the authored asset — the
    /// executor writes the copy, and the authored asset staying at zero is itself one of the things
    /// being asserted.
    ///
    /// The stubs are shared with the M7i and M7j suites on purpose: <see cref="StubBoundFieldTask"/>
    /// (one public field per bindable type, reported on every entry) is the same target those cases
    /// bind through parameters, and <see cref="StubOutputTask"/> is the same producer M7j routes
    /// from. Reusing them is what makes "the parameter path and the key path write the same field the
    /// same way" a fact about the code rather than about two different fixtures.
    /// </summary>
    [TestFixture]
    public sealed class BlackboardBindingTests
    {
        /// <summary>Blackboard key the ping-pong fixture leaves the working state on.</summary>
        private const string k_ToIdleKey = "toIdle";

        /// <summary>...and the one that sends it back in, which is what makes a re-entry a thing a
        /// test can ask for.</summary>
        private const string k_ToWorkKey = "toWork";

        /// <summary>The key almost every case binds through — named after what really flows down it:
        /// a number one state computed and the next one needs.</summary>
        private const string k_DamageKey = "damageDealt";

        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            m_Objects.Clear();
            m_Assets.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            m_Objects.Clear();
            m_Assets.Clear();
        }

        // ------------------------------------------------------------------ entry-time apply

        /// <summary>THE rule: by the time the task is entered, the bound field already holds what the
        /// key held. Asserted through the OnEnter log line because that is the earliest moment a task
        /// can observe anything — a value that arrived one statement later would be invisible here
        /// and useless to every task that decides what to do in OnEnter.</summary>
        [Test]
        public void EntryBinding_FieldHoldsTheKeysValueBeforeTheTaskEnters()
        {
            StubBoundFieldTask authored = MakeBoundTask("bound");
            var work = MakeNode("work", authored);
            BindKey(work, 0, "speed", k_DamageKey);

            var runner = MakeRunner(MakeTree(work, "EntryBoundTree"), "Zombie");
            runner.context.blackboard[k_DamageKey] = 12f;
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "12|0|false|" }, Entries(runner),
                "the running copy entered with the key's value already in its field");
            Assert.AreEqual(0f, authored.speed,
                "the authored task must never be written to — only the executor's deep copy is");
        }

        /// <summary>The difference from a parameter row, stated as a test: a key is a value the run
        /// PRODUCES, so a state entered again reads it again. Anything else would make the feature
        /// useless for the case it exists for — a state re-entered per attack, each time with a
        /// different result routed into it.</summary>
        [Test]
        public void EntryBinding_ReEntryReadsTheKeyAgain()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "speed", k_DamageKey);

            var runner = MakeRunner(PingPongTree(work), "Zombie");
            runner.context.blackboard[k_DamageKey] = 1f;
            runner.StartTree();

            runner.context.blackboard[k_DamageKey] = 5f;
            ReEnterWork(runner);

            CollectionAssert.AreEqual(new[] { "1|0|false|", "5|0|false|" }, Entries(runner),
                "each entry read the key as it stood at that entry");
        }

        /// <summary>A key that is not there is NOT a fault and must not say so. Several transitions
        /// normally lead into one state and only some of them route anything, so entering through an
        /// unrouted edge is the ordinary path — a console line per entry would make the feature
        /// unusable in exactly the shape it is meant for.</summary>
        [Test]
        public void EntryBinding_MissingKeyLeavesTheFieldAloneAndSaysNothing()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "speed", "neverWritten");

            var runner = MakeRunner(MakeTree(work, "EntryBoundTree"), "Zombie");
            runner.StartTree();
            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(new[] { "0|0|false|" }, Entries(runner),
                "the field kept its authored value");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The other half of that rule, which is the part worth pinning down: "skip" means
        /// LEAVE IT, not "clear it". A state entered once through a routing edge and once through a
        /// plain one keeps the last routed value rather than snapping back to the authored default —
        /// the field behaves like a variable, not like a slot that empties.</summary>
        [Test]
        public void EntryBinding_KeyRemovedBetweenEntriesLeavesTheLastValueStanding()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "speed", k_DamageKey);

            var runner = MakeRunner(PingPongTree(work), "Zombie");
            runner.context.blackboard[k_DamageKey] = 8f;
            runner.StartTree();

            runner.context.blackboard.Remove(k_DamageKey);
            ReEnterWork(runner);

            CollectionAssert.AreEqual(new[] { "8|0|false|", "8|0|false|" }, Entries(runner),
                "the second entry found no key and left the field as the first entry set it");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>An empty key is an unfinished row, not a mistake to report at runtime: the
        /// inspector is where it is visible and where it can be finished.</summary>
        [Test]
        public void EntryBinding_EmptyKeyIsInert()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "speed", "");

            var runner = MakeRunner(MakeTree(work, "EntryBoundTree"), "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "0|0|false|" }, Entries(runner));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ route → key → field

        /// <summary>
        /// THE case the whole milestone exists for, end to end: one state's task RETURNS a number
        /// (M7j), the transition that ends the state routes it to a key (M7j), and the state it
        /// transitions into starts with that number in a plain <c>public float</c> (M7k). Three
        /// features, one tick, and neither end knows the other exists — they agree on a key.
        ///
        /// The ordering inside that single tick is the load-bearing part: capture at the return,
        /// route on the transition, bind on the entry, all before the receiving task's OnEnter.
        /// </summary>
        [Test]
        public void RoutedOutput_ReachesTheNextStatesBoundFieldInOneHop()
        {
            var fight = MakeNode("fight", MakeOutputTask("attack", 7f));
            Route(AddTransition(fight, "recover", null, false), 0, "amount", k_DamageKey);

            var recover = MakeNode("recover", MakeBoundTask("bound"));
            BindKey(recover, 0, "speed", k_DamageKey);

            var root = MakeNode("root");
            root.children.Add(fight);
            root.children.Add(recover);

            var runner = MakeRunner(MakeTree(root, "RouteTree"), "Zombie");
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("recover", runner.activeNodeId, "the completion transition fired");
            CollectionAssert.AreEqual(new[] { "7|0|false|" }, Entries(runner),
                "what the attack returned is in the next state's field before that state's OnEnter");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ conversions

        /// <summary>A number reaches all three numeric-ish fields, because the blackboard has no
        /// kinds to consult: a Bool output is stored as 1/0 (the seeding rule — see
        /// <c>StateTreeExecutor.BoxedValue</c>), so refusing float→bool would make the single most
        /// likely thing to route impossible to bind.</summary>
        [Test]
        public void EntryBinding_ANumberWritesFloatIntAndBoolFields()
        {
            var work = MakeBoundFieldFanOut("amount", "mood");
            var runner = MakeRunner(MakeTree(work, "ConvertTree"), "Zombie");
            runner.context.blackboard["amount"] = 2.5f;
            runner.context.blackboard["mood"] = "furious";
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "2.5|2|true|furious" }, Entries(runner),
                "float as-is, int truncated, bool as != 0, string into the string field");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>And the same crossing in the other direction: a boxed <c>bool</c> — what a
        /// hand-written condition or task naturally puts on the blackboard — reads back as 1/0 into a
        /// number field, so a flag and a count are interchangeable here even though a PARAMETER row
        /// keeps them apart. The asymmetry is deliberate: a parameter arrives with a declared kind to
        /// respect, a blackboard entry arrives with nothing but a CLR type.</summary>
        [Test]
        public void EntryBinding_ABoxedBoolWritesBoolFloatAndIntFields()
        {
            var work = MakeBoundFieldFanOut("amount", "mood");
            var runner = MakeRunner(MakeTree(work, "ConvertTree"), "Zombie");
            runner.context.blackboard["amount"] = true;
            runner.context.blackboard["mood"] = "calm";
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "1|1|true|calm" }, Entries(runner));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A boxed <c>int</c> counts as a number, for the same reason
        /// <c>StateTreeLibraryUtil.TryGetFloat</c> accepts one (StateTreeLibraryUtil.cs:164-177): a
        /// task or a preset that stores <c>3</c> rather than <c>3f</c> is ordinary, and a binding that
        /// warned where a Get Blackboard node succeeds would be reporting a difference the author
        /// cannot see.</summary>
        [Test]
        public void EntryBinding_ABoxedIntCountsAsANumber()
        {
            var work = MakeBoundFieldFanOut("amount", "mood");
            var runner = MakeRunner(MakeTree(work, "ConvertTree"), "Zombie");
            runner.context.blackboard["amount"] = 3;
            runner.context.blackboard["mood"] = "calm";
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "3|3|true|calm" }, Entries(runner));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A value the field cannot take is a WARNING rather than an error — the blackboard
        /// is shared and untyped, so two authored things collided and both may be individually
        /// right — said ONCE however many times the state is re-entered, and costing only that row:
        /// the rest of the list still applies, because one bad wire must not disarm the state.</summary>
        [Test]
        public void EntryBinding_TypeMismatchWarnsOnceAndSkipsOnlyThatRow()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "speed", "mood");   // a string into a float field: refused.
            BindKey(work, 0, "label", "mood");   // and this one still lands.

            var runner = MakeRunner(PingPongTree(work), "Zombie");
            runner.context.blackboard["mood"] = "furious";

            LogAssert.Expect(LogType.Warning, new Regex("'speed'"));
            runner.StartTree();
            ReEnterWork(runner);
            ReEnterWork(runner);

            CollectionAssert.AreEqual(
                new[] { "0|0|false|furious", "0|0|false|furious", "0|0|false|furious" },
                Entries(runner), "three entries, the good row applied every time");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ conditions

        /// <summary>A transition's condition is entered-into as much as a task is: it is evaluated
        /// while the state runs, so a value carried into the state is exactly as relevant to the
        /// edges leaving it. Proven through BEHAVIOUR — the key the condition watches is itself what
        /// is bound, so the transition fires on the routed key and on no other.</summary>
        [Test]
        public void EntryBinding_TransitionConditionFieldIsBoundOnEntry()
        {
            var work = MakeNode("work", MakeTask("work"));
            AddTransition(work, "done", MakeFlagCondition("unbound"), true);
            BindKey(work, 0, "flagKey", "gateKeyName",
                StateTreeFieldBinding.TargetKind.TransitionCondition);
            var done = MakeNode("done", MakeTask("done"));
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(done);

            var runner = MakeRunner(MakeTree(root, "GateTree"), "Zombie");
            runner.context.blackboard["gateKeyName"] = "gate";
            runner.StartTree();

            runner.context.blackboard["unbound"] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("work", runner.activeNodeId,
                "the condition no longer watches the key it was authored with");

            runner.context.blackboard["gate"] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("done", runner.activeNodeId,
                "it watches the key the binding named");
        }

        // ------------------------------------------------------------------ broken rows

        /// <summary>A row naming a field that is not there cannot come right by itself, so unlike a
        /// missing key it IS reported — once per run, as an error, and the state still enters and
        /// runs at its authored values. Binding is authoring metadata; losing one must not lose the
        /// state it was authored on.</summary>
        [Test]
        public void EntryBinding_UnknownFieldErrorsOnceAndTheStateStillRuns()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "missingField", k_DamageKey);

            var runner = MakeRunner(PingPongTree(work), "Zombie");
            runner.context.blackboard[k_DamageKey] = 4f;

            LogAssert.Expect(LogType.Error, new Regex("missingField"));
            runner.StartTree();
            ReEnterWork(runner);

            CollectionAssert.AreEqual(new[] { "0|0|false|", "0|0|false|" }, Entries(runner));
            Assert.AreEqual("work", runner.activeNodeId, "and the state is still the one running");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The structural check does NOT wait for the key: a row pointing at a task that is
        /// no longer there is broken whether or not anything has routed into it yet, and waiting
        /// would hide it until the day something did.</summary>
        [Test]
        public void EntryBinding_OutOfRangeTargetErrorsEvenWithTheKeyAbsent()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 3, "speed", "neverWritten");

            var runner = MakeRunner(MakeTree(work, "EntryBoundTree"), "Zombie");
            LogAssert.Expect(LogType.Error, new Regex("task 3"));
            runner.StartTree();
            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(new[] { "0|0|false|" }, Entries(runner));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ M7i regression

        /// <summary>
        /// The parameter source is UNCHANGED by all of this: applied once, at start, and never
        /// re-applied on entry. Proven with both sources on ONE field, which is the only arrangement
        /// in which "start-time" is observable at all:
        /// <list type="number">
        /// <item>start — the parameter writes 3, the key is absent, so 3 stands;</item>
        /// <item>re-entry with the key at 9 — the key wins, because it is read later;</item>
        /// <item>re-entry with the key GONE — 9 stands. If the parameter row were re-applied on
        /// entry, this line would read 3.</item>
        /// </list>
        /// </summary>
        [Test]
        public void ParameterBinding_AppliesAtStartAndIsNotReAppliedOnEntry()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 0, "speed", Id("speed"));
            BindKey(work, 0, "speed", k_DamageKey);

            StateTreeAsset tree = PingPongTree(work);
            tree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();

            runner.context.blackboard[k_DamageKey] = 9f;
            ReEnterWork(runner);

            runner.context.blackboard.Remove(k_DamageKey);
            ReEnterWork(runner);

            CollectionAssert.AreEqual(new[] { "3|0|false|", "9|0|false|", "9|0|false|" },
                Entries(runner));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>And a parameter row on its own still behaves exactly as M7i left it, including
        /// through a re-entry: the value is the declaration's, once, and nothing about the entry path
        /// touches it.</summary>
        [Test]
        public void ParameterBinding_SurvivesReEntryUntouched()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 0, "label", Id("mood"));

            StateTreeAsset tree = PingPongTree(work);
            tree.parameters = Params(Param("mood", GraphTaskParameterKind.String, 0f, "furious"));

            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();
            ReEnterWork(runner);

            CollectionAssert.AreEqual(new[] { "0|0|false|furious", "0|0|false|furious" },
                Entries(runner));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ fixture helpers

        /// <summary>A state carrying one bound task and FOUR blackboard rows — the three numeric-ish
        /// fields off one key and the string field off another — so one log line answers the whole
        /// conversion table for whatever those two keys happen to hold.</summary>
        private StateTreeNodeAsset MakeBoundFieldFanOut(string numberKey, string stringKey)
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            BindKey(work, 0, "speed", numberKey);
            BindKey(work, 0, "count", numberKey);
            BindKey(work, 0, "flag", numberKey);
            BindKey(work, 0, "label", stringKey);
            return work;
        }

        /// <summary>
        /// Two states the test can drive back and forth: <paramref name="work"/> (whose bindings are
        /// what is under test) and an "idle" state to be in while a key is changed. A re-entry is the
        /// only way to observe an entry-time rule twice, and it has to be a REAL one — through a
        /// transition, with the tasks exited and entered again — because that is the path the rule
        /// lives on.
        /// </summary>
        private StateTreeAsset PingPongTree(StateTreeNodeAsset work)
        {
            AddTransition(work, "idle", MakeFlagCondition(k_ToIdleKey), true);
            var idle = MakeNode("idle", MakeTask("idle"));
            AddTransition(idle, "work", MakeFlagCondition(k_ToWorkKey), true);

            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(idle);
            return MakeTree(root, "PingPongTree");
        }

        /// <summary>Out of the working state and back into it — one full re-entry, with the flags
        /// lowered again so the next tick is the test's to spend.</summary>
        private static void ReEnterWork(StateTreeRunner runner)
        {
            Toggle(runner, k_ToIdleKey);
            Toggle(runner, k_ToWorkKey);
        }

        private static void Toggle(StateTreeRunner runner, string flagKey)
        {
            runner.context.blackboard[flagKey] = true;
            runner.TickTree(0.1f);
            runner.context.blackboard[flagKey] = false;
        }

        /// <summary>Every entry of the bound task, in order, with the "bound:enter:" prefix and the
        /// ping-pong's own bookkeeping filtered out — so a case reads as the list of values the field
        /// held on each entry, which is exactly what these tests are about.</summary>
        private static List<string> Entries(StateTreeRunner runner)
        {
            const string prefix = "bound:enter:";
            var entries = new List<string>();
            List<string> log = StateTreeTestLog.Get(runner.context);
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].StartsWith(prefix, System.StringComparison.Ordinal))
                    entries.Add(log[i].Substring(prefix.Length));
            }
            return entries;
        }

        /// <summary>Adds one BLACKBOARD-sourced binding row, the way the inspector's "Blackboard
        /// key…" link would.</summary>
        private static StateTreeFieldBinding BindKey(StateTreeNodeAsset node, int targetIndex,
            string fieldName, string blackboardKey,
            StateTreeFieldBinding.TargetKind targetKind = StateTreeFieldBinding.TargetKind.Task)
        {
            var binding = new StateTreeFieldBinding
            {
                targetKind = targetKind,
                targetIndex = targetIndex,
                fieldName = fieldName,
                sourceKind = StateTreeFieldBinding.SourceKind.BlackboardKey,
                blackboardKey = blackboardKey
            };
            node.bindings.Add(binding);
            return binding;
        }

        /// <summary>Adds one PARAMETER-sourced row (M7i) — the regression cases need both kinds on
        /// one node.</summary>
        private static StateTreeFieldBinding Bind(StateTreeNodeAsset node, int targetIndex,
            string fieldName, string parameterId)
        {
            var binding = new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = targetIndex,
                fieldName = fieldName,
                sourceKind = StateTreeFieldBinding.SourceKind.Parameter,
                parameterId = parameterId
            };
            node.bindings.Add(binding);
            return binding;
        }

        private static TransitionOutputRoute Route(StateTreeTransition transition, int taskIndex,
            string outputName, string blackboardKey)
        {
            var route = new TransitionOutputRoute
            {
                taskIndex = taskIndex,
                outputName = outputName,
                blackboardKey = blackboardKey
            };
            transition.outputRoutes.Add(route);
            return route;
        }

        /// <summary>The M7i test identity: derived from the name, so a declaration and a row created
        /// from the same name agree without either case having to say so.</summary>
        private static string Id(string name)
        {
            return string.IsNullOrEmpty(name) ? null : "pid-" + name;
        }

        private static GraphTaskParameter Param(string name, GraphTaskParameterKind kind,
            float floatValue = 0f, string stringValue = null)
        {
            return new GraphTaskParameter
            {
                name = name, kind = kind, floatValue = floatValue, stringValue = stringValue,
                id = Id(name)
            };
        }

        private static List<GraphTaskParameter> Params(params GraphTaskParameter[] declared)
        {
            return new List<GraphTaskParameter>(declared);
        }

        private StubBoundFieldTask MakeBoundTask(string id)
        {
            var task = ScriptableObject.CreateInstance<StubBoundFieldTask>();
            task.name = id + "Bound";
            task.taskId = id;
            m_Assets.Add(task);
            return task;
        }

        /// <summary>The M7j producer, finishing on its first tick — the source end of the
        /// route → key → field case.</summary>
        private StubOutputTask MakeOutputTask(string id, float emitAmount)
        {
            var task = ScriptableObject.CreateInstance<StubOutputTask>();
            task.name = id + "Output";
            task.taskId = id;
            task.emitAmount = emitAmount;
            task.finishOnTick = 1;
            m_Assets.Add(task);
            return task;
        }

        private StubRecordingTask MakeTask(string id)
        {
            var task = ScriptableObject.CreateInstance<StubRecordingTask>();
            task.name = id + "Task";
            task.taskId = id;
            m_Assets.Add(task);
            return task;
        }

        private StubFlagCondition MakeFlagCondition(string flagKey)
        {
            var condition = ScriptableObject.CreateInstance<StubFlagCondition>();
            condition.name = flagKey + "Condition";
            condition.flagKey = flagKey;
            m_Assets.Add(condition);
            return condition;
        }

        private StateTreeNodeAsset MakeNode(string nodeId, params StateTreeTaskAsset[] tasks)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.name = nodeId;
            node.nodeId = nodeId;
            node.displayName = nodeId;
            if (tasks != null)
                node.tasks.AddRange(tasks);
            m_Assets.Add(node);
            return node;
        }

        private static StateTreeTransition AddTransition(StateTreeNodeAsset source,
            string targetNodeId, StateTreeConditionAsset condition, bool checkWhileRunning)
        {
            var transition = new StateTreeTransition
            {
                targetNodeId = targetNodeId,
                condition = condition,
                checkWhileRunning = checkWhileRunning
            };
            source.transitions.Add(transition);
            return transition;
        }

        private StateTreeAsset MakeTree(StateTreeNodeAsset root, string treeName)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = treeName;
            tree.treeName = treeName;
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        /// <summary>Runner on an INACTIVE GameObject: Awake/Start/Update never run, so the test owns
        /// every tick — and the context exists before StartTree, which is what lets a case seed the
        /// blackboard the entry bindings will read.</summary>
        private StateTreeRunner MakeRunner(StateTreeAsset tree, string ownerName)
        {
            var go = new GameObject(ownerName);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            m_Objects.Add(go);

            var runner = go.AddComponent<StateTreeRunner>();
            runner.autoStart = false;
            runner.data = tree;
            runner.ownerObject = go;
            runner.context = new StateTreeContext(go);
            return runner;
        }
    }
}

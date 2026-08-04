using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Task stub that reports WHAT IT FOUND on the blackboard under one key, boxed type included,
    /// and can overwrite it. The parameter tests need both halves: the seeded value has to be read
    /// from INSIDE the sub-tree (the caller reading its own blackboard would prove nothing about
    /// the child seeing it, and nothing about the ordering against the child's OnEnter), and the
    /// re-entry case needs the child to move the key first.
    ///
    /// It logs the runtime type name because the boxed type IS the contract on a
    /// <c>Dictionary&lt;string, object&gt;</c>: "Single(1)" and "Boolean(True)" are the same value
    /// to a human and a different one to <c>StateTreeLibraryUtil.TryGetFloat</c>, which accepts the
    /// first and rejects the second (StateTreeLibraryUtil.cs:164-177). Putting it in the trace
    /// makes a regression there read as a failing log line rather than as a mystery.
    ///
    /// It lives in this file rather than beside the other stubs because agent-vm3 owns exactly
    /// three files this round — the same reason <see cref="GraphCountingTask"/> lives in
    /// GraphTaskTests.cs.
    /// </summary>
    internal sealed class StubBlackboardReadTask : StateTreeTaskAsset
    {
        /// <summary>Prefix of every log entry this task writes.</summary>
        public string taskId = "read";

        /// <summary>Blackboard key to report.</summary>
        public string key = "";

        /// <summary>Overwrite <see cref="key"/> with <see cref="writeValue"/> after reporting it
        /// on tick — the "the child moved the key mid-run" half of the re-entry case.</summary>
        public bool writeOnTick;

        public float writeValue;

        public override void OnEnter(StateTreeContext context)
        {
            StateTreeTestLog.Record(context, taskId + ":enter:" + Describe(context));
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            StateTreeTestLog.Record(context, taskId + ":tick:" + Describe(context));
            if (writeOnTick)
                context.blackboard[key] = writeValue;
            return StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            StateTreeTestLog.Record(context, taskId + ":exit:" + status);
        }

        /// <summary>"Single(3)" / "String(calm)" / "&lt;absent&gt;".</summary>
        private string Describe(StateTreeContext context)
        {
            object value;
            if (context == null || !context.blackboard.TryGetValue(key, out value) || value == null)
                return "<absent>";
            return value.GetType().Name + "("
                + System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                + ")";
        }
    }

    /// <summary>
    /// EditMode coverage of <see cref="RunSubTreeTask"/> — a tree running as a task inside
    /// another tree (M7c). Same rules as <see cref="StateTreeRunnerTests"/>: trees are built in
    /// memory (no AssetDatabase), runners live on INACTIVE GameObjects with autoStart off so
    /// every tick is explicit, and the stubs record into the context log.
    ///
    /// The shared <see cref="StateTreeContext"/> is what makes those stubs work here at all: a
    /// sub-tree runs on its PARENT's context, so a child task's log entries land in the same
    /// list as the parent's. That is the property the composition depends on, so the tests read
    /// one log per runner and expect parent and child entries interleaved in it.
    ///
    /// Two of the four cases drive the task directly (OnEnter/OnTick/OnExit) rather than through
    /// a runner: for the guard cases the task's own returned status IS the assertion, and a
    /// parent tree would only hide it behind a transition.
    ///
    /// The PARAMETERS section (M7g) is the same shape one level up: a sub-tree's declared
    /// parameters are the arguments of the call, and the shared blackboard is the only channel
    /// they can travel through — so every one of those cases reads the value from inside the
    /// sub-tree, through <see cref="StubBlackboardReadTask"/>, rather than from the caller's own
    /// dictionary.
    /// </summary>
    [TestFixture]
    public sealed class SubTreeTaskTests
    {
        private const string k_InterruptKey = "interrupt";
        private const string k_GateKey = "gate";

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

        // ------------------------------------------------------------------ required case (a)

        /// <summary>The sub-tree reaches a state named in successStates, so the composite task
        /// Succeeds and the PARENT's completion transition fires in the same tick.</summary>
        [Test]
        public void SubTreeReachingSuccessState_CompletesParentTaskWithSuccess()
        {
            StateTreeAsset childTree = MakeAttackThenSuccessTree("AttackTree");

            var fight = MakeNode("fight", MakeSubTreeTask(childTree));
            AddTransition(fight, "done", null, false);
            var done = MakeNode("done", MakeTask("done"));
            var parentRoot = MakeNode("parentRoot");
            parentRoot.children.Add(fight);
            parentRoot.children.Add(done);

            var runner = MakeRunner(MakeTree(parentRoot, "ParentTree"), "Zombie");
            runner.StartTree();
            Assert.AreEqual("fight", runner.activeNodeId);
            CollectionAssert.AreEqual(new[] { "attack:enter" }, Log(runner),
                "entering the parent state must start the sub-tree, on the parent's context");

            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:exit:Success", "done:enter" },
                Log(runner));
            Assert.AreEqual("done", runner.activeNodeId,
                "the composite task Succeeded, so the parent's completion transition must fire");
            Assert.IsFalse(runner.context.domainContext.ContainsKey(RunSubTreeTask.depthKey),
                "the depth marker must be popped when the composite task exits");
        }

        /// <summary>Mirror of the above through failureStates — the sub-tree ending in "fail"
        /// must not read as Success (both lists are consulted, in that order).</summary>
        [Test]
        public void SubTreeReachingFailureState_EndsParentTaskWithFailure()
        {
            var attack = MakeNode("attack", MakeTask("attack", 1, StateTreeStatus.Failure));
            AddTransition(attack, "fail", null, false);
            var fail = MakeNode("fail");
            var childRoot = MakeNode("childRoot");
            childRoot.children.Add(attack);
            childRoot.children.Add(fail);
            StateTreeAsset childTree = MakeTree(childRoot, "AttackTree");

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);
            Assert.AreEqual(StateTreeStatus.Failure, task.OnTick(context, 0.1f));
            task.OnExit(context, StateTreeStatus.Failure);

            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:exit:Failure" }, Log(context));
        }

        // ------------------------------------------------------------------ required case (b)

        /// <summary>THE composition test: an interrupt in the PARENT tree must reach the task
        /// running inside the SUB-tree as OnExit(Cancelled). Without that chain a composite task
        /// would leave nav goals, timers and spawned VFX behind every time a state is pre-empted
        /// — the exact failure the Cancelled status exists to prevent (M6 exit criterion, one
        /// level deeper).</summary>
        [Test]
        public void ParentInterrupt_CancelsTaskRunningInsideSubTree()
        {
            StateTreeAsset childTree = MakeTree(MakeNode("attack", MakeTask("attack")), "AttackTree");

            var fight = MakeNode("fight", MakeSubTreeTask(childTree));
            AddTransition(fight, "idle", MakeFlagCondition(k_InterruptKey), true);
            var idle = MakeNode("idle", MakeTask("idle"));
            var parentRoot = MakeNode("parentRoot");
            parentRoot.children.Add(fight);
            parentRoot.children.Add(idle);

            var runner = MakeRunner(MakeTree(parentRoot, "ParentTree"), "Zombie");
            List<string> events = TrackEvents(runner);
            runner.StartTree();

            runner.TickTree(0.1f);
            Assert.AreEqual("fight", runner.activeNodeId, "the sub-tree task is still Running");
            Assert.AreEqual(1, ReadDepth(runner.context), "the sub-tree is one level deep while it runs");

            runner.context.blackboard[k_InterruptKey] = true;
            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:exit:Cancelled", "idle:enter" },
                Log(runner),
                "the parent interrupt must cancel the task running inside the sub-tree");
            Assert.AreEqual("idle", runner.activeNodeId);
            CollectionAssert.AreEqual(
                new[] { "started", "entered:fight", "changed:->fight", "left:fight", "entered:idle",
                    "changed:fight->idle" },
                events,
                "the sub-tree runs privately: only the parent's own states reach the runner events");
            Assert.IsFalse(runner.context.domainContext.ContainsKey(RunSubTreeTask.depthKey));
        }

        /// <summary>Same chain from the other end: StopTree on the parent cancels the sub-tree
        /// too, so tearing down an entity never leaves a composite half-running.</summary>
        [Test]
        public void StopTree_CancelsTaskRunningInsideSubTree()
        {
            StateTreeAsset childTree = MakeTree(MakeNode("attack", MakeTask("attack")), "AttackTree");
            var root = MakeNode("fight", MakeSubTreeTask(childTree));

            var runner = MakeRunner(MakeTree(root, "ParentTree"), "Zombie");
            runner.StartTree();
            runner.TickTree(0.1f);

            runner.StopTree();

            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:exit:Cancelled" }, Log(runner));
            Assert.IsFalse(runner.isRunning);
        }

        // ------------------------------------------------------------------ required case (c)

        /// <summary>One context, both directions: the sub-tree's task writes into the caller's
        /// blackboard (the log list lives there), and a value the caller writes AFTER the
        /// sub-tree started is seen by a condition inside it on the next tick.</summary>
        [Test]
        public void SharedContext_BlackboardIsVisibleInBothDirections()
        {
            var work = MakeNode("work", MakeTask("work"));
            var success = MakeNode("success");
            work.children.Add(success);
            AddTransition(work, "success", MakeFlagCondition(k_GateKey), true);
            StateTreeAsset childTree = MakeTree(work, "GatedTree");

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);
            CollectionAssert.AreEqual(new[] { "work:enter" }, Log(context),
                "child -> parent: the sub-tree's task wrote into the caller's blackboard");
            Assert.AreEqual(1, ReadDepth(context));

            Assert.AreEqual(StateTreeStatus.Running, task.OnTick(context, 0.1f));

            context.blackboard[k_GateKey] = true;
            StateTreeStatus status = task.OnTick(context, 0.1f);

            Assert.AreEqual(StateTreeStatus.Success, status,
                "parent -> child: the condition inside the sub-tree reads the caller's key");
            CollectionAssert.AreEqual(
                new[] { "work:enter", "work:tick1", "work:exit:Cancelled" }, Log(context),
                "the sub-tree's own interrupt cancels its running task before the success state");

            task.OnExit(context, status);
            Assert.IsFalse(context.domainContext.ContainsKey(RunSubTreeTask.depthKey),
                "the depth marker is popped on exit, not left for the next composite");
        }

        // ------------------------------------------------------------------ required case (d)

        /// <summary>A tree that runs itself is refused BEFORE anything is copied: one error, a
        /// Failure the parent state can transition away from, and no recursion at all.</summary>
        [Test]
        public void SelfReferencingSubTree_AbortsWithFailure()
        {
            RunSubTreeTask task = MakeSubTreeTask(null);
            StateTreeAsset tree = MakeTree(MakeNode("loop", task), "SelfTree");
            task.subTree = tree;
            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Error,
                "RunSubTreeTask: 'SelfTree' runs itself (directly or through another tree), aborting");
            task.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Failure, task.OnTick(context, 0.1f));
            // A second tick must stay silent — an unexpected error here fails the test, which is
            // how "logged once, on entry" is asserted.
            Assert.AreEqual(StateTreeStatus.Failure, task.OnTick(context, 0.1f));
            Assert.IsFalse(context.domainContext.ContainsKey(RunSubTreeTask.depthKey),
                "a refused entry must not push a depth level");
            CollectionAssert.IsEmpty(Log(context), "nothing of the tree may have run");

            task.OnExit(context, StateTreeStatus.Failure);
        }

        /// <summary>The same guard for an indirect cycle (A runs B, B runs A) — the walk is a
        /// path check over the authored graph, not a self-comparison.</summary>
        [Test]
        public void MutuallyRecursiveSubTrees_AbortWithFailure()
        {
            RunSubTreeTask innerTask = MakeSubTreeTask(null);
            StateTreeAsset treeB = MakeTree(MakeNode("b", innerTask), "TreeB");
            RunSubTreeTask outerTask = MakeSubTreeTask(treeB);
            StateTreeAsset treeA = MakeTree(MakeNode("a", outerTask), "TreeA");
            innerTask.subTree = treeA;

            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Error,
                "RunSubTreeTask: 'TreeB' runs itself (directly or through another tree), aborting");
            outerTask.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Failure, outerTask.OnTick(context, 0.1f));
            outerTask.OnExit(context, StateTreeStatus.Failure);
        }

        /// <summary>Unassigned sub-tree: one error, Failure, no executor. Authoring mistake, not
        /// a crash.</summary>
        [Test]
        public void MissingSubTree_AbortsWithFailure()
        {
            RunSubTreeTask task = MakeSubTreeTask(null);
            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Error, "RunSubTreeTask: no sub tree assigned");
            task.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Failure, task.OnTick(context, 0.1f));
            task.OnExit(context, StateTreeStatus.Failure);
        }

        /// <summary>Two entities on the same composite own separate sub-tree copies: the deep
        /// copy the runner makes covers the composite task, and the executor it creates deep-
        /// copies the sub-tree in turn.</summary>
        [Test]
        public void DeepCopy_TwoRunnersDoNotShareSubTreeState()
        {
            StateTreeAsset childTree = MakeTree(MakeNode("attack", MakeTask("attack")), "AttackTree");
            var root = MakeNode("fight", MakeSubTreeTask(childTree));
            StateTreeAsset parentTree = MakeTree(root, "ParentTree");

            var first = MakeRunner(parentTree, "ZombieA");
            var second = MakeRunner(parentTree, "ZombieB");
            first.StartTree();
            second.StartTree();
            first.TickTree(0.1f);
            second.TickTree(0.1f);
            first.TickTree(0.1f);
            second.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:tick2" }, Log(first));
            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:tick2" }, Log(second));
        }

        // ------------------------------------------------------------------ M7g parameters

        /// <summary>A tree declares "speed", nobody overrides it, and the task inside the SUB-tree
        /// reads the declared default off the shared blackboard — at its own OnEnter, which is the
        /// ordering half of the claim: seeding happens before the child machine starts, not after
        /// its entry state has already run.</summary>
        [Test]
        public void DeclaredParameter_DefaultIsSeededBeforeTheChildTaskEnters()
        {
            StateTreeAsset childTree = MakeReaderTree("speed");
            childTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);

            CollectionAssert.AreEqual(new[] { "speed:enter:Single(3)" }, Log(context),
                "the child's OnEnter must already see the declared default");
            Assert.AreEqual(3f, SeededFloat(context, "speed"));

            task.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The Blueprint-instance model: the caller's ENABLED row wins over the tree's
        /// declared default, and an unchecked row is not a value — it falls through to the
        /// default, so a state that overrides one parameter does not freeze the others.</summary>
        [TestCase(true, 9f)]
        [TestCase(false, 3f)]
        public void EnabledOverride_WinsOverTheDeclaredDefault(bool enabled, float expected)
        {
            StateTreeAsset childTree = MakeReaderTree("speed");
            childTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("speed", enabled, 9f));
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);

            CollectionAssert.AreEqual(new[] { "speed:enter:Single(" + expected + ")" }, Log(context));
            Assert.AreEqual(expected, SeededFloat(context, "speed"));
            Assert.AreEqual(3f, childTree.parameters[0].floatValue,
                "the authored tree must never be written to");

            task.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>Arguments do not persist across calls. The sub-tree counts the key somewhere
        /// else while it runs; re-entering the state must hand it the configured value again, not
        /// whatever the previous run left behind.</summary>
        [Test]
        public void ReEntry_ReSeedsAfterTheChildMutatedTheKey()
        {
            StateTreeAsset childTree = MakeReaderTree("speed", writeOnTick: true, writeValue: 99f);
            childTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("speed", true, 7f));
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);
            Assert.AreEqual(7f, SeededFloat(context, "speed"), "the override seeded the first run");

            Assert.AreEqual(StateTreeStatus.Running, task.OnTick(context, 0.1f));
            Assert.AreEqual(99f, SeededFloat(context, "speed"), "the child moved the key mid-run");
            task.OnExit(context, StateTreeStatus.Cancelled);
            Assert.AreEqual(99f, SeededFloat(context, "speed"),
                "and exiting leaves it there — v1 does not save/restore the caller's keys");

            task.OnEnter(context);

            Assert.AreEqual(7f, SeededFloat(context, "speed"), "re-entry re-seeds the effective value");
            CollectionAssert.AreEqual(
                new[] { "speed:enter:Single(7)", "speed:tick:Single(7)", "speed:exit:Cancelled",
                    "speed:enter:Single(7)" },
                Log(context),
                "the second activation's child reads 7, not the 99 it wrote itself");

            task.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>A tree gets re-authored long after the states that call it were configured, so
        /// a row naming a renamed parameter is wear, not a fault: skipped, never seeded as a key of
        /// its own, the surviving rows still applied — and WARNED ONCE, because seeding runs on
        /// every activation and a state re-entered every second must not flood the console.</summary>
        [Test]
        public void StaleOverrideName_WarnsOnceAndIsSkipped()
        {
            StateTreeAsset childTree = MakeReaderTree("speed");
            childTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("spede", true, 99f), Override("speed", true, 7f));
            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Warning, new Regex("'spede'"));
            task.OnEnter(context);

            Assert.AreEqual(7f, SeededFloat(context, "speed"),
                "the stale row is dropped, the good one still applies");
            Assert.IsFalse(context.blackboard.ContainsKey("spede"),
                "an undeclared name must not become a blackboard key of its own");
            task.OnExit(context, StateTreeStatus.Cancelled);

            // Second activation: same stale row, no second warning.
            task.OnEnter(context);
            task.OnExit(context, StateTreeStatus.Cancelled);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The other two kinds, and the boxed type each one lands as — the part of this that is a
        /// real decision rather than plumbing. A String seeds as a <c>string</c>; a Bool seeds as a
        /// <c>float</c> 1/0, NOT as a boxed <c>bool</c>, because
        /// <c>StateTreeLibraryUtil.TryGetFloat</c> accepts float/int/double and nothing else
        /// (StateTreeLibraryUtil.cs:164-177) and that is the path
        /// <see cref="BlackboardCompareCondition"/> reads through — a boxed bool would make every
        /// transition gated on a declared Bool parameter read false forever, silently. Asserting
        /// the runtime type is what pins that down: the values compare equal either way.
        ///
        /// Both kinds run twice on ONE task — defaults first, then with overrides — which also
        /// pins down that a second activation re-derives from the declarations instead of layering
        /// onto what the last one left.
        /// </summary>
        [Test]
        public void StringAndBoolParameters_SeedTheBoxedTypesTheLibraryReads()
        {
            var work = MakeNode("work",
                MakeReader("mood", "mood"),
                MakeReader("angry", "angry"));
            StateTreeAsset childTree = MakeTree(work, "MoodTree");
            childTree.parameters = Params(
                Param("mood", GraphTaskParameterKind.String, 0f, "calm"),
                Param("angry", GraphTaskParameterKind.Bool));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);
            Assert.AreEqual("calm", SeededString(context, "mood"));
            Assert.AreEqual(0f, SeededFloat(context, "angry"), "a Bool default of false is 0f");
            task.OnExit(context, StateTreeStatus.Cancelled);

            task.overrides = Overrides(
                Override("mood", true, 0f, "furious"),
                Override("angry", true, 1f));
            task.OnEnter(context);

            Assert.AreEqual("furious", SeededString(context, "mood"));
            Assert.AreEqual(1f, SeededFloat(context, "angry"), "a Bool override of true is 1f");
            CollectionAssert.AreEqual(
                new[] { "mood:enter:String(calm)", "angry:enter:Single(0)",
                    "mood:exit:Cancelled", "angry:exit:Cancelled",
                    "mood:enter:String(furious)", "angry:enter:Single(1)" },
                Log(context));

            task.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The declaration has to survive <see cref="StateTreeAsset.DeepCopy"/> — the
        /// copy every executor runs — or a tree would forget its own contract the moment it was
        /// started. Instantiate clones SERIALIZED data and a <c>List&lt;[Serializable] class&gt;</c>
        /// is serialized data, so the copy must come out with its own rows: same values, separate
        /// objects, and writing to one must not reach the other.</summary>
        [Test]
        public void DeclaredParameters_SurviveDeepCopyAsIndependentRows()
        {
            StateTreeAsset tree = MakeTree(MakeNode("work", MakeTask("work")), "DeclaringTree");
            tree.parameters = Params(
                Param("speed", GraphTaskParameterKind.Float, 3f),
                Param("mood", GraphTaskParameterKind.String, 0f, "calm"),
                Param("angry", GraphTaskParameterKind.Bool, 1f));

            StateTreeAsset copy = tree.DeepCopy();
            try
            {
                Assert.IsNotNull(copy.parameters);
                Assert.AreEqual(3, copy.parameters.Count, "every declared row survived the copy");
                for (int i = 0; i < tree.parameters.Count; i++)
                {
                    GraphTaskParameter authored = tree.parameters[i];
                    GraphTaskParameter copied = copy.parameters[i];
                    Assert.AreEqual(authored.name, copied.name);
                    Assert.AreEqual(authored.kind, copied.kind);
                    Assert.AreEqual(authored.floatValue, copied.floatValue);
                    Assert.AreEqual(authored.stringValue, copied.stringValue);
                    Assert.AreNotSame(authored, copied, "row " + i + " must be a copy, not an alias");
                }
                Assert.AreNotSame(tree.parameters, copy.parameters);

                copy.parameters[0].floatValue = 99f;
                Assert.AreEqual(3f, tree.parameters[0].floatValue,
                    "writing to a running copy must never reach the asset on disk");
            }
            finally
            {
                StateTreeAsset.DestroyCopy(copy);
            }
        }

        // ------------------------------------------------------------------ fixture helpers

        /// <summary>Sub-tree shape used by the success case: an attack state that finishes, then
        /// a terminal "success" state carrying no tasks at all.</summary>
        private StateTreeAsset MakeAttackThenSuccessTree(string treeName)
        {
            var attack = MakeNode("attack", MakeTask("attack", 1, StateTreeStatus.Success));
            AddTransition(attack, "success", null, false);
            var success = MakeNode("success");
            var childRoot = MakeNode("childRoot");
            childRoot.children.Add(attack);
            childRoot.children.Add(success);
            return MakeTree(childRoot, treeName);
        }

        private RunSubTreeTask MakeSubTreeTask(StateTreeAsset subTree)
        {
            var task = ScriptableObject.CreateInstance<RunSubTreeTask>();
            task.name = "SubTreeTask";
            task.subTree = subTree;
            m_Assets.Add(task);
            return task;
        }

        private StubRecordingTask MakeTask(string id, int finishOnTick = 0,
            StateTreeStatus finishStatus = StateTreeStatus.Success)
        {
            var task = ScriptableObject.CreateInstance<StubRecordingTask>();
            task.name = id + "Task";
            task.taskId = id;
            task.finishOnTick = finishOnTick;
            task.finishStatus = finishStatus;
            m_Assets.Add(task);
            return task;
        }

        /// <summary>Single-state sub-tree whose one task reports <paramref name="key"/>.</summary>
        private StateTreeAsset MakeReaderTree(string key, bool writeOnTick = false,
            float writeValue = 0f)
        {
            var work = MakeNode("work", MakeReader(key, key, writeOnTick, writeValue));
            return MakeTree(work, "ReaderTree");
        }

        private StubBlackboardReadTask MakeReader(string id, string key, bool writeOnTick = false,
            float writeValue = 0f)
        {
            var task = ScriptableObject.CreateInstance<StubBlackboardReadTask>();
            task.name = id + "Reader";
            task.taskId = id;
            task.key = key;
            task.writeOnTick = writeOnTick;
            task.writeValue = writeValue;
            m_Assets.Add(task);
            return task;
        }

        private static GraphTaskParameter Param(string name, GraphTaskParameterKind kind,
            float floatValue = 0f, string stringValue = null)
        {
            return new GraphTaskParameter
            {
                name = name, kind = kind, floatValue = floatValue, stringValue = stringValue
            };
        }

        private static List<GraphTaskParameter> Params(params GraphTaskParameter[] declared)
        {
            return new List<GraphTaskParameter>(declared);
        }

        private static GraphTaskParameterOverride Override(string name, bool enabled,
            float floatValue = 0f, string stringValue = null)
        {
            return new GraphTaskParameterOverride
            {
                name = name, enabled = enabled, floatValue = floatValue, stringValue = stringValue
            };
        }

        private static List<GraphTaskParameterOverride> Overrides(
            params GraphTaskParameterOverride[] rows)
        {
            return new List<GraphTaskParameterOverride>(rows);
        }

        /// <summary>The seeded value AND its boxed type — see
        /// <see cref="StringAndBoolParameters_SeedTheBoxedTypesTheLibraryReads"/> for why the type
        /// is asserted rather than just the number.</summary>
        private static float SeededFloat(StateTreeContext context, string key)
        {
            object value = Seeded(context, key);
            Assert.IsInstanceOf<float>(value,
                "'" + key + "' must be boxed as a float — StateTreeLibraryUtil.TryGetFloat "
                + "(the BlackboardCompareCondition path) reads float/int/double and nothing else");
            return (float)value;
        }

        private static string SeededString(StateTreeContext context, string key)
        {
            object value = Seeded(context, key);
            Assert.IsInstanceOf<string>(value, "'" + key + "' must be boxed as a string");
            return (string)value;
        }

        private static object Seeded(StateTreeContext context, string key)
        {
            object value;
            Assert.IsTrue(context.blackboard.TryGetValue(key, out value),
                "the blackboard carries no key '" + key + "'");
            return value;
        }

        private StubFlagCondition MakeFlagCondition(string flagKey, bool invert = false)
        {
            var condition = ScriptableObject.CreateInstance<StubFlagCondition>();
            condition.name = flagKey + "Condition";
            condition.flagKey = flagKey;
            condition.invert = invert;
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

        private static StateTreeTransition AddTransition(StateTreeNodeAsset source, string targetNodeId,
            StateTreeConditionAsset condition, bool checkWhileRunning)
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

        /// <summary>Runner on an INACTIVE GameObject: Awake/Start/Update never run, so the test
        /// owns every tick.</summary>
        private StateTreeRunner MakeRunner(StateTreeAsset tree, string ownerName)
        {
            var go = MakeOwner(ownerName);
            var runner = go.AddComponent<StateTreeRunner>();
            runner.autoStart = false;
            runner.data = tree;
            runner.ownerObject = go;
            runner.context = new StateTreeContext(go);
            return runner;
        }

        /// <summary>Context for the directly-driven cases — what a parent runner would hand the
        /// task, minus the parent.</summary>
        private StateTreeContext MakeContext(string ownerName)
        {
            return new StateTreeContext(MakeOwner(ownerName));
        }

        private GameObject MakeOwner(string ownerName)
        {
            var go = new GameObject(ownerName);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            m_Objects.Add(go);
            return go;
        }

        private static int ReadDepth(StateTreeContext context)
        {
            object value;
            if (context.domainContext.TryGetValue(RunSubTreeTask.depthKey, out value) && value is int)
                return (int)value;
            return 0;
        }

        private static List<string> Log(StateTreeRunner runner)
        {
            return StateTreeTestLog.Get(runner.context);
        }

        private static List<string> Log(StateTreeContext context)
        {
            return StateTreeTestLog.Get(context);
        }

        private static List<string> TrackEvents(StateTreeRunner runner)
        {
            var events = new List<string>();
            runner.treeStarted += () => events.Add("started");
            runner.treeStopped += () => events.Add("stopped");
            runner.nodeEntered += id => events.Add("entered:" + id);
            runner.nodeLeft += id => events.Add("left:" + id);
            runner.activeNodeChanged += (from, to) => events.Add("changed:" + from + "->" + to);
            return events;
        }
    }
}

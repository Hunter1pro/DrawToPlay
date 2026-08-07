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
    /// Task stub with one PUBLIC FIELD PER BINDABLE KIND, reporting all four whenever it is entered.
    /// The M7i binding cases need a target whose fields are the assertion: a binding writes a field
    /// of the DEEP COPY the executor runs, so the authored instance the test holds can never show
    /// what happened — only the copy's own log entry can.
    ///
    /// It reports every field on every entry, not just the bound one, because the interesting
    /// failures are the ones where a row writes the WRONG field or a skipped row leaves its target
    /// at the authored value while the rest of the list still applies. One trace line shows both.
    /// </summary>
    internal sealed class StubBoundFieldTask : StateTreeTaskAsset
    {
        public string taskId = "bound";

        /// <summary>Float parameter target.</summary>
        public float speed;

        /// <summary>Float parameter target of the other accepted numeric type.</summary>
        public int count;

        /// <summary>Bool parameter target.</summary>
        public bool flag;

        /// <summary>String parameter target.</summary>
        public string label = "";

        public override void OnEnter(StateTreeContext context)
        {
            StateTreeTestLog.Record(context, taskId + ":enter:" + Describe());
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            StateTreeTestLog.Record(context, taskId + ":tick:" + Describe());
            return StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            StateTreeTestLog.Record(context, taskId + ":exit:" + status);
        }

        /// <summary>"3|0|false|" — invariant culture on the float, because a machine running under a
        /// comma-decimal locale must fail these tests for a real reason or not at all.</summary>
        private string Describe()
        {
            return System.Convert.ToString(speed, System.Globalization.CultureInfo.InvariantCulture)
                + "|" + count + "|" + (flag ? "true" : "false") + "|" + label;
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
        /// a row whose declaration has been deleted is wear, not a fault: skipped, never seeded as
        /// a key of its own, the surviving rows still applied — and WARNED ONCE, because seeding
        /// runs on every activation and a state re-entered every second must not flood the console.
        /// The row keeps its display name, which is how the warning can name it.</summary>
        [Test]
        public void StaleOverride_WarnsOnceAndIsSkipped()
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
                    // The identity has to survive too, or a running copy would match nothing and
                    // every override in the tree would read as stale the moment it started.
                    Assert.AreEqual(authored.id, copied.id, "row " + i + " kept its identity");
                    Assert.IsNotEmpty(copied.id, "row " + i + " has an identity at all");
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

        // ------------------------------------------------------------------ identity (M7h)
        //
        // An override binds to a declaration by ID. The NAME stays the blackboard key the sub-tree
        // reads by — the child reads it as a hand-typed string and always did — but it is never a
        // matching key. These four cases are the rule from both sides, plus the two ways a row can
        // be bound to nothing.

        /// <summary>THE identity rule: the id decides, so a row whose display name has gone out of
        /// date still applies. Name-keyed matching would drop this caller back to the declared
        /// default the moment someone retyped a parameter in the sub-tree — silently, across every
        /// state that called it, which is the failure this replaces.</summary>
        [Test]
        public void Identity_OverrideAppliesWhenOnlyTheIdAgrees()
        {
            // The declaration has been renamed to "moveSpeed"; the row was created back when it was
            // called "speed" and nobody rewrote it. Only the id still agrees.
            StateTreeAsset childTree = MakeReaderTree("moveSpeed");
            childTree.parameters = Params(
                Param("moveSpeed", GraphTaskParameterKind.Float, 3f, null, "p-speed"));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("speed", true, 9f, null, "p-speed"));
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);

            Assert.AreEqual(9f, SeededFloat(context, "moveSpeed"),
                "the id bound the row, so the stale display name cost nothing");
            CollectionAssert.AreEqual(new[] { "moveSpeed:enter:Single(9)" }, Log(context));

            task.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The converse, and the half that makes the rule a rule rather than a fallback: a
        /// row that agrees on the NAME and not on the id is stale. It has to be — that shape is a
        /// row left over from a deleted parameter whose name a later one reused, and seeding the
        /// new parameter with it would publish a number the author last typed against something
        /// else entirely.</summary>
        [Test]
        public void Identity_OverrideIsStaleWhenOnlyTheNameAgrees()
        {
            StateTreeAsset childTree = MakeReaderTree("speed");
            childTree.parameters = Params(
                Param("speed", GraphTaskParameterKind.Float, 3f, null, "p-speed"));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("speed", true, 9f, null, "p-deleted"));
            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Warning, new Regex("'speed'"));
            task.OnEnter(context);

            Assert.AreEqual(3f, SeededFloat(context, "speed"),
                "the declared default, not the row's 9");
            task.OnExit(context, StateTreeStatus.Cancelled);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The rename, executed rather than asserted about: one authored tree, one set of
        /// override rows, and the declaration renamed underneath them between two activations. The
        /// SEEDED KEY moves with the name (the child reads it by string), the row moves not at all,
        /// and the value survives — which is visible in the trace as the child going from finding
        /// nothing to finding the caller's 9 under its own key.</summary>
        [Test]
        public void Identity_IdBoundOverrideSurvivesADeclarationRename()
        {
            StateTreeAsset childTree = MakeReaderTree("moveSpeed");
            childTree.parameters = Params(
                Param("speed", GraphTaskParameterKind.Float, 3f, null, "p-speed"));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("speed", true, 9f, null, "p-speed"));
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);
            Assert.AreEqual(9f, SeededFloat(context, "speed"), "bound by id before the rename");
            task.OnExit(context, StateTreeStatus.Cancelled);

            // The author renames the declaration. Its id does not change, and the caller's row is
            // not touched — that is the whole point of the identity.
            childTree.parameters[0].name = "moveSpeed";

            task.OnEnter(context);

            Assert.AreEqual(9f, SeededFloat(context, "moveSpeed"),
                "the same row still tunes the same parameter, under its new key");
            Assert.AreEqual("speed", task.overrides[0].name,
                "and the rename did not have to rewrite the caller's row to achieve it");
            CollectionAssert.AreEqual(
                new[] { "moveSpeed:enter:<absent>", "moveSpeed:exit:Cancelled",
                    "moveSpeed:enter:Single(9)" },
                Log(context),
                "the child's own read follows the name, and finds the caller's value there");

            task.OnExit(context, StateTreeStatus.Cancelled);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A row bound to nothing — no id at all — is stale by definition: there is no
        /// declaration it can claim to be an override OF. Reported like any other stale row (once,
        /// by whatever label it carries) rather than silently ignored, because the only way to
        /// produce one is a bug in whatever wrote it.</summary>
        [Test]
        public void Identity_RowWithNoIdIsStale()
        {
            StateTreeAsset childTree = MakeReaderTree("speed");
            childTree.parameters = Params(
                Param("speed", GraphTaskParameterKind.Float, 3f, null, "p-speed"));

            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("speed", true, 9f, null, string.Empty));
            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Warning, new Regex("'speed'"));
            task.OnEnter(context);

            Assert.AreEqual(3f, SeededFloat(context, "speed"),
                "an unbound row cannot override anything");
            task.OnExit(context, StateTreeStatus.Cancelled);

            // Once per instance, however many activations re-read the same row.
            task.OnEnter(context);
            task.OnExit(context, StateTreeStatus.Cancelled);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ field bindings (M7i)
        //
        // A declared parameter reaches a task's own FIELD, not just the blackboard. The executor
        // applies the rows to the copy it runs, once per start, which is why every case here reads
        // the value out of the running copy's log rather than off the authored asset.

        /// <summary>THE binding rule: when the tree starts, the bound field of the running copy
        /// already holds the parameter's effective value — before the task is entered, so a task
        /// that acts on OnEnter acts on the bound value. And the AUTHORED asset is untouched, which
        /// is what lets two states share one configured task at two different tunings.</summary>
        [Test]
        public void Binding_TaskFieldHoldsTheEffectiveValueBeforeTheTaskEnters()
        {
            StubBoundFieldTask authored = MakeBoundTask("bound");
            var work = MakeNode("work", authored);
            Bind(work, 0, "speed", Id("speed"));
            StateTreeAsset tree = MakeTree(work, "BoundTree");
            tree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:3|0|false|" }, Log(runner),
                "the running copy entered with its bound field already set");
            Assert.AreEqual(0f, authored.speed,
                "the authored task must never be written to — only the executor's deep copy is");
        }

        /// <summary>Every accepted kind through its own field type, in one tree: a Float into an
        /// <c>int</c> as well as a <c>float</c> (a tuning knob is routinely a count), a Bool into a
        /// <c>bool</c> — a REAL bool here, unlike the blackboard's 1/0, because a field has a
        /// declared type and nothing to disambiguate — and a String into a <c>string</c>.</summary>
        [Test]
        public void Binding_EachKindWritesItsOwnFieldType()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 0, "speed", Id("speed"));
            Bind(work, 0, "count", Id("speed"));
            Bind(work, 0, "flag", Id("angry"));
            Bind(work, 0, "label", Id("mood"));
            StateTreeAsset tree = MakeTree(work, "BoundTree");
            tree.parameters = Params(
                Param("speed", GraphTaskParameterKind.Float, 4f),
                Param("angry", GraphTaskParameterKind.Bool, 1f),
                Param("mood", GraphTaskParameterKind.String, 0f, "furious"));

            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:4|4|true|furious" }, Log(runner));
        }

        /// <summary>A field or a parameter retyped after the row was authored is the one way a live
        /// binding can go wrong, so it is an ERROR (a field silently left at its authored value is
        /// indistinguishable from a binding that worked) — and only THAT row is skipped: the rest of
        /// the list still applies, because one bad wire must not disarm the state.</summary>
        [Test]
        public void Binding_KindMismatchErrorsAndSkipsOnlyThatRow()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 0, "flag", Id("speed"));      // Float into a bool: refused, not converted.
            Bind(work, 0, "label", Id("mood"));      // and this one still lands.
            StateTreeAsset tree = MakeTree(work, "BoundTree");
            tree.parameters = Params(
                Param("speed", GraphTaskParameterKind.Float, 4f),
                Param("mood", GraphTaskParameterKind.String, 0f, "calm"));

            LogAssert.Expect(LogType.Error, new Regex("'flag'"));
            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:0|0|false|calm" }, Log(runner),
                "the mismatched row was skipped and the good one applied");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The runtime half of the index-remap contract: the editor Ops remap binding rows
        /// when a task is removed or a transition reordered, and when they miss one the row must
        /// name a target that is not there — one error, the row skipped, nothing thrown, and the
        /// state still enters and runs. A binding is authoring metadata; losing one cannot be
        /// allowed to lose the state it was authored on.</summary>
        [Test]
        public void Binding_OutOfRangeTargetErrorsAndTheStateStillRuns()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 3, "speed", Id("speed"));
            StateTreeAsset tree = MakeTree(work, "BoundTree");
            tree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            LogAssert.Expect(LogType.Error, new Regex("task 3"));
            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();
            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(new[] { "bound:enter:0|0|false|", "bound:tick:0|0|false|" },
                Log(runner), "the state ran, at the authored value, with nothing bound");
            Assert.AreEqual("work", runner.activeNodeId);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A row bound to a parameter the tree does not declare is the other authoring
        /// mistake with the same consequence, and it is reported the same way — the id, not the
        /// field, being the part that went missing.</summary>
        [Test]
        public void Binding_UnknownParameterIdErrorsAndIsSkipped()
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 0, "speed", "pid-deleted");
            StateTreeAsset tree = MakeTree(work, "BoundTree");
            tree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            LogAssert.Expect(LogType.Error, new Regex("pid-deleted"));
            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:0|0|false|" }, Log(runner));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Transitions are the other half of a state's authored surface, so a condition's
        /// field binds exactly like a task's. Proven through BEHAVIOUR rather than a field read: the
        /// parameter supplies the blackboard key <see cref="StubFlagCondition"/> watches, and the
        /// transition fires on that key and no other.</summary>
        [Test]
        public void Binding_TransitionConditionFieldIsBound()
        {
            var work = MakeNode("work", MakeTask("work"));
            AddTransition(work, "done", MakeFlagCondition("unbound"), true);
            Bind(work, 0, "flagKey", Id("gateKey"),
                StateTreeFieldBinding.TargetKind.TransitionCondition);
            var done = MakeNode("done", MakeTask("done"));
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(done);

            StateTreeAsset tree = MakeTree(root, "GateTree");
            tree.parameters = Params(Param("gateKey", GraphTaskParameterKind.String, 0f, "gate"));

            var runner = MakeRunner(tree, "Zombie");
            runner.StartTree();

            runner.context.blackboard["unbound"] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("work", runner.activeNodeId,
                "the condition no longer watches the key it was authored with");

            runner.context.blackboard["gate"] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("done", runner.activeNodeId,
                "it watches the key the bound parameter named");
        }

        /// <summary>The caller's override reaches the child's bound FIELD, which is the whole point
        /// of the two features together: a state tunes a sub-tree, and what it tuned lands in the
        /// field of a task three assets away without either end knowing about the other.</summary>
        [Test]
        public void Binding_SubTreeOverrideReachesTheChildsBoundField()
        {
            StateTreeAsset childTree = MakeBoundTree("childSpeed", 1f);
            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(Override("childSpeed", true, 9f));
            StateTreeContext context = MakeContext("Zombie");

            task.OnEnter(context);

            CollectionAssert.AreEqual(new[] { "bound:enter:9|0|false|" }, Log(context),
                "the child's bound field holds the caller's value, not the declared 1");
            task.OnExit(context, StateTreeStatus.Cancelled);
        }

        // ------------------------------------------------------------------ pass-through (M7i)
        //
        // An override row may source its value from a parameter of the tree the CALLING STATE lives
        // in, so a value declared at the top of a composition reaches the bottom of it.

        /// <summary>THE pass-through case, end to end and one level deep: the parent tree declares
        /// "speed", the state's override row on the sub-tree call takes its value from it, and the
        /// child's bound field ends up holding the parent's value — never the literal typed into the
        /// row, which is left at a value it could not be confused with.</summary>
        [Test]
        public void PassThrough_ParentParameterReachesTheChildsBoundField()
        {
            StateTreeAsset childTree = MakeBoundTree("childSpeed", 1f);
            RunSubTreeTask call = MakeSubTreeTask(childTree);
            call.overrides = Overrides(
                Override("childSpeed", true, -1f, null, Id("childSpeed"), Id("speed")));

            StateTreeAsset parentTree = MakeTree(MakeNode("call", call), "ParentTree");
            parentTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 7f));

            var runner = MakeRunner(parentTree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:7|0|false|" }, Log(runner),
                "the child read the PARENT's parameter, not the row's own -1");
        }

        /// <summary>The parent's own override travels the same wire: tuning the parent's parameter
        /// re-tunes everything downstream of it, which is what makes a composition worth
        /// parameterising at all. Same fixture as above, one level higher.</summary>
        [Test]
        public void PassThrough_ChainsThroughTwoLevelsOfOverride()
        {
            StateTreeAsset childTree = MakeBoundTree("childSpeed", 1f);
            RunSubTreeTask innerCall = MakeSubTreeTask(childTree);
            innerCall.overrides = Overrides(
                Override("childSpeed", true, -1f, null, Id("childSpeed"), Id("speed")));

            StateTreeAsset middleTree = MakeTree(MakeNode("call", innerCall), "MiddleTree");
            middleTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 7f));

            RunSubTreeTask outerCall = MakeSubTreeTask(middleTree);
            outerCall.overrides = Overrides(Override("speed", true, 12f));
            StateTreeContext context = MakeContext("Zombie");

            outerCall.OnEnter(context);

            CollectionAssert.AreEqual(new[] { "bound:enter:12|0|false|" }, Log(context),
                "the outermost caller's 12 travelled two levels down into a field");
            Assert.AreEqual(12f, SeededFloat(context, "speed"), "the middle tree seeded its own key");
            Assert.AreEqual(12f, SeededFloat(context, "childSpeed"),
                "and the child's key carries what was passed through, not its declared 1");

            outerCall.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The scope is the CALLER's for exactly as long as the caller is the caller: a
        /// sub-tree that declares a parameter with the same id as its parent's must not be able to
        /// feed its own value back into itself. Proven by nesting the same id: the row still reads
        /// 7, the parent's value, although the child declares 1 under that id by the time the child
        /// is running.</summary>
        [Test]
        public void PassThrough_ReadsTheCallersScopeAndNotTheCalleesOwn()
        {
            StateTreeAsset childTree = MakeBoundTree("speed", 1f);
            RunSubTreeTask call = MakeSubTreeTask(childTree);
            call.overrides = Overrides(
                Override("speed", true, -1f, null, Id("speed"), Id("speed")));

            StateTreeAsset parentTree = MakeTree(MakeNode("call", call), "ParentTree");
            parentTree.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 7f));

            var runner = MakeRunner(parentTree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:7|0|false|" }, Log(runner));

            runner.StopTree();
            Assert.IsFalse(runner.context.domainContext.ContainsKey(StateTreeExecutor.paramScopeKey),
                "and both scopes are popped when the trees stop — the context is left as it was");
        }

        /// <summary>A source parameter that no longer exists is wear, not a fault: the row is
        /// skipped so the callee's DECLARED DEFAULT stands (the same thing an unchecked row does),
        /// everything else still applies, and it is a warning said once per instance rather than
        /// once per activation.</summary>
        [Test]
        public void PassThrough_UnknownSourceWarnsOnceAndFallsBackToTheDeclaration()
        {
            StateTreeAsset childTree = MakeBoundTree("childSpeed", 1f);
            RunSubTreeTask task = MakeSubTreeTask(childTree);
            task.overrides = Overrides(
                Override("childSpeed", true, -1f, null, Id("childSpeed"), "pid-gone"));
            StateTreeContext context = MakeContext("Zombie");

            LogAssert.Expect(LogType.Warning, new Regex("'childSpeed'"));
            task.OnEnter(context);

            CollectionAssert.AreEqual(new[] { "bound:enter:1|0|false|" }, Log(context),
                "the declared default, never the row's own -1");
            task.OnExit(context, StateTreeStatus.Cancelled);

            task.OnEnter(context);
            task.OnExit(context, StateTreeStatus.Cancelled);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A source that exists but has been RETYPED is refused for the same reason a
        /// mismatched field binding is: carrying a string into a float parameter would publish a
        /// number the author never chose, and no conversion here is better than a silent one.</summary>
        [Test]
        public void PassThrough_SourceOfADifferentKindIsRefused()
        {
            StateTreeAsset childTree = MakeBoundTree("childSpeed", 1f);
            RunSubTreeTask call = MakeSubTreeTask(childTree);
            call.overrides = Overrides(
                Override("childSpeed", true, -1f, null, Id("childSpeed"), Id("mood")));

            StateTreeAsset parentTree = MakeTree(MakeNode("call", call), "ParentTree");
            parentTree.parameters = Params(
                Param("mood", GraphTaskParameterKind.String, 0f, "furious"));

            LogAssert.Expect(LogType.Warning, new Regex("'childSpeed'"));
            var runner = MakeRunner(parentTree, "Zombie");
            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "bound:enter:1|0|false|" }, Log(runner),
                "the kinds disagree, so the child keeps its declared default");
            LogAssert.NoUnexpectedReceived();
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

        /// <summary>Deterministic stand-in for the identity the inspector generates
        /// (<c>Guid.NewGuid().ToString("N")</c>): readable in a failure message, and — because it
        /// is derived from the name — identical for a declaration and a row created from the same
        /// name. That is what lets every case that is NOT about identity keep saying only "speed"
        /// and still be id-bound, the way real authored data is.</summary>
        private static string Id(string name)
        {
            return string.IsNullOrEmpty(name) ? null : "pid-" + name;
        }

        private static GraphTaskParameter Param(string name, GraphTaskParameterKind kind,
            float floatValue = 0f, string stringValue = "", string id = null)
        {
            // "" rather than null: Unity serialization normalizes null strings to empty on
            // every Instantiate, so a null here would make the deep-copy equality test fail
            // on serialization's behavior instead of DeepCopy's.
            return new GraphTaskParameter
            {
                name = name, kind = kind, floatValue = floatValue, stringValue = stringValue,
                id = id ?? Id(name)
            };
        }

        private static List<GraphTaskParameter> Params(params GraphTaskParameter[] declared)
        {
            return new List<GraphTaskParameter>(declared);
        }

        /// <summary>An override row. <paramref name="id"/> is what it BINDS by;
        /// <paramref name="name"/> is only what the inspector would show — the identity cases pass
        /// the two deliberately out of step. <paramref name="sourceParameterId"/> makes it a
        /// PASS-THROUGH row: the value then comes from the calling tree's parameter of that id and
        /// <paramref name="floatValue"/> is the literal it must NOT use.</summary>
        private static GraphTaskParameterOverride Override(string name, bool enabled,
            float floatValue = 0f, string stringValue = null, string id = null,
            string sourceParameterId = null)
        {
            return new GraphTaskParameterOverride
            {
                name = name, enabled = enabled, floatValue = floatValue, stringValue = stringValue,
                id = id ?? Id(name), sourceParameterId = sourceParameterId
            };
        }

        /// <summary>A task whose four public fields are the four bindable kinds.</summary>
        private StubBoundFieldTask MakeBoundTask(string id)
        {
            var task = ScriptableObject.CreateInstance<StubBoundFieldTask>();
            task.name = id + "Bound";
            task.taskId = id;
            m_Assets.Add(task);
            return task;
        }

        /// <summary>Single-state tree declaring one Float parameter and binding it to the
        /// <see cref="StubBoundFieldTask.speed"/> field of its one task — the smallest shape in
        /// which "a value reached a field" is observable.</summary>
        private StateTreeAsset MakeBoundTree(string parameterName, float declaredDefault)
        {
            var work = MakeNode("work", MakeBoundTask("bound"));
            Bind(work, 0, "speed", Id(parameterName));
            StateTreeAsset tree = MakeTree(work, "BoundTree");
            tree.parameters = Params(
                Param(parameterName, GraphTaskParameterKind.Float, declaredDefault));
            return tree;
        }

        /// <summary>Adds one binding row to a node, the way the inspector's link control would:
        /// the target is an index into the node's own list, the parameter an id.</summary>
        private static StateTreeFieldBinding Bind(StateTreeNodeAsset node, int targetIndex,
            string fieldName, string parameterId,
            StateTreeFieldBinding.TargetKind targetKind = StateTreeFieldBinding.TargetKind.Task)
        {
            var binding = new StateTreeFieldBinding
            {
                targetKind = targetKind,
                targetIndex = targetIndex,
                fieldName = fieldName,
                parameterId = parameterId
            };
            node.bindings.Add(binding);
            return binding;
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

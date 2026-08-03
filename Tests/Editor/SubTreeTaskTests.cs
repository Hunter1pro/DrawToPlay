using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
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

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the ported state_tree_runner.gd semantics (brief §7.1, M6 exit
    /// criterion). Trees are built in memory (ScriptableObject.CreateInstance — no AssetDatabase)
    /// and driven through the public <see cref="StateTreeRunner.TickTree"/> so nothing depends on
    /// play mode: runners live on INACTIVE GameObjects with autoStart off, so Unity never runs
    /// Awake/Start/Update behind the tests' back and every tick is explicit.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeRunnerTests
    {
        private const string k_InterruptKey = "interrupt";
        private const string k_AdvanceKey = "advance";

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

        // ------------------------------------------------------------------ required case 1

        /// <summary>Exit criterion: interrupting a running state fires OnExit(Cancelled).</summary>
        [Test]
        public void Interrupt_ExitsRunningTasksWithCancelled_AndEntersTarget()
        {
            var chaseTask = MakeTask("chase");
            var chase = MakeNode("chase", chaseTask);
            var idle = MakeNode("idle", MakeTask("idle"));
            AddTransition(chase, "idle", MakeFlagCondition(k_InterruptKey), true);
            var root = MakeNode("root");
            root.children.Add(chase);
            root.children.Add(idle);

            var runner = MakeRunner(MakeTree(root), "Zombie");
            List<string> events = TrackEvents(runner);
            runner.StartTree();

            runner.TickTree(0.1f);
            Assert.AreEqual("chase", runner.activeNodeId, "interrupt condition is false, chase must keep running");

            runner.context.blackboard[k_InterruptKey] = true;
            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[] { "chase:enter", "chase:tick1", "chase:exit:Cancelled", "idle:enter" },
                Log(runner));
            Assert.AreEqual("idle", runner.activeNodeId);
            Assert.IsTrue(runner.isRunning);
            CollectionAssert.AreEqual(
                new[] { "started", "entered:chase", "changed:->chase", "left:chase", "entered:idle", "changed:chase->idle" },
                events);
        }

        // ------------------------------------------------------------------ required case 2

        /// <summary>Tick order: interrupts are evaluated BEFORE task ticks, so a task that would
        /// have returned Success this tick is cancelled instead and never ticks at all. Both
        /// branches are driven off one authored tree to make the ordering unambiguous.</summary>
        [Test]
        public void TickOrder_InterruptEvaluatedBeforeTaskTick()
        {
            var attackTask = MakeTask("attack", 1, StateTreeStatus.Success);
            var attack = MakeNode("attack", attackTask);
            var flee = MakeNode("flee", MakeTask("flee"));
            var done = MakeNode("done", MakeTask("done"));
            StubFlagCondition interrupt = MakeFlagCondition(k_InterruptKey);
            interrupt.recordEvaluations = true;
            AddTransition(attack, "flee", interrupt, true);
            AddTransition(attack, "done", null, false);
            var root = MakeNode("root");
            root.children.Add(attack);
            root.children.Add(flee);
            root.children.Add(done);
            StateTreeAsset tree = MakeTree(root);

            var quiet = MakeRunner(tree, "Quiet");
            var pressured = MakeRunner(tree, "Pressured");
            pressured.context.blackboard[k_InterruptKey] = true;

            quiet.StartTree();
            pressured.StartTree();
            quiet.TickTree(0.1f);
            pressured.TickTree(0.1f);

            // No interrupt: condition evaluated first, THEN the task ticks and succeeds.
            CollectionAssert.AreEqual(
                new[] { "attack:enter", "cond:interrupt:false", "attack:tick1", "attack:exit:Success", "done:enter" },
                Log(quiet));
            Assert.AreEqual("done", quiet.activeNodeId);

            // Interrupt raised: same condition, evaluated at the same point, pre-empts the tick.
            CollectionAssert.AreEqual(
                new[] { "attack:enter", "cond:interrupt:true", "attack:exit:Cancelled", "flee:enter" },
                Log(pressured));
            Assert.AreEqual("flee", pressured.activeNodeId);
            CollectionAssert.DoesNotContain(Log(pressured), "attack:tick1");
        }

        // ------------------------------------------------------------------ required case 3

        /// <summary>Completion transitions wait for EVERY task in the node, and each self-finishing
        /// task receives its own status (Success/Failure) in OnExit — never Cancelled.</summary>
        [Test]
        public void CompletionTransition_FiresOnlyAfterAllTasksFinish_AndPreservesStatuses()
        {
            var alpha = MakeTask("alpha", 1, StateTreeStatus.Success);
            var beta = MakeTask("beta", 3, StateTreeStatus.Failure);
            var work = MakeNode("work", alpha, beta);
            var done = MakeNode("done", MakeTask("done"));
            AddTransition(work, "done", null, false);
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(done);

            var runner = MakeRunner(MakeTree(root), "Worker");
            runner.StartTree();

            runner.TickTree(0.1f);
            Assert.AreEqual("work", runner.activeNodeId, "beta is still Running, the node must not complete");
            runner.TickTree(0.1f);
            Assert.AreEqual("work", runner.activeNodeId);
            runner.TickTree(0.1f);
            Assert.AreEqual("done", runner.activeNodeId, "all tasks finished, completion transition must fire");

            CollectionAssert.AreEqual(
                new[]
                {
                    "alpha:enter", "beta:enter",
                    "alpha:tick1", "alpha:exit:Success", "beta:tick1",
                    "beta:tick2",
                    "beta:tick3", "beta:exit:Failure",
                    "done:enter"
                },
                Log(runner));
            Assert.AreEqual(1, CountEntries(Log(runner), "alpha:exit"), "a finished task must be exited exactly once");
            Assert.AreEqual(1, CountEntries(Log(runner), "beta:exit"));
        }

        // ------------------------------------------------------------------ required case 4

        /// <summary>DeepCopy isolation: two runners on the SAME authored asset own separate task
        /// instances, and the authored instances themselves are never invoked.</summary>
        [Test]
        public void DeepCopy_RunnersDoNotShareTaskInstanceState()
        {
            StubRecordingTask authored = MakeTask("chase");
            var root = MakeNode("chase", authored);
            StateTreeAsset tree = MakeTree(root);

            var first = MakeRunner(tree, "ZombieA");
            var second = MakeRunner(tree, "ZombieB");
            first.StartTree();
            second.StartTree();
            first.TickTree(0.1f);
            second.TickTree(0.1f);
            first.TickTree(0.1f);
            second.TickTree(0.1f);

            // Shared instances would make the second runner see tick2 / tick4 on its first ticks.
            CollectionAssert.AreEqual(new[] { "chase:enter", "chase:tick1", "chase:tick2" }, Log(first));
            CollectionAssert.AreEqual(new[] { "chase:enter", "chase:tick1", "chase:tick2" }, Log(second));
            Assert.AreNotSame(first.context, second.context);

            Assert.AreEqual(0, authored.enterCount, "the authored asset must never be entered — only its deep copies run");
            Assert.AreEqual(0, authored.tickCount);
            Assert.AreEqual(0, authored.exitCount);
        }

        // ------------------------------------------------------------------ required case 5

        /// <summary>Entry resolution descends organizational nodes (no tasks, no transitions,
        /// at least one child) down to the first leaf.</summary>
        [Test]
        public void EntryNode_DescendsOrganizationalNodesToFirstLeaf()
        {
            var leaf = MakeNode("leaf", MakeTask("leaf"));
            var sibling = MakeNode("sibling", MakeTask("sibling"));
            var branch = MakeNode("branch");
            branch.children.Add(leaf);
            branch.children.Add(sibling);
            var root = MakeNode("root");
            root.children.Add(branch);

            var runner = MakeRunner(MakeTree(root), "Nested");
            List<string> events = TrackEvents(runner);
            runner.StartTree();

            Assert.AreEqual("leaf", runner.activeNodeId);
            CollectionAssert.AreEqual(new[] { "leaf:enter" }, Log(runner));
            CollectionAssert.AreEqual(new[] { "started", "entered:leaf", "changed:->leaf" }, events);
        }

        // ------------------------------------------------------------------ required case 6

        /// <summary>StopTree cancels whatever is running and leaves the runner idle.</summary>
        [Test]
        public void StopTree_CancelsRunningTasks()
        {
            var root = MakeNode("chase", MakeTask("chase"));
            var runner = MakeRunner(MakeTree(root), "Zombie");
            List<string> events = TrackEvents(runner);
            runner.StartTree();
            runner.TickTree(0.1f);

            runner.StopTree();

            CollectionAssert.AreEqual(new[] { "chase:enter", "chase:tick1", "chase:exit:Cancelled" }, Log(runner));
            Assert.IsFalse(runner.isRunning);
            Assert.AreEqual("", runner.activeNodeId);
            CollectionAssert.AreEqual(new[] { "started", "entered:chase", "changed:->chase", "left:chase", "stopped" }, events);

            int logCount = Log(runner).Count;
            int eventCount = events.Count;
            runner.TickTree(0.1f);
            runner.StopTree();
            Assert.AreEqual(logCount, Log(runner).Count, "a stopped runner must not tick or re-exit anything");
            Assert.AreEqual(eventCount, events.Count, "StopTree on an idle runner must be a no-op");
        }

        // ------------------------------------------------------------------ extra coverage

        /// <summary>A node that owns transitions is a leaf even without tasks — descent stops
        /// there, and with no tasks its completion transitions fire on the very next tick.</summary>
        [Test]
        public void EntryNode_StopsDescendingAtNodeThatOwnsTransitions()
        {
            var child = MakeNode("child", MakeTask("child"));
            var root = MakeNode("root");
            root.children.Add(child);
            AddTransition(root, "child", null, false);

            var runner = MakeRunner(MakeTree(root), "Gate");
            runner.StartTree();
            Assert.AreEqual("root", runner.activeNodeId, "root owns a transition, so it is the entry node");
            CollectionAssert.IsEmpty(Log(runner));

            runner.TickTree(0.1f);
            Assert.AreEqual("child", runner.activeNodeId, "a task-less node completes immediately");
            CollectionAssert.AreEqual(new[] { "child:enter" }, Log(runner));
        }

        /// <summary>A transition to an unknown node id logs an error and changes nothing — the
        /// current node and its running tasks survive. Note the tick is consumed: the runner
        /// returns early, so the task does not tick while the bad transition keeps firing.</summary>
        [Test]
        public void UnknownTransitionTarget_LogsError_AndKeepsCurrentState()
        {
            var chase = MakeNode("chase", MakeTask("chase"));
            AddTransition(chase, "ghost", MakeFlagCondition(k_InterruptKey), true);
            var root = MakeNode("root");
            root.children.Add(chase);

            var runner = MakeRunner(MakeTree(root), "Zombie");
            List<string> events = TrackEvents(runner);
            runner.StartTree();
            runner.context.blackboard[k_InterruptKey] = true;

            LogAssert.Expect(LogType.Error, "StateTreeRunner: unknown transition target 'ghost'");
            runner.TickTree(0.1f);

            Assert.AreEqual("chase", runner.activeNodeId);
            Assert.IsTrue(runner.isRunning);
            CollectionAssert.AreEqual(new[] { "chase:enter" }, Log(runner),
                "the failed transition must not exit the running task, and the tick is skipped");
            CollectionAssert.AreEqual(new[] { "started", "entered:chase", "changed:->chase" }, events,
                "no node-left / node-entered may be emitted for a failed transition");

            // State really is intact: drop the flag and the same task resumes ticking.
            runner.context.blackboard[k_InterruptKey] = false;
            runner.TickTree(0.1f);
            CollectionAssert.AreEqual(new[] { "chase:enter", "chase:tick1" }, Log(runner));
        }

        /// <summary>A task that finished (with its own status) in an earlier tick must not be
        /// exited a second time when an interrupt pre-empts the node later.</summary>
        [Test]
        public void TaskFinishedInEarlierTick_IsNotExitedAgainByLaterInterrupt()
        {
            var work = MakeNode("work", MakeTask("alpha", 1, StateTreeStatus.Success));
            // HOLD (M22): this state's exit is a condition someone else flips, so it must
            // wait complete rather than flow to its next sibling — which is exactly what a
            // completed state with an unmatched conditional edge does by default since the
            // implicit flow landed.
            work.completionFlow = StateTreeCompletionFlow.Hold;
            var idle = MakeNode("idle", MakeTask("idle"));
            var done = MakeNode("done", MakeTask("done"));
            AddTransition(work, "idle", MakeFlagCondition(k_InterruptKey), true);
            AddTransition(work, "done", MakeFlagCondition(k_AdvanceKey), false);
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(idle);
            root.children.Add(done);

            var runner = MakeRunner(MakeTree(root), "Zombie");
            runner.StartTree();

            runner.TickTree(0.1f);
            Assert.AreEqual("work", runner.activeNodeId, "a Hold state stays put, complete, "
                + "with no running tasks");

            runner.context.blackboard[k_InterruptKey] = true;
            runner.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[] { "alpha:enter", "alpha:tick1", "alpha:exit:Success", "idle:enter" },
                Log(runner));
            Assert.AreEqual(1, CountEntries(Log(runner), "alpha:exit"), "no second OnExit(Cancelled) for an already-finished task");
            Assert.AreEqual("idle", runner.activeNodeId);
        }

        /// <summary>Guard rails: StartTree without data (or without a root) reports and stays idle.</summary>
        [Test]
        public void StartTree_WithoutData_LogsErrorAndStaysStopped()
        {
            var runner = MakeRunner(MakeTree(MakeNode("root", MakeTask("root"))), "Broken");

            runner.data = null;
            LogAssert.Expect(LogType.Error, "StateTreeRunner: data or root is null");
            runner.StartTree();
            Assert.IsFalse(runner.isRunning);

            runner.data = MakeTree(null);
            LogAssert.Expect(LogType.Error, "StateTreeRunner: data or root is null");
            runner.StartTree();
            Assert.IsFalse(runner.isRunning);
            CollectionAssert.IsEmpty(Log(runner));
        }

        /// <summary>Characterization of a Godot-faithful quirk: start() does not stop() first, so
        /// restarting a live runner drops the previous node's tasks WITHOUT OnExit. Kept as a test
        /// so any future change to that contract is a deliberate, visible one.</summary>
        [Test]
        public void StartTreeWhileRunning_DoesNotCancelPreviousTasks_GodotParity()
        {
            var root = MakeNode("chase", MakeTask("chase"));
            var runner = MakeRunner(MakeTree(root), "Zombie");
            runner.StartTree();
            runner.TickTree(0.1f);

            runner.StartTree();

            CollectionAssert.AreEqual(new[] { "chase:enter", "chase:tick1", "chase:enter" }, Log(runner),
                "state_tree_runner.gd start() re-enters without exiting — no Cancelled is emitted");
            Assert.AreEqual(0, CountEntries(Log(runner), "chase:exit"));

            runner.TickTree(0.1f);
            CollectionAssert.AreEqual(new[] { "chase:enter", "chase:tick1", "chase:enter", "chase:tick1" }, Log(runner),
                "the fresh copy starts from tick 1");
        }

        // ------------------------------------------------------------------ fixture helpers

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

        private StateTreeAsset MakeTree(StateTreeNodeAsset root)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "TestTree";
            tree.treeName = "TestTree";
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        /// <summary>Runner on an INACTIVE GameObject: Awake/Start/Update never run, so the test
        /// owns every tick. The context is injected up front so the blackboard can be primed
        /// before StartTree (the set_context path of the Godot runner).</summary>
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

        private static List<string> Log(StateTreeRunner runner)
        {
            return StateTreeTestLog.Get(runner.context);
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

        private static int CountEntries(List<string> log, string prefix)
        {
            int count = 0;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].StartsWith(prefix))
                    count++;
            }
            return count;
        }
    }
}

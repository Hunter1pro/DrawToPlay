using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M22 (brief §10.1/§10.2): the IMPLICIT completion flow — children in order are a
    /// sequence, a last sibling bubbles to its parent, the root completing finishes the
    /// tree — and the completion POLICY (AllTasks / AnyTask / Never, per-task blocking).
    /// Declared transitions always win over the implicit flow; Hold opts a state out of it.
    ///
    /// Same ground rules as <see cref="StateTreeRunnerTests"/>: everything in memory, every
    /// tick explicit, runners on inactive GameObjects so the tests own the clock.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeCompletionTests
    {
        private const string k_FlagKey = "flag";

        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();

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

        // ------------------------------------------------------------- 1. the implicit flow

        [Test]
        public void ZeroEdges_ChildrenRunAsSequence_AndTheTreeFinishes()
        {
            var root = MakeNode("root");
            root.children.Add(MakeNode("a", MakeTask("a", 1)));
            root.children.Add(MakeNode("b", MakeTask("b", 1)));
            root.children.Add(MakeNode("c", MakeTask("c", 1)));

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            var finished = false;
            runner.treeFinished += () => finished = true;
            runner.StartTree();

            for (int i = 0; i < 4 && runner.isRunning; i++)
                runner.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[]
                {
                    "a:enter", "a:tick1", "a:exit:Success",
                    "b:enter", "b:tick1", "b:exit:Success",
                    "c:enter", "c:tick1", "c:exit:Success"
                },
                Log(runner),
                "three children with not one authored edge ARE a sequence");
            Assert.IsTrue(finished, "running off the root's end is a FINISH, not a hang");
            Assert.IsFalse(runner.isRunning, "a finished tree released its run");
        }

        [Test]
        public void LastSibling_BubblesToTheParentsNextSibling()
        {
            var group = MakeNode("group");
            group.children.Add(MakeNode("a", MakeTask("a", 1)));
            group.children.Add(MakeNode("b", MakeTask("b", 1)));
            var after = MakeNode("after", MakeTask("after"));
            var root = MakeNode("root");
            root.children.Add(group);
            root.children.Add(after);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);   // a finishes → b
            runner.TickTree(0.1f);   // b finishes → bubbles through group → after

            Assert.AreEqual("after", runner.activeNodeId,
                "the group's last child completing completes the GROUP, whose next sibling "
                + "is where the flow lands");
        }

        [Test]
        public void ParentsDeclaredEdge_FiresBeforeItsNextSibling_WhenBubbling()
        {
            var group = MakeNode("group");
            group.children.Add(MakeNode("a", MakeTask("a", 1)));
            var elsewhere = MakeNode("elsewhere", MakeTask("elsewhere"));
            var after = MakeNode("after", MakeTask("after"));
            var root = MakeNode("root");
            root.children.Add(group);
            root.children.Add(after);
            root.children.Add(elsewhere);
            // The whole sequence says where it goes when it ends — an edge on the PARENT.
            AddTransition(group, "elsewhere", null, false);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("elsewhere", runner.activeNodeId,
                "a completed group's own on-completion edge outranks its next sibling");
        }

        [Test]
        public void DeclaredEdge_WinsOverTheImplicitSibling()
        {
            var a = MakeNode("a", MakeTask("a", 1));
            var b = MakeNode("b", MakeTask("b"));
            var target = MakeNode("target", MakeTask("target"));
            AddTransition(a, "target", null, false);
            var root = MakeNode("root");
            root.children.Add(a);
            root.children.Add(b);
            root.children.Add(target);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("target", runner.activeNodeId,
                "the explicit model is the override: a declared edge goes exactly where it "
                + "says, never to the sibling");
        }

        [Test]
        public void Hold_StaysComplete_UntilItsConditionalEdgePasses()
        {
            var work = MakeNode("work", MakeTask("work", 1));
            work.completionFlow = StateTreeCompletionFlow.Hold;
            var next = MakeNode("next", MakeTask("next"));
            var done = MakeNode("done", MakeTask("done"));
            AddTransition(work, "done", MakeFlagCondition(k_FlagKey), false);
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(next);
            root.children.Add(done);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);
            runner.TickTree(0.1f);
            Assert.AreEqual("work", runner.activeNodeId,
                "Hold: complete, going nowhere, and NOT to the sibling");

            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("done", runner.activeNodeId,
                "a Hold state's declared edges stay live and leave the moment one passes");
        }

        // -------------------------------------------------------- 2. the completion policy

        [Test]
        public void Never_IsResident_WithoutAnyTimerTask()
        {
            var wait = MakeNode("wait");   // NO tasks at all — the state the WaitTask faked
            wait.completeWhen = StateTreeCompleteWhen.Never;
            var next = MakeNode("next", MakeTask("next"));
            AddTransition(wait, "next", MakeFlagCondition(k_FlagKey), true);
            var root = MakeNode("root");
            root.children.Add(wait);
            root.children.Add(next);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);
            runner.TickTree(0.1f);
            Assert.AreEqual("wait", runner.activeNodeId,
                "Never: an empty state that simply IS somewhere, no hour-long timer needed");

            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("next", runner.activeNodeId,
                "interrupts are a resident state's only way out, and they still work");
        }

        [Test]
        public void AnyTask_CompletesOnTheFirstFinish_CancellingTheRest()
        {
            var race = MakeNode("race", MakeTask("fast", 1), MakeTask("slow", 99));
            race.completeWhen = StateTreeCompleteWhen.AnyTask;
            var next = MakeNode("next", MakeTask("next"));
            var root = MakeNode("root");
            root.children.Add(race);
            root.children.Add(next);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("next", runner.activeNodeId,
                "one finisher is enough under AnyTask");
            CollectionAssert.Contains(Log(runner), "slow:exit:Cancelled",
                "the task that lost the race is torn down like any pre-empted task");
        }

        [Test]
        public void NonBlockingTask_DoesNotHoldTheStateOpen()
        {
            StubRecordingTask ambient = MakeTask("ambient");   // never finishes on its own
            ambient.blocking = false;
            var work = MakeNode("work", MakeTask("work", 1), ambient);
            var next = MakeNode("next", MakeTask("next"));
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(next);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("next", runner.activeNodeId,
                "a non-blocking task runs while the state lives and holds nothing open");
            CollectionAssert.Contains(Log(runner), "ambient:exit:Cancelled");
        }

        // ------------------------------------------------------------- 3. sub-trees return

        [Test]
        public void SubTreeRunningOffItsEnd_CompletesTheTaskWithSuccess()
        {
            var childRoot = MakeNode("childRoot");
            childRoot.children.Add(MakeNode("only", MakeTask("only", 1)));
            StateTreeAsset childTree = MakeTree(childRoot);

            var sub = ScriptableObject.CreateInstance<RunSubTreeTask>();
            sub.subTree = childTree;
            // No terminal-state names: the OLD contract needed one; finishing is the new one.
            sub.successStates = new List<string>();
            sub.failureStates = new List<string>();
            m_Assets.Add(sub);

            var call = MakeNode("call");
            call.tasks.Add(sub);
            var after = MakeNode("after", MakeTask("after"));
            var root = MakeNode("root");
            root.children.Add(call);
            root.children.Add(after);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.TickTree(0.1f);   // inner task finishes; inner tree runs off its end
            runner.TickTree(0.1f);   // the wrapper reports Success; call completes → after

            Assert.AreEqual("after", runner.activeNodeId,
                "a finished sub-tree is a RETURNED call — Success, not the old Failure");
        }

        // ------------------------------------------------------------------------ helpers

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

        private static void AddTransition(StateTreeNodeAsset source, string targetNodeId,
            StateTreeConditionAsset condition, bool checkWhileRunning)
        {
            source.transitions.Add(new StateTreeTransition
            {
                targetNodeId = targetNodeId,
                condition = condition,
                checkWhileRunning = checkWhileRunning
            });
        }

        private StateTreeAsset MakeTree(StateTreeNodeAsset root)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "CompletionTestTree";
            tree.treeName = "CompletionTestTree";
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        private StateTreeRunner MakeRunner(StateTreeAsset tree)
        {
            var go = new GameObject("CompletionOwner");
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
    }
}

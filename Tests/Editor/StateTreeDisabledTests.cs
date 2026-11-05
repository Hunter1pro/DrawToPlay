using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A switched-off state: entered done, none of its tasks or children run, and the run goes on
    /// past it — however it got there.</summary>
    [TestFixture]
    public sealed class StateTreeDisabledTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private readonly List<StateTreeExecutor> m_Running = new List<StateTreeExecutor>();

        [TearDown]
        public void TearDown()
        {
            foreach (StateTreeExecutor executor in m_Running)
                executor.StopTree();
            m_Running.Clear();
            foreach (Object junk in m_Junk)
            {
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ADisabledStep_AndADisabledGroup_AreSkipped_TheNextSiblingRuns()
        {
            var group = Node("group");
            group.children.Add(Node("g1", Task("g1", 1)));
            group.children.Add(Node("g2", Task("g2", 1)));
            group.disabled = true;
            var step = Node("step", Task("step", 1));
            step.disabled = true;
            var root = Node("root");
            root.children.Add(Node("a", Task("a", 1)));
            root.children.Add(group);
            root.children.Add(step);
            root.children.Add(Node("c", Task("c")));

            StateTreeExecutor runner = Run(root);
            var entered = new List<string>();
            runner.activeNodeChanged += (from, to) => entered.Add(to);
            runner.StartTree();
            for (int i = 0; i < 6 && runner.activeNodeId != "c"; i++)
                runner.TickTree(0.1f);

            Assert.AreEqual("c", runner.activeNodeId);
            CollectionAssert.AreEqual(new[] { "a:enter", "a:tick1", "a:exit:Success", "c:enter" }, Log(runner),
                "no task of the switched-off step, the group or its children ever entered");
            CollectionAssert.DoesNotContain(entered, "g1", "the group's children are skipped with it");
            CollectionAssert.Contains(entered, "group");
        }

        [Test]
        public void AMoveIntoADisabledGroup_LandsOnTheGroup_AndGoesOnPastIt()
        {
            var group = Node("group");
            group.children.Add(Node("g1", Task("g1", 1)));
            group.disabled = true;
            var root = Node("root");
            root.children.Add(Node("a", Task("a")));
            root.children.Add(group);
            root.children.Add(Node("c", Task("c")));

            StateTreeExecutor runner = Run(root);
            runner.StartTree();
            Assert.IsTrue(runner.RequestTransition("g1"));
            Assert.AreEqual("group", runner.activeNodeId, "a way into its child is a way into the switched-off group");
            Assert.IsTrue(runner.activeNodeComplete);
            runner.TickTree(0.1f);
            Assert.AreEqual("c", runner.activeNodeId);

            runner.StopTree();
            runner.StartTree("g1", false);
            Assert.AreEqual("group", runner.activeNodeId, "a resume inside it too");
            CollectionAssert.DoesNotContain(Log(runner), "g1:enter");
        }

        [Test]
        public void ADisabledState_NeitherHolds_NorTakesItsOwnTransitions()
        {
            var off = Node("off", Task("off"));
            off.completionFlow = StateTreeCompletionFlow.Hold;
            off.transitions.Add(new StateTreeTransition { targetNodeId = "elsewhere" });
            off.disabled = true;
            var root = Node("root");
            root.children.Add(off);
            root.children.Add(Node("next", Task("next")));
            root.children.Add(Node("elsewhere", Task("elsewhere")));

            StateTreeExecutor runner = Run(root);
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("next", runner.activeNodeId);
        }

        [Test]
        public void ADisabledBranch_EndsItsQuestion_NotIntoTheNextBranch()
        {
            var first = Node("first", Task("first"));
            first.completionFlow = StateTreeCompletionFlow.EndsParent;
            first.disabled = true;
            var second = Node("second", Task("second"));
            second.completionFlow = StateTreeCompletionFlow.EndsParent;
            var question = Node("question");
            question.children.Add(first);
            question.children.Add(second);
            var root = Node("root");
            root.children.Add(question);
            root.children.Add(Node("after", Task("after")));

            StateTreeExecutor runner = Run(root);
            runner.StartTree();
            Assert.AreEqual("first", runner.activeNodeId);
            runner.TickTree(0.1f);

            Assert.AreEqual("after", runner.activeNodeId);
            Assert.IsFalse(Log(runner).Any(l => l.StartsWith("first") || l.StartsWith("second")));
        }

        [Test]
        public void TheFlag_IsOffByDefault_AndTravelsWithTheCopy()
        {
            var node = Node("n");
            Assert.IsFalse(node.disabled);
            node.disabled = true;
            var root = Node("root");
            root.children.Add(node);
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = root;
            StateTreeAsset copy = tree.DeepCopy();
            Assert.IsTrue(copy.root.children[0].disabled);
            StateTreeAsset.DestroyCopy(copy);
        }

        private StateTreeExecutor Run(StateTreeNodeAsset root)
        {
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.treeName = "DisabledTestTree";
            tree.root = root;
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext() };
            m_Running.Add(executor);
            return executor;
        }

        private StateTreeNodeAsset Node(string id, params StateTreeTaskAsset[] tasks)
        {
            var node = Junk(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            node.name = id;
            node.nodeId = id;
            node.tasks.AddRange(tasks);
            return node;
        }

        private StubRecordingTask Task(string id, int finishOnTick = 0)
        {
            var task = Junk(ScriptableObject.CreateInstance<StubRecordingTask>());
            task.taskId = id;
            task.finishOnTick = finishOnTick;
            return task;
        }

        private static List<string> Log(StateTreeExecutor runner) => StateTreeTestLog.Get(runner.context);

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }
    }
}

using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// 0.2.0: interrupts are heard along the whole ACTIVE CHAIN — the current state first,
    /// then its ancestors up to the root — so "a film pre-empts everything" is one row on
    /// the root instead of one per leaf. A pre-empted task exits Cancelled, never completed:
    /// an interrupt is not a result.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeInterruptTests
    {
        private const string k_FlagKey = "flag";
        private const string k_OtherKey = "other";

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

        [Test]
        public void RootInterrupt_PreemptsARunningDescendant_WhichExitsCancelled()
        {
            var slow = MakeNode("slow", MakeTask("slow", 99));
            var group = MakeNode("group");
            group.children.Add(slow);
            var film = MakeNode("film", MakeTask("film"));
            var root = MakeNode("root");
            root.children.Add(group);
            root.children.Add(film);
            AddTransition(root, "film", MakeFlagCondition(k_FlagKey), true);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            Assert.AreEqual("slow", runner.activeNodeId,
                "an interrupt row alone must not stop entry from descending to the first leaf");

            runner.TickTree(0.1f);
            Assert.AreEqual("slow", runner.activeNodeId, "no flag, no pre-emption");

            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("film", runner.activeNodeId,
                "the root's interrupt pulled a running grandchild out");
            CollectionAssert.Contains(Log(runner), "slow:exit:Cancelled",
                "pre-empted is cancelled, not completed");
            CollectionAssert.DoesNotContain(Log(runner), "slow:exit:Success");
        }

        [Test]
        public void CurrentStatesOwnInterrupt_OutranksAnAncestors()
        {
            var work = MakeNode("work", MakeTask("work", 99));
            var mine = MakeNode("mine", MakeTask("mine"));
            var theirs = MakeNode("theirs", MakeTask("theirs"));
            var root = MakeNode("root");
            root.children.Add(work);
            root.children.Add(mine);
            root.children.Add(theirs);
            AddTransition(work, "mine", MakeFlagCondition(k_FlagKey), true);
            AddTransition(root, "theirs", MakeFlagCondition(k_FlagKey), true);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);

            Assert.AreEqual("mine", runner.activeNodeId,
                "both passed on the same tick; the current state's row wins");
        }

        [Test]
        public void MidChainInterrupt_IsHeardToo()
        {
            var leaf = MakeNode("leaf", MakeTask("leaf", 99));
            var group = MakeNode("group");
            group.children.Add(leaf);
            AddTransition(group, "out", MakeFlagCondition(k_FlagKey), true);
            var outNode = MakeNode("out", MakeTask("out"));
            var root = MakeNode("root");
            root.children.Add(group);
            root.children.Add(outNode);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            Assert.AreEqual("leaf", runner.activeNodeId);

            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("out", runner.activeNodeId,
                "a row between root and leaf pre-empts its own subtree");
        }

        [Test]
        public void AncestorOrder_IsNearestFirst()
        {
            var leaf = MakeNode("leaf", MakeTask("leaf", 99));
            var group = MakeNode("group");
            group.children.Add(leaf);
            AddTransition(group, "near", MakeFlagCondition(k_FlagKey), true);
            var near = MakeNode("near", MakeTask("near"));
            var far = MakeNode("far", MakeTask("far"));
            var root = MakeNode("root");
            root.children.Add(group);
            root.children.Add(near);
            root.children.Add(far);
            AddTransition(root, "far", MakeFlagCondition(k_FlagKey), true);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);

            Assert.AreEqual("near", runner.activeNodeId,
                "the parent's row is consulted before the grandparent's");
        }

        [Test]
        public void ANodeWithACompletionEdge_IsStillAResidentState_OnEntry()
        {
            // The old entry rule: any transition stops the descent. Only interrupt-only nodes
            // descend now — a completion edge still marks a state you can be in.
            var group = MakeNode("group");
            group.children.Add(MakeNode("inner", MakeTask("inner")));
            AddTransition(group, "elsewhere", null, false);
            var elsewhere = MakeNode("elsewhere", MakeTask("elsewhere"));
            var root = MakeNode("root");
            root.children.Add(group);
            root.children.Add(elsewhere);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();

            Assert.AreEqual("group", runner.activeNodeId);
        }

        [Test]
        public void AnInterruptTargetingTheRunningState_DoesNotRetrigger()
        {
            var slow = MakeNode("slow", MakeTask("slow", 99));
            var film = MakeNode("film", MakeTask("film"));
            var root = MakeNode("root");
            root.children.Add(slow);
            root.children.Add(film);
            AddTransition(root, "film", MakeFlagCondition(k_FlagKey), true);

            StateTreeRunner runner = MakeRunner(MakeTree(root));
            runner.StartTree();
            runner.context.blackboard[k_FlagKey] = true;
            runner.TickTree(0.1f);
            Assert.AreEqual("film", runner.activeNodeId);

            runner.TickTree(0.1f);
            runner.TickTree(0.1f);
            int entries = Log(runner).FindAll(line => line == "film:enter").Count;
            Assert.AreEqual(1, entries,
                "a still-true interrupt must not restart the state it already reached");
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
            tree.name = "InterruptTestTree";
            tree.treeName = "InterruptTestTree";
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        private StateTreeRunner MakeRunner(StateTreeAsset tree)
        {
            var go = new GameObject("InterruptOwner");
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

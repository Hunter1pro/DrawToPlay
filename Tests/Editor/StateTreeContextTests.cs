using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M8 contexts spine (brief §3.1/§3.2, §5.1): resolution walks the
    /// hierarchy and settles multiplayer by parenting, scoped atoms hit the scope they name, a
    /// host's own tree runs IN the host's context, level swap means fresh level state under a
    /// surviving Root, services connect by placement and are found up the chain, and a stopped
    /// scope cancels its behavior.
    ///
    /// Same ground rules as <see cref="StateTreeRunnerTests"/>: everything in memory, every tick
    /// explicit. HOST GameObjects stay ACTIVE (resolution filters on isActiveAndEnabled, and
    /// plain MonoBehaviour callbacks do not run in EditMode, so nothing ticks behind the tests'
    /// back); hosts are registered explicitly, which is exactly what OnEnable does in play mode.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeContextTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        [SetUp]
        public void SetUp()
        {
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // ReferenceEquals, not Unity's fake-null: a host DESTROYED mid-test (the level swap)
            // is still a live C# object sitting in the static registry, and skipping it here
            // would leak it into every later test's UniqueMatch scan.
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
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
            m_Hosts.Clear();
        }

        // ------------------------------------------------------ 1. resolution walks the spine

        [Test]
        public void Resolve_NearestWins_AndParentingSplitsPlayers()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root);
            StateTreeContextHost level = MakeHost("Level", StateTreeContextKind.Level, parent: root);
            StateTreeContextHost p1 = MakeHost("P1", StateTreeContextKind.Player, "p1", level);
            StateTreeContextHost p2 = MakeHost("P2", StateTreeContextKind.Player, "p2", level);
            GameObject unit1 = MakeUnit("Unit1", p1);
            GameObject unit2 = MakeUnit("Unit2", p2);

            Assert.AreSame(p1, StateTreeContextHost.Resolve(unit1, StateTreeContextKind.Player),
                "the nearest Player above unit1 is p1 — parenting is the multiplayer split");
            Assert.AreSame(p2, StateTreeContextHost.Resolve(unit2, StateTreeContextKind.Player));
            Assert.AreSame(level, StateTreeContextHost.Resolve(unit1, StateTreeContextKind.Level));
            Assert.AreSame(root, StateTreeContextHost.Resolve(unit1, StateTreeContextKind.Root));

            Assert.AreSame(p2,
                StateTreeContextHost.Resolve(unit1, StateTreeContextKind.Player, "p2"),
                "asking for a NAMED sibling from inside the other branch finds it by id");

            Assert.AreSame(level, p1.ParentHost, "the parent chain steps Player -> Level");
            Assert.AreSame(root, level.ParentHost);
        }

        [Test]
        public void Resolve_DetachedCaller_UsesUniqueMatch_AndAmbiguityIsNull()
        {
            StateTreeContextHost level = MakeHost("OnlyLevel", StateTreeContextKind.Level);
            GameObject stray = MakeUnit("Stray", null);

            Assert.AreSame(level, StateTreeContextHost.Resolve(stray, StateTreeContextKind.Level),
                "a detached caller reaches the UNIQUE level");

            MakeHost("SecondLevel", StateTreeContextKind.Level);
            Assert.IsNull(StateTreeContextHost.Resolve(stray, StateTreeContextKind.Level),
                "two anonymous levels are ambiguous from outside both — null, not a guess");
        }

        // ------------------------------------------------- 2. the host's tree IS its context

        [Test]
        public void HostTree_RunsInHostContext_WritingTheScopeBlackboard()
        {
            StateTreeContextHost host = MakeHost("Level", StateTreeContextKind.Level);
            var seed = ScriptableObject.CreateInstance<SetBlackboardTask>();
            seed.key.text = "mode";
            seed.kind = SetBlackboardTask.ValueKind.String;
            seed.stringValue = "combat";
            m_Assets.Add(seed);

            host.tree = MakeTree(MakeLeaf("boot", seed));
            host.StartTree();
            host.TickTree(0.1f);

            Assert.AreEqual("combat", host.Context.blackboard["mode"],
                "a state of the host's own tree wrote the SCOPE blackboard, not a private one");
        }

        // -------------------------------------------------------------- 3. the scoped atoms

        [Test]
        public void ScopedAtoms_HitTheScopeTheyName_FromANestedUnit()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root);
            StateTreeContextHost level = MakeHost("Level", StateTreeContextKind.Level, parent: root);
            StateTreeContextHost p1 = MakeHost("P1", StateTreeContextKind.Player, "p1", level);
            GameObject unit = MakeUnit("Unit", p1);

            var publish = ScriptableObject.CreateInstance<SetContextValueTask>();
            publish.scope = StateTreeContextKind.Level;
            publish.key.text = "alarm";
            publish.kind = SetBlackboardTask.ValueKind.Float;
            publish.floatValue = 1f;
            m_Assets.Add(publish);

            StateTreeRunner writer = MakeRunner(MakeTree(MakeLeaf("raise", publish)), unit);
            writer.StartTree();
            writer.TickTree(0.1f);

            Assert.AreEqual(1f, level.Context.blackboard["alarm"],
                "the publish landed on the LEVEL scope");
            Assert.IsFalse(root.Context.blackboard.ContainsKey("alarm"),
                "and only there — Root is a different scope, not a mirror");
            Assert.IsFalse(p1.Context.blackboard.ContainsKey("alarm"));

            var read = ScriptableObject.CreateInstance<GetContextValueTask>();
            read.scope = StateTreeContextKind.Level;
            read.key.text = "alarm";
            read.localKey.text = "alarmLocal";
            m_Assets.Add(read);

            StateTreeRunner reader = MakeRunner(MakeTree(MakeLeaf("check", read)), unit);
            reader.StartTree();
            reader.TickTree(0.1f);

            Assert.AreEqual(1f, reader.context.blackboard["alarmLocal"],
                "the copy-down landed on the READING tree's own blackboard");

            var has = ScriptableObject.CreateInstance<HasContextKeyCondition>();
            has.scope = StateTreeContextKind.Level;
            has.key.text = "alarm";
            m_Assets.Add(has);
            Assert.IsTrue(has.Evaluate(reader.context), "the condition sees the scope key");
            has.invert = true;
            Assert.IsFalse(has.Evaluate(reader.context));
            has.invert = false;
            has.key.text = "no-such-key";
            Assert.IsFalse(has.Evaluate(reader.context));
        }

        [Test]
        public void GetContextValue_MissingKey_FailsByDefault_QuietWhenAsked()
        {
            MakeHost("Root", StateTreeContextKind.Root);
            GameObject unit = MakeUnit("Unit", null);

            var read = ScriptableObject.CreateInstance<GetContextValueTask>();
            read.scope = StateTreeContextKind.Root;
            read.key.text = "absent";
            m_Assets.Add(read);

            var context = new StateTreeContext(unit);
            Assert.AreEqual(StateTreeStatus.Failure, read.OnTick(context, 0.1f),
                "a missing scope key FAILS by default so a transition can branch on it");

            read.failIfMissing = false;
            Assert.AreEqual(StateTreeStatus.Success, read.OnTick(context, 0.1f));
            Assert.IsFalse(context.blackboard.ContainsKey("absent"),
                "and the quiet form writes nothing rather than a null");
        }

        // ------------------------------------------------------------------- 4. level swap

        [Test]
        public void LevelSwap_FreshLevelState_WhileRootPersists()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root);
            root.Context.blackboard["progress"] = 3f;

            StateTreeContextHost levelA = MakeHost("LevelA", StateTreeContextKind.Level,
                parent: root);
            levelA.Context.blackboard["doorOpen"] = 1f;
            GameObject stray = MakeUnit("Stray", null);

            Object.DestroyImmediate(levelA.gameObject);
            StateTreeContextHost levelB = MakeHost("LevelB", StateTreeContextKind.Level,
                parent: root);

            Assert.AreSame(levelB, StateTreeContextHost.Resolve(stray, StateTreeContextKind.Level),
                "after the swap the unique Level is the new one");
            Assert.IsFalse(levelB.Context.blackboard.ContainsKey("doorOpen"),
                "level state died with the level — a fresh host is a fresh dictionary");
            Assert.AreEqual(3f, root.Context.blackboard["progress"],
                "while Root state simply persisted");
        }

        // --------------------------------------------------------------------- 5. services

        [Test]
        public void Services_ConnectByPlacement_AndAreFoundUpTheChain()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root);
            StateTreeContextHost level = MakeHost("Level", StateTreeContextKind.Level, parent: root);
            StateTreeContextHost p1 = MakeHost("P1", StateTreeContextKind.Player, "p1", level);
            GameObject unit = MakeUnit("Unit", p1);

            // A RUNTIME service class: an editor-assembly MonoBehaviour (the sims' stub)
            // cannot be AddComponent'd in real Unity — AddComponent returns null for
            // editor-assembly scripts, which the shims never enforced.
            var service = new WorldService(root, null);

            root.Provide(service);
            Assert.AreSame(root, service.scope, "it belongs to the scope that built it");

            Assert.AreSame(service, p1.GetService<WorldService>(),
                "a Player scope sees the Root service through the parent chain");
            Assert.AreSame(service, StateTreeContextHost.FindService<WorldService>(unit),
                "and a unit asks with one call, knowing nothing about where it lives");

            service.Dispose();
            root.Forget(service);
            Assert.IsNull(p1.GetService<WorldService>(),
                "a service the scope has let go is gone from every scope that saw it");
        }

        // --------------------------------------------------------------------- 6. teardown

        [Test]
        public void HostStop_CancelsItsRunningTree()
        {
            StateTreeContextHost host = MakeHost("Level", StateTreeContextKind.Level);
            var work = ScriptableObject.CreateInstance<StubRecordingTask>();
            work.taskId = "svc";
            m_Assets.Add(work);

            host.tree = MakeTree(MakeLeaf("serve", work));
            host.StartTree();
            host.TickTree(0.1f);
            host.StopTree();

            CollectionAssert.AreEqual(
                new[] { "svc:enter", "svc:tick1", "svc:exit:Cancelled" },
                StateTreeTestLog.Get(host.Context),
                "stopping the scope cancelled its service task — the library teardown contract");
        }

        // ---------------------------------------------------------------------- fixtures

        /// <summary>ACTIVE GameObject (resolution filters on isActiveAndEnabled; no callbacks
        /// run in EditMode), autoStart off, registered exactly as OnEnable would.</summary>
        private StateTreeContextHost MakeHost(string goName, StateTreeContextKind kind,
            string contextId = "", StateTreeContextHost parent = null)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);

            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
            host.contextId = contextId;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }

        private GameObject MakeUnit(string goName, StateTreeContextHost parent)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            return go;
        }

        private StateTreeRunner MakeRunner(StateTreeAsset tree, GameObject ownerGo)
        {
            var go = new GameObject(ownerGo.name + "Runner");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(ownerGo.transform);
            m_Objects.Add(go);

            var runner = go.AddComponent<StateTreeRunner>();
            runner.autoStart = false;
            runner.data = tree;
            runner.ownerObject = ownerGo;
            runner.context = new StateTreeContext(ownerGo);
            return runner;
        }

        private StateTreeNodeAsset MakeLeaf(string nodeId, StateTreeTaskAsset task)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = nodeId;
            node.name = "Node " + nodeId;
            node.tasks.Add(task);
            m_Assets.Add(node);
            return node;
        }

        private StateTreeAsset MakeTree(StateTreeNodeAsset root)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "ContextTestTree";
            tree.treeName = "ContextTestTree";
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }
    }
}

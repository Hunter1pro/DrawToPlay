using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M12 key contracts: declarations live in tree headers with a
    /// GUID id, linked fields are rewritten to the declaration's CURRENT name at StartTree
    /// (renames are free), resolution searches own → uses → the mount chain with nearest
    /// winning, and a link whose id resolves nowhere degrades to the field's own text with
    /// one error — unmanaged, not broken.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeKeyTests
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

        [Test]
        public void LinkedField_GetsTheDeclaredName_AndFollowsRenames()
        {
            var set = ScriptableObject.CreateInstance<SetBlackboardTask>();
            set.key = "stale-text";
            set.kind = SetBlackboardTask.ValueKind.Float;
            set.floatValue = 7f;
            m_Assets.Add(set);

            StateTreeAsset tree = MakeTree(MakeLeaf("write", set));
            StateTreeKeyDeclaration loot = Declare(tree, "loot", StateTreeKeyKind.Float);
            tree.root.children[0].keyLinks.Add(new StateTreeKeyLink
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "key",
                keyId = loot.id
            });

            StateTreeRunner runner = MakeRunner(tree);
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.AreEqual(7f, runner.context.blackboard["loot"],
                "the linked field ran under the DECLARED name, not its serialized text");
            Assert.AreEqual("stale-text", set.key,
                "the authored asset was never touched — only the deep copy is rewritten");

            runner.StopTree();
            loot.name = "gold";
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.AreEqual(7f, runner.context.blackboard["gold"],
                "renaming the declaration re-pointed every wired use on the next run");
        }

        [Test]
        public void Resolution_SearchesUses_ThenMountChain_NearestWins()
        {
            // A shared vocabulary tree, imported horizontally.
            var shared = ScriptableObject.CreateInstance<StateTreeAsset>();
            shared.name = "SharedKeys";
            m_Assets.Add(shared);
            StateTreeKeyDeclaration alarm = Declare(shared, "alarm", StateTreeKeyKind.Event);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "User";
            m_Assets.Add(tree);
            tree.uses.Add(shared);
            Assert.AreSame(alarm, StateTreeKeyResolver.Find(tree, null, alarm.id),
                "an imported tree's declaration is found through `uses`");

            // The mount chain: a host's tree declares; a unit below resolves it by owner.
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            var rootTree = ScriptableObject.CreateInstance<StateTreeAsset>();
            rootTree.name = "RootTree";
            m_Assets.Add(rootTree);
            root.tree = rootTree;
            StateTreeKeyDeclaration score = Declare(rootTree, "score", StateTreeKeyKind.Float);

            GameObject unit = MakeUnit("Unit", root);
            Assert.AreSame(score, StateTreeKeyResolver.Find(tree, unit, score.id),
                "an ancestor host tree's declaration resolves through the mount chain");
            Assert.IsNull(StateTreeKeyResolver.Find(tree, null, score.id),
                "and NOT without an owner — the chain is a runtime fact");

            // Nearest wins: the tree's own declaration shadows a chain one with the same id.
            var shadow = Declare(tree, "score-local", StateTreeKeyKind.Float);
            shadow.id = score.id;
            Assert.AreSame(shadow, StateTreeKeyResolver.Find(tree, unit, score.id),
                "own declarations outrank the chain");
        }

        [Test]
        public void MissingDeclaration_KeepsTheFieldText_WithOneError()
        {
            var set = ScriptableObject.CreateInstance<SetBlackboardTask>();
            set.key = "fallback";
            set.kind = SetBlackboardTask.ValueKind.Float;
            set.floatValue = 1f;
            m_Assets.Add(set);

            StateTreeAsset tree = MakeTree(MakeLeaf("write", set));
            tree.root.children[0].keyLinks.Add(new StateTreeKeyLink
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "key",
                keyId = "no-such-id"
            });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "resolves no declaration"));
            StateTreeRunner runner = MakeRunner(tree);
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.AreEqual(1f, runner.context.blackboard["fallback"],
                "an unresolvable link degrades to the field's own text — unmanaged, not broken");
        }

        // ---------------------------------------------------------------------- fixtures

        private StateTreeKeyDeclaration Declare(StateTreeAsset tree, string name,
            StateTreeKeyKind kind)
        {
            var declaration = new StateTreeKeyDeclaration
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = name,
                kind = kind
            };
            tree.keys.Add(declaration);
            return declaration;
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

        private StateTreeAsset MakeTree(StateTreeNodeAsset leaf)
        {
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            root.name = "Node root";
            root.children.Add(leaf);
            m_Assets.Add(root);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "KeyTestTree";
            tree.treeName = "KeyTestTree";
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        private StateTreeRunner MakeRunner(StateTreeAsset tree)
        {
            var go = new GameObject("KeyRunner");
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

        private StateTreeContextHost MakeHost(string goName, StateTreeContextKind kind,
            StateTreeContextHost parent)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
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
    }
}

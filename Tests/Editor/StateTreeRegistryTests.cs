using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M13 data registries: a typed entry reference resolves
    /// against the tree's registry list at StartTree by ID (renames are free — the injected
    /// entry IS the registry's row, so the runtime string follows the dashboard), and an id
    /// that resolves nowhere is one error plus an empty reference the task answers for.
    /// The full-flow coverage (registry refs bound by type, the inventory example end to
    /// end) lives in <see cref="UITreeTests"/>, which runs on the same machinery.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeRegistryTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

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
        public void EntryRef_ResolvesById_AndFollowsRenames()
        {
            ItemRegistry registry = MakeRegistry(out ItemDef sword);
            StateTreeContextHost player = MakePlayer();
            MountBag(player, registry);

            var add = ScriptableObject.CreateInstance<InventoryAddTask>();
            add.item.entryId = sword.id;
            add.item.entryName = "stale-cache";
            add.count = 1;
            m_Assets.Add(add);

            StateTreeRunner runner = MakeRunner(MakeTree(add, registry), player);
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.AreEqual(1, StateTreeInventoryUtil.Count(player.Context, "sword"),
                "the reference resolved by ID — the serialized name cache is never trusted");

            runner.StopTree();
            sword.name = "blade";
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.AreEqual(1, StateTreeInventoryUtil.Count(player.Context, "blade"),
                "renaming the entry in the dashboard re-pointed the runtime string");
        }

        [Test]
        public void MissingEntry_IsOneError_AndTheTaskFails()
        {
            ItemRegistry registry = MakeRegistry(out _);
            StateTreeContextHost player = MakePlayer();
            MountBag(player, registry);

            var add = ScriptableObject.CreateInstance<InventoryAddTask>();
            add.item.entryId = "no-such-id";
            add.count = 1;
            m_Assets.Add(add);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "resolves no entry"));
            StateTreeRunner runner = MakeRunner(MakeTree(add, registry), player);
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.AreEqual(0, StateTreeInventoryUtil.Count(player.Context, "sword"),
                "an unresolved reference adds nothing — the task Failed instead of inventing");
        }

        // ---------------------------------------------------------------------- fixtures

        private ItemRegistry MakeRegistry(out ItemDef sword)
        {
            var registry = ScriptableObject.CreateInstance<ItemRegistry>();
            sword = new ItemDef
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = "sword",
                displayName = "Iron Sword",
                kind = ItemKind.Weapon
            };
            registry.entries.Add(sword);
            m_Assets.Add(registry);
            return registry;
        }


        /// <summary>
        /// THE BAG IS A SUBSYSTEM NOW (M32): the inventory atoms ask it instead of writing the
        /// encoding themselves, so a tree that gives or spends items needs one mounted. Two
        /// lines of fixture, and the test exercises the path the game actually runs.
        /// </summary>
        private InventoryService MountBag(StateTreeContextHost host, ItemRegistry items)
        {
            var go = new GameObject("Bag");
            go.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(go);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "inventory";
            def.serviceName = "inventory";
            def.registry = items;
            m_Assets.Add(def);
            var service = go.AddComponent<InventoryService>();
            service.definition = def;
            host.Provide(service);
            return service;
        }

        private StateTreeContextHost MakePlayer()
        {
            var go = new GameObject("P1");
            go.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Player;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }

        private StateTreeAsset MakeTree(StateTreeTaskAsset task,
            StateTreeRegistryAsset registry)
        {
            var leaf = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            leaf.nodeId = "work";
            leaf.name = "Node work";
            leaf.tasks.Add(task);
            m_Assets.Add(leaf);

            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            root.name = "Node root";
            root.children.Add(leaf);
            m_Assets.Add(root);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "RegistryTestTree";
            tree.treeName = "RegistryTestTree";
            tree.root = root;
            tree.registries.Add(registry);
            m_Assets.Add(tree);
            return tree;
        }

        private StateTreeRunner MakeRunner(StateTreeAsset tree, StateTreeContextHost player)
        {
            var go = new GameObject("RegistryRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(player.transform);
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

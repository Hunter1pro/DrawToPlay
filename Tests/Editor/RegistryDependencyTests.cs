using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M22 dependency edge —
    /// <see cref="StateTreeRegistryAsset.dependsOn"/> and its closure. This is the runtime half
    /// of "which data may a graph name": the editor half (<c>GraphRegistryScope</c>, which finds
    /// the ROOTS by looking for the registry row that points at a graph) lives behind the Graph
    /// Toolkit firewall and cannot be referenced from a test assembly, but everything it does
    /// after picking roots is this method — so the traversal is pinned here, cycles included.
    /// </summary>
    [TestFixture]
    public sealed class RegistryDependencyTests
    {
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            m_Assets.Clear();
        }

        [Test]
        public void CollectWithDependencies_TakesTheRootFirst_WhenNothingIsDeclared()
        {
            ItemRegistry root = MakeRegistry();

            var collected = new List<StateTreeRegistryAsset>();
            root.CollectWithDependencies(collected);

            Assert.AreEqual(new[] { root }, collected,
                "A registry with no declared dependencies is its own whole scope.");
        }

        [Test]
        public void CollectWithDependencies_FollowsTheChain_Transitively()
        {
            ItemRegistry root = MakeRegistry();
            ItemRegistry middle = MakeRegistry();
            ItemRegistry leaf = MakeRegistry();
            root.dependsOn.Add(middle);
            middle.dependsOn.Add(leaf);

            var collected = new List<StateTreeRegistryAsset>();
            root.CollectWithDependencies(collected);

            Assert.AreEqual(new StateTreeRegistryAsset[] { root, middle, leaf }, collected,
                "The scope is the transitive closure, roots first — a dialog registry that "
                + "depends on items which depend on tags reaches all three.");
        }

        [Test]
        public void CollectWithDependencies_SurvivesACycle()
        {
            ItemRegistry a = MakeRegistry();
            ItemRegistry b = MakeRegistry();
            a.dependsOn.Add(b);
            b.dependsOn.Add(a);

            var collected = new List<StateTreeRegistryAsset>();
            a.CollectWithDependencies(collected);

            Assert.AreEqual(2, collected.Count,
                "Two registries naming each other is a legal authoring state, not a stack "
                + "overflow: each is visited once.");
            CollectionAssert.AreEquivalent(new StateTreeRegistryAsset[] { a, b }, collected);
        }

        [Test]
        public void CollectWithDependencies_IgnoresNullRows_AndSelfReference()
        {
            ItemRegistry root = MakeRegistry();
            root.dependsOn.Add(null);
            root.dependsOn.Add(root);

            var collected = new List<StateTreeRegistryAsset>();
            root.CollectWithDependencies(collected);

            Assert.AreEqual(new StateTreeRegistryAsset[] { root }, collected,
                "An empty slot the author has not filled in yet, and a registry that names "
                + "itself, both cost nothing.");
        }

        [Test]
        public void CollectWithDependencies_AccumulatesAcrossRoots_WithoutDuplicating()
        {
            ItemRegistry shared = MakeRegistry();
            ItemRegistry first = MakeRegistry();
            ItemRegistry second = MakeRegistry();
            first.dependsOn.Add(shared);
            second.dependsOn.Add(shared);

            // The editor collects several roots into ONE list — a graph reached from two
            // registry rows — so the accumulator has to keep de-duplicating across calls.
            var collected = new List<StateTreeRegistryAsset>();
            first.CollectWithDependencies(collected);
            second.CollectWithDependencies(collected);

            Assert.AreEqual(new StateTreeRegistryAsset[] { first, shared, second }, collected,
                "A registry both roots depend on appears once, at its first reachable position.");
        }

        private ItemRegistry MakeRegistry()
        {
            var registry = ScriptableObject.CreateInstance<ItemRegistry>();
            m_Assets.Add(registry);
            return registry;
        }
    }
}

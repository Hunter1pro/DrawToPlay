using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M30.1 — one type model for keys and graph parameters, and the rule about where choices
    /// come from.
    ///
    /// Two things are worth pinning here and nothing else is. First, that a richer type is an
    /// AUTHORING refinement of a primitive that is already stored: get that wrong and every
    /// blackboard read in the project has to learn a new vocabulary, which is exactly the
    /// migration this design exists to avoid. Second, that offers come from the DECLARED
    /// neighbourhood — a type may name a catalog, but an asset that never declared it gets
    /// nothing, because a dependency is a statement rather than a comment.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeValueTypeTests
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
        public void ARicherType_RidesInAPrimitiveThatAlreadyExists()
        {
            ItemRegistry items = Registry<ItemRegistry>("Items");

            Assert.That(StateTreeValueType.Of(StateTreeKeyKind.Float).Storage,
                Is.EqualTo(StateTreeKeyKind.Float), "a primitive is itself");
            Assert.That(StateTreeValueType.RowsOf(items).Storage,
                Is.EqualTo(StateTreeKeyKind.String),
                "a row rides as its NAME — what every runtime lookup already reads");
            Assert.That(new StateTreeValueType { kind = StateTreeValueKind.Payload }.Storage,
                Is.EqualTo(StateTreeKeyKind.Object),
                "a payload rides in the object slot an announcement already used");
        }

        [Test]
        public void AParameterWithNoDeclaredType_MeansTheKindItAlwaysMeant()
        {
            var plain = new GraphTaskParameter { name = "speed", kind = GraphTaskParameterKind.Float };
            var text = new GraphTaskParameter { name = "who", kind = GraphTaskParameterKind.String };

            Assert.That(plain.TypeOf().IsPlain, Is.True);
            Assert.That(plain.TypeOf().primitive, Is.EqualTo(StateTreeKeyKind.Float));
            Assert.That(text.TypeOf().primitive, Is.EqualTo(StateTreeKeyKind.String),
                "every parameter authored before M30.1 keeps reading as what it was");
        }

        [Test]
        public void RowsAreOffered_OnlyThroughADeclaredDependency()
        {
            ItemRegistry items = Registry<ItemRegistry>("Items");
            items.entries.Add(new ItemDef { id = "item.rope", name = "rope" });
            items.entries.Add(new ItemDef { id = "item.lamp", name = "lamp" });

            // A catalog that names the items — the declaration — and one that does not.
            LevelObjectRegistry declaring = Registry<LevelObjectRegistry>("Declaring");
            declaring.dependsOn.Add(items);
            LevelObjectRegistry silent = Registry<LevelObjectRegistry>("Silent");

            StateTreeValueType type = StateTreeValueType.RowsOf(items);
            var offered = new List<StateTreeRegistryEntry>();

            StateTreeOffers.RowsFor(type, declaring, offered);
            Assert.That(offered.Count, Is.EqualTo(2), "declared: the rows are on offer");

            StateTreeOffers.RowsFor(type, silent, offered);
            Assert.That(offered, Is.Empty,
                "undeclared: nothing, because a type pointing outside the neighbourhood is the "
                + "broken link this rule exists to surface");
        }

        [Test]
        public void TheNeighbourhood_IsTransitive_AndATreeSpeaksItsData()
        {
            ItemRegistry items = Registry<ItemRegistry>("Items");
            EffectRegistry effects = Registry<EffectRegistry>("Effects");
            items.dependsOn.Add(effects);

            LevelObjectRegistry level = Registry<LevelObjectRegistry>("Level");
            level.dependsOn.Add(items);

            var reachable = new List<StateTreeRegistryAsset>();
            StateTreeOffers.ReachableRegistries(level, reachable);
            Assert.That(reachable, Does.Contain(items));
            Assert.That(reachable, Does.Contain(effects),
                "a dependency's dependencies are reachable — the closure, not one hop");

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            m_Assets.Add(tree);
            tree.registries.Add(items);
            StateTreeOffers.ReachableRegistries(tree, reachable);
            Assert.That(reachable, Does.Contain(items), "a tree declares its Data the same way");
            Assert.That(reachable, Does.Contain(effects));
        }

        [Test]
        public void ATypeDescribesItself_SoAListReadsWithoutOpeningIt()
        {
            ItemRegistry items = Registry<ItemRegistry>("Items");
            Assert.That(StateTreeValueType.RowsOf(items).Describe(), Does.Contain("row"));
            Assert.That(StateTreeValueType.Of(StateTreeKeyKind.Bool).Describe(), Is.EqualTo("bool"));
            Assert.That(new StateTreeValueType
            {
                kind = StateTreeValueKind.Payload,
                payloadTypeName = "PowerOfFire.DrawToPlay.CutsceneResult"
            }.Describe(), Is.EqualTo("CutsceneResult"), "the short name is what a row has space for");
        }

        private T Registry<T>(string name) where T : ScriptableObject
        {
            var registry = ScriptableObject.CreateInstance<T>();
            registry.name = name;
            m_Assets.Add(registry);
            return registry;
        }
    }
}

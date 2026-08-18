using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M30.5 — the map's data, asked questions instead of looked at.
    ///
    /// What a map must get right is not how it draws: it is WHICH EDGES EXIST. Every one of them
    /// was authored by somebody — a Depends On, a def's declared catalog, the body it spawns, a
    /// row's picked reference — and the two roll-ups (a row's use is an edge to its catalog, a
    /// caller is an edge to the def that answers) are what make the picture answerable rather
    /// than a hairball of forty rows.
    /// </summary>
    [TestFixture]
    public sealed class DependencyGraphTests
    {
        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ADeclaredNeighbourhood_IsAnEdge_PointingAtWhatIsDependedOn()
        {
            var attributes = Make<AttributeRegistry>("Attributes");
            attributes.entries.Add(new AttributeDef { id = "attribute.health", name = "health" });

            var items = Make<ItemRegistry>("Items");
            items.dependsOn.Add(attributes);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(items, index);
            AssetWireScan.ScanRegistry(attributes, index);

            DependencyGraph graph = DependencyGraph.Build(index);
            Assert.That(Edge(graph, items, attributes), Is.True,
                "A → B reads 'A refers to B' — Depends On is the plainest edge there is");
            Assert.That(Edge(graph, attributes, items), Is.False, "and it is one-way");
        }

        [Test]
        public void ADefsCatalogAndItsBody_AreBothEdges()
        {
            var attributes = Make<AttributeRegistry>("Attributes");
            var recipes = Make<CraftRecipeRegistry>("Recipes");
            var body = new GameObject("Door");
            body.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(body);

            var def = Make<ServiceDef>("door");
            def.serviceName = "door";
            def.registry = recipes;
            def.declares.Add(attributes);
            def.body.prefab = body;

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanServiceDef(def, index);

            DependencyGraph graph = DependencyGraph.Build(index);
            Assert.That(Edge(graph, def, recipes), Is.True, "the catalog it manages");
            Assert.That(Edge(graph, def, attributes), Is.True, "the catalog it declares");
            Assert.That(Edge(graph, def, body), Is.True,
                "and the body it spawns — a def that stood alone on the map would be a lie");
            Assert.That(graph.IndexOf(body), Is.GreaterThanOrEqualTo(0));
            Assert.That(graph.nodes[graph.IndexOf(body)].kind,
                Is.EqualTo(DependencyGraph.NodeKind.Prefab));
        }

        [Test]
        public void ARowsUse_RollsUpToTheCatalogThatHoldsIt_AndRepeatsAreCounted()
        {
            var cues = Make<CueRegistry>("Cues");
            cues.entries.Add(new CueDef { id = "cue.impact", name = "impact" });

            var effects = Make<EffectRegistry>("Effects");
            for (int i = 0; i < 2; i++)
            {
                var effect = new EffectDef { id = "effect." + i, name = "hit-" + i };
                effect.cue.entryId = "cue.impact";
                effect.cue.entryName = "impact";
                effects.entries.Add(effect);
            }

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(cues, index);
            AssetWireScan.ScanRegistry(effects, index);

            DependencyGraph graph = DependencyGraph.Build(index);
            int from = graph.IndexOf(effects);
            int to = graph.IndexOf(cues);
            Assert.That(from, Is.GreaterThanOrEqualTo(0));
            Assert.That(to, Is.GreaterThanOrEqualTo(0));

            var counted = 0;
            for (int i = 0; i < graph.edges.Count; i++)
            {
                if (graph.edges[i].from != from || graph.edges[i].to != to)
                    continue;
                counted++;
                Assert.That(graph.edges[i].kind, Is.EqualTo(DependencyGraph.EdgeKind.Row));
                Assert.That(graph.edges[i].count, Is.EqualTo(2),
                    "two rows picking the same cue is ONE edge that happened twice — a map "
                    + "with a line per row is a map nobody can read");
            }
            Assert.That(counted, Is.EqualTo(1));
            Assert.That(graph.Touching(from, to), Is.True);
        }

        [Test]
        public void ACaller_IsAnEdgeToTheDefThatAnswersIt_DerivedRequestsIncluded()
        {
            var def = Make<ServiceDef>("resource");
            def.serviceName = "resource";
            var has = new ServiceAttribute { writable = true };
            has.attribute.entryId = "attribute.health";
            has.attribute.entryName = "health";
            def.attributes.Add(has);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanServiceDef(def, index);
            Assert.That(index.requestOwners.ContainsKey("health.add"), Is.True,
                "a derived request is declared by the def that derives it");

            var chopper = Make<StateTreeAsset>("ChopTree");
            index.AddRequestCaller("health.add", new AssetWireScan.WireUse
            {
                context = chopper, description = "chop · writes 'health.add'"
            });

            DependencyGraph graph = DependencyGraph.Build(index);
            Assert.That(Edge(graph, chopper, def), Is.True,
                "calling a request is depending on whoever answers it");
        }

        [Test]
        public void TheMapCanDeclareANeighbourhood_AndRefusesTheNonsense()
        {
            var attributes = Make<AttributeRegistry>("Attributes");
            var def = Make<ServiceDef>("door");
            var items = Make<ItemRegistry>("Items");

            Assert.That(DependencyGraph.Declare(def, attributes), Is.True);
            Assert.That(def.declares, Does.Contain(attributes));
            Assert.That(DependencyGraph.Declare(def, attributes), Is.False, "already declared");

            Assert.That(DependencyGraph.Declare(items, attributes), Is.True);
            Assert.That(items.dependsOn, Does.Contain(attributes),
                "a registry declares in Depends On — the same sentence in its own words");
            Assert.That(DependencyGraph.Declare(items, items), Is.False,
                "declaring itself would be a neighbourhood of one");
        }

        private static bool Edge(DependencyGraph graph, Object from, Object to)
        {
            int a = graph.IndexOf(from);
            int b = graph.IndexOf(to);
            if (a < 0 || b < 0)
                return false;
            for (int i = 0; i < graph.edges.Count; i++)
            {
                if (graph.edges[i].from == a && graph.edges[i].to == b)
                    return true;
            }
            return false;
        }

        private T Make<T>(string assetName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            m_Junk.Add(asset);
            return asset;
        }
    }
}

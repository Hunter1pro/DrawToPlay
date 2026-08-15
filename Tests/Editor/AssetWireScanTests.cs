using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The reverse wire map (M23 review: "see who used the ability") — the scan walks the
    /// wires everything else authors forward and answers backwards: a row knows its users
    /// (id wires and name-only references both), an asset knows its referencers (Data
    /// listings, dependsOn edges, a row's tree, a prefab component's table), and an
    /// argument row's ⛃ entry wire counts as a use of the picked row.
    /// </summary>
    [TestFixture]
    public sealed class AssetWireScanTests
    {
        private readonly List<Object> m_Objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            m_Objects.Clear();
        }

        private T Make<T>(string assetName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            m_Objects.Add(asset);
            return asset;
        }

        [Test]
        public void ARowsPickedRef_IsAUse_OfTheReferencedRow()
        {
            var impact = new CueDef { id = "cue.impact", name = "impact" };

            var effects = Make<EffectRegistry>("Effects");
            var hit = new EffectDef { id = "effect.hit", name = "strike-hit" };
            hit.cue.entryId = "cue.impact";
            hit.cue.entryName = "impact";
            effects.entries.Add(hit);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(effects, index);

            List<AssetWireScan.WireUse> uses = AssetWireScan.UsersOfRow(index, impact);
            Assert.AreEqual(1, uses.Count);
            StringAssert.Contains("row 'strike-hit'", uses[0].description);
            StringAssert.Contains("cue", uses[0].description);
            Assert.AreSame(effects, uses[0].context, "ping lands on the asset holding the wire");
        }

        [Test]
        public void ATreeTasksRef_IsAUse_NamingTheState()
        {
            var strike = new AbilityDef { id = "ability.strike", name = "strike" };

            var tree = Make<StateTreeAsset>("EnemyTree");
            var root = Make<StateTreeNodeAsset>("root");
            root.nodeId = "root";
            tree.root = root;
            var striking = Make<StateTreeNodeAsset>("striking");
            striking.nodeId = "striking";
            root.children.Add(striking);
            var activate = Make<ActivateAbilityTask>("activate");
            activate.ability.entryId = "ability.strike";
            striking.tasks.Add(activate);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanTree(tree, index);

            List<AssetWireScan.WireUse> uses = AssetWireScan.UsersOfRow(index, strike);
            Assert.AreEqual(1, uses.Count);
            StringAssert.Contains("'striking'", uses[0].description);
            StringAssert.Contains("ActivateAbilityTask", uses[0].description);
        }

        [Test]
        public void DataListings_RowTreeRefs_AndDependsOn_AreAssetUses()
        {
            var registry = Make<AbilityRegistry>("Abilities");
            var tree = Make<StateTreeAsset>("PushTree");

            var user = Make<StateTreeAsset>("PlayerTree");
            user.registries.Add(registry);

            var push = new AbilityDef { id = "ability.push", name = "push", tree = tree };
            registry.entries.Add(push);

            var effects = Make<EffectRegistry>("Effects");
            registry.dependsOn.Add(effects);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanTree(user, index);
            AssetWireScan.ScanRegistry(registry, index);

            List<AssetWireScan.WireUse> registryUses =
                AssetWireScan.UsersOfAsset(index, registry);
            Assert.AreEqual(1, registryUses.Count);
            StringAssert.Contains("listed as Data", registryUses[0].description);

            List<AssetWireScan.WireUse> treeUses = AssetWireScan.UsersOfAsset(index, tree);
            Assert.AreEqual(1, treeUses.Count);
            StringAssert.Contains("row 'push'", treeUses[0].description);
            StringAssert.Contains("tree", treeUses[0].description);

            List<AssetWireScan.WireUse> effectUses =
                AssetWireScan.UsersOfAsset(index, effects);
            Assert.AreEqual(1, effectUses.Count);
            StringAssert.Contains("dependsOn", effectUses[0].description);
        }

        [Test]
        public void APrefabComponent_UsesItsTable_AndItsSeedRows()
        {
            var health = new AttributeDef { id = "attribute.health", name = "health" };
            var table = Make<ProgressionTable>("Progression");

            var go = new GameObject("Actor");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            m_Objects.Add(go);
            var attributes = go.AddComponent<AttributeComponent>();
            attributes.table = table;
            var seed = new AttributeComponent.Seed();
            seed.attribute.entryName = "health";   // a name-only reference still counts
            attributes.seeds.Add(seed);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanPrefab(go, index);

            List<AssetWireScan.WireUse> tableUses = AssetWireScan.UsersOfAsset(index, table);
            Assert.AreEqual(1, tableUses.Count);
            StringAssert.Contains("AttributeComponent", tableUses[0].description);

            List<AssetWireScan.WireUse> rowUses = AssetWireScan.UsersOfRow(index, health);
            Assert.AreEqual(1, rowUses.Count,
                "the seed's name-only reference matched the row by its current name");
            StringAssert.Contains("seeds", rowUses[0].description);
        }

        [Test]
        public void AChain_FollowsThroughTheHoldingRow_ToTheRealConsumer()
        {
            // The review case: the push tree's user list stopped at "row 'push'" — the
            // chain must continue to whoever activates that row.
            var tree = Make<StateTreeAsset>("PushTree");

            var abilities = Make<AbilityRegistry>("Abilities");
            var push = new AbilityDef { id = "ability.push", name = "push", tree = tree };
            abilities.entries.Add(push);

            var player = Make<StateTreeAsset>("PlayerTree");
            var root = Make<StateTreeNodeAsset>("root");
            root.nodeId = "pushing";
            player.root = root;
            var activate = Make<ActivateAbilityTask>("activate");
            activate.ability.entryId = "ability.push";
            root.tasks.Add(activate);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(abilities, index);
            AssetWireScan.ScanTree(player, index);

            var lines = new List<AssetWireScan.ChainLine>();
            AssetWireScan.CollectChain(index,
                AssetWireScan.UsersOfAsset(index, tree), lines, 0,
                new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal));

            Assert.AreEqual(2, lines.Count, "the row hop AND the consumer behind it");
            Assert.AreEqual(0, lines[0].depth);
            StringAssert.Contains("row 'push'", lines[0].description);
            Assert.AreEqual(1, lines[1].depth);
            StringAssert.Contains("PlayerTree", lines[1].description);
            StringAssert.Contains("ActivateAbilityTask", lines[1].description);
        }

        [Test]
        public void TheMap_RollsRowWiresUp_ToAssetEdges_BothDirections()
        {
            // The map's granularity: PlayerTree → (ability row 'push') → Abilities registry
            // is ONE asset edge each way, remembering how many wires it stands for.
            var pushTree = Make<StateTreeAsset>("PushTree");
            var abilities = Make<AbilityRegistry>("Abilities");
            var push = new AbilityDef { id = "ability.push", name = "push", tree = pushTree };
            abilities.entries.Add(push);

            var player = Make<StateTreeAsset>("PlayerTree");
            var root = Make<StateTreeNodeAsset>("root");
            root.nodeId = "pushing";
            player.root = root;
            var activate = Make<ActivateAbilityTask>("activate");
            activate.ability.entryId = "ability.push";
            root.tasks.Add(activate);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(abilities, index);
            AssetWireScan.ScanTree(player, index);

            List<AssetWireGraph.GraphEdge> intoRegistry =
                AssetWireGraph.IncomingOf(index, abilities);
            Assert.AreEqual(1, intoRegistry.Count);
            Assert.AreSame(player, intoRegistry[0].other,
                "the row wire rolled up: the registry's referencer is the TREE");

            List<AssetWireGraph.GraphEdge> fromPlayer =
                AssetWireGraph.OutgoingOf(index, player);
            Assert.AreEqual(1, fromPlayer.Count);
            Assert.AreSame(abilities, fromPlayer[0].other,
                "forward: the tree's reference resolves to the row's OWNING registry");

            List<AssetWireGraph.GraphEdge> fromRegistry =
                AssetWireGraph.OutgoingOf(index, abilities);
            Assert.AreEqual(1, fromRegistry.Count);
            Assert.AreSame(pushTree, fromRegistry[0].other,
                "and the registry's own outgoing edge is the row's tree");
        }

        [Test]
        public void AnArgumentsEntryWire_IsAUse_OfThePickedRow()
        {
            var ridge = new LevelDef { id = "level.ridge", name = "ridge" };

            var manifest = Make<LevelObjectRegistry>("YardObjects");
            var exit = new LevelObjectDef { id = "place.exit", name = "Road" };
            exit.parameters.values.Add(new GraphTaskParameterOverride
            {
                name = "destination",
                enabled = true,
                stringValue = "ridge",
                entryId = "level.ridge"
            });
            manifest.entries.Add(exit);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(manifest, index);

            List<AssetWireScan.WireUse> uses = AssetWireScan.UsersOfRow(index, ridge);
            Assert.AreEqual(1, uses.Count);
            StringAssert.Contains("argument 'destination'", uses[0].description);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M39.2 — a view and its service talk in C#.
    ///
    /// The screens cannot be built here (a UIDocument needs a panel), so what is pinned is the
    /// seam they hang on: a verb RETURNS what happened and the board holds the same object
    /// (the panel the bench holds is told it in the same method — no events, M39.2b), not
    /// rows the def has to carry for a conversation nobody else is part of. And the defs, as
    /// the waystation generates them, carry only what a flow wires.
    /// </summary>
    [TestFixture]
    public sealed class BagSeamTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeContextHost m_Root;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Root = go.AddComponent<StateTreeContextHost>();
            m_Root.kind = StateTreeContextKind.Root;
            m_Root.autoStart = false;
            m_Root.Register();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Root != null)
                m_Root.Unregister();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void TheBench_TellsItsPanelWhatHappened_MadeOrRefused()
        {
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            items.entries.Add(new ItemDef { name = "wood" });
            items.entries.Add(new ItemDef { name = "skiff" });
            m_Junk.Add(items);
            var bagDef = ScriptableObject.CreateInstance<ServiceDef>();
            bagDef.serviceName = "inventory";
            bagDef.registry = items;
            m_Junk.Add(bagDef);
            var bag = new InventoryService(m_Root, bagDef);
            m_Root.Provide(bag);
            m_Root.Provide(typeof(IBag), bag);

            var recipes = ScriptableObject.CreateInstance<CraftRecipeRegistry>();
            recipes.dependsOn.Add(items);
            var skiff = new CraftRecipeDef { name = "skiff", result = { entryName = "skiff" } };
            skiff.costs.Add(new CraftRecipeDef.Cost { item = { entryName = "wood" }, count = 3 });
            recipes.entries.Add(skiff);
            m_Junk.Add(recipes);
            var craftDef = ScriptableObject.CreateInstance<ServiceDef>();
            craftDef.serviceName = "craft";
            craftDef.registry = recipes;
            craftDef.settings.values.Add(new ServiceSettingValue
            {
                name = nameof(CraftService.stationTag), stringValue = "station"
            });
            m_Junk.Add(craftDef);
            var bench = new CraftService(m_Root, craftDef);
            m_Root.Provide(bench);
            bench.Tick(0f);   // injects the bag

            CraftResult refused = bench.Craft("skiff");
            Assert.IsFalse(refused.made);
            Assert.That(refused.line, Does.Contain("wood"), "a refusal says what it wants");
            Assert.AreSame(refused, m_Root.Context.blackboard[CraftResult.Key],
                "a refusal is told like a success");

            bag.Add("wood", 3);
            CraftResult made = bench.Craft("skiff");
            Assert.IsTrue(made.made);
            Assert.AreEqual(1, bag.Count("skiff"));
            Assert.AreEqual(0, bag.Count("wood"));
            Assert.AreSame(made, m_Root.Context.blackboard[CraftResult.Key],
                "the answer returned IS the announcement");
        }

        [Test]
        public void OneDetect_ThePanelAndTheSwingCannotDisagree_AboutWhichBench()
        {
            // M39.4: the bench is found in ONE place — CraftService.at — with one range. The
            // panel shows its offer and an empty craft.begin makes its recipe, so a player
            // standing between two stations gets the same answer on screen and from the swing.
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            items.entries.Add(new ItemDef { name = "wood" });
            items.entries.Add(new ItemDef { name = "skiff" });
            items.entries.Add(new ItemDef { name = "raft" });
            m_Junk.Add(items);
            var bagDef = ScriptableObject.CreateInstance<ServiceDef>();
            bagDef.serviceName = "inventory";
            bagDef.registry = items;
            m_Junk.Add(bagDef);
            var bag = new InventoryService(m_Root, bagDef);
            m_Root.Provide(bag);
            m_Root.Provide(typeof(IBag), bag);

            var recipes = ScriptableObject.CreateInstance<CraftRecipeRegistry>();
            recipes.dependsOn.Add(items);
            var skiff = new CraftRecipeDef { name = "skiff", result = { entryName = "skiff" } };
            skiff.costs.Add(new CraftRecipeDef.Cost { item = { entryName = "wood" }, count = 1 });
            var raft = new CraftRecipeDef { name = "raft", result = { entryName = "raft" } };
            raft.costs.Add(new CraftRecipeDef.Cost { item = { entryName = "wood" }, count = 1 });
            recipes.entries.Add(skiff);
            recipes.entries.Add(raft);
            m_Junk.Add(recipes);
            var craftDef = ScriptableObject.CreateInstance<ServiceDef>();
            craftDef.serviceName = "craft";
            craftDef.registry = recipes;
            craftDef.settings.values.Add(new ServiceSettingValue
            {
                name = nameof(CraftService.stationTag), stringValue = "station"
            });
            craftDef.settings.values.Add(new ServiceSettingValue
            {
                name = nameof(CraftService.benchRange), floatValue = 3f
            });
            m_Junk.Add(craftDef);

            // A level with a world, two stations 4 m apart, and a player between them.
            var levelGo = new GameObject("Level") { hideFlags = HideFlags.HideAndDontSave };
            levelGo.transform.SetParent(m_Root.transform);
            m_Junk.Add(levelGo);
            var level = levelGo.AddComponent<StateTreeContextHost>();
            level.kind = StateTreeContextKind.Level;
            level.autoStart = false;
            level.Register();
            var world = new WorldService(level, null);
            level.Provide(world);
            WorldObjectBehaviour shipyard = Station(levelGo, "Shipyard", "skiff", new Vector3(-2f, 0f, 0f));
            WorldObjectBehaviour raftyard = Station(levelGo, "Raftyard", "raft", new Vector3(2f, 0f, 0f));

            var playerGo = new GameObject("Player") { hideFlags = HideFlags.HideAndDontSave };
            playerGo.transform.SetParent(levelGo.transform);
            m_Junk.Add(playerGo);
            var player = playerGo.AddComponent<StateTreeContextHost>();
            player.kind = StateTreeContextKind.Player;
            player.autoStart = false;
            player.Register();

            var bench = new CraftService(m_Root, craftDef);
            m_Root.Provide(bench);
            bag.Add("wood", 2);

            try
            {
                playerGo.transform.position = new Vector3(-1f, 0f, 0f);   // nearer the shipyard
                bench.Tick(0f);
                Assert.AreSame(shipyard, bench.at);
                Assert.AreEqual("skiff", bench.offer.recipeName, "the panel would show the skiff");
                CraftResult made = bench.Craft("");
                Assert.IsTrue(made.made);
                Assert.AreEqual("skiff", made.recipeName, "and the swing made the skiff — same answer");

                playerGo.transform.position = new Vector3(1f, 0f, 0f);    // nearer the raftyard
                bench.Tick(0f);
                Assert.AreSame(raftyard, bench.at);
                Assert.AreEqual("raft", bench.Craft("").recipeName);

                playerGo.transform.position = new Vector3(0f, 0f, 9f);    // away from both
                bench.Tick(0f);
                Assert.IsNull(bench.at);
                Assert.IsNull(bench.offer);
                Assert.That(bench.Craft("").refusal, Does.Contain("no station"));
            }
            finally
            {
                player.Unregister();
                level.Unregister();
            }
        }

        private WorldObjectBehaviour Station(GameObject levelGo, string stationName, string recipe, Vector3 at)
        {
            var go = new GameObject(stationName) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(levelGo.transform);
            go.transform.position = at;
            m_Junk.Add(go);
            var citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.entryName = recipe;
            citizen.tags.Add("station");
            citizen.RegisterToWorld();
            return citizen;
        }

        [Test]
        public void TheDefs_CarryOnlyWhatAFlowWires()
        {
            var inventory = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21InventoryService.asset");
            var craft = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21CraftService.asset");
            Assume.That(inventory, Is.Not.Null);
            Assume.That(craft, Is.Not.Null);

            var bagKeys = inventory.requests.ConvertAll(r => r.key);
            Assert.That(bagKeys, Is.EquivalentTo(new[] { "bag.add", "bag.remove", "bag.open" }),
                "the keeper's gift, the warden's take and 'show me' — the bag's own buttons are "
                + "its screen's business");
            foreach (ServiceRequest row in inventory.requests)
            {
                Assert.That(row.internalOnly, Is.False, row.key + ": no row is for nobody");
                Assert.That(row.reactions, Is.Empty, row.key + ": the screen redraws on its own");
            }

            var craftKeys = craft.requests.ConvertAll(r => r.key);
            Assert.That(craftKeys, Is.EquivalentTo(new[] { "craft.begin" }));
            ServiceRequest begin = craft.requests[0];
            Assert.That(begin.reactions, Is.Empty, "the panel hears the result from its service");
            Assert.That(begin.reactionGraph, Is.Not.Null, "the HUD line stays a drawn flow");
        }
    }
}

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
    /// seam they hang on: the service publishes what the screen draws and tells it what
    /// happened, as events and return values — not as rows the def has to carry for a
    /// conversation nobody else is part of. And the defs, as the waystation generates them,
    /// carry only what a flow wires.
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

            var told = new List<CraftResult>();
            bench.crafted += told.Add;

            CraftResult refused = bench.Craft("skiff");
            Assert.IsFalse(refused.made);
            Assert.That(told, Has.Count.EqualTo(1), "a refusal is told like a success");
            Assert.AreSame(refused, told[0]);
            Assert.That(refused.line, Does.Contain("wood"));

            bag.Add("wood", 3);
            CraftResult made = bench.Craft("skiff");
            Assert.IsTrue(made.made);
            Assert.AreEqual(1, bag.Count("skiff"));
            Assert.AreEqual(0, bag.Count("wood"));
            Assert.AreSame(made, told[1], "and the panel hears the same object the board gets");
            Assert.AreSame(made, m_Root.Context.blackboard[CraftResult.Key]);
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

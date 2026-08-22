using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M35.2 found a SAVE that fired in the window between the file being loaded and the player
    /// arriving: the bag was fresh then, wore nothing, and the save wrote that over the file.
    /// M39 removes the window rather than guarding it — the bag owns what it holds and wears,
    /// a load puts the whole snapshot back at once, and no body is needed for any of it.
    /// </summary>
    [TestFixture]
    public sealed class OutpostSaveTests
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
        public void ALoadedBag_IsCarriedAndWorn_BeforeAnyPlayerExists()
        {
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            items.entries.Add(new ItemDef { name = "hammer", slot = { entryId = "slot.hand" } });
            m_Junk.Add(items);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "inventory";
            def.registry = items;
            m_Junk.Add(def);
            var bag = new InventoryService(m_Root, def);
            m_Root.Provide(bag);

            var progress = new OutpostProgressService();
            var save = new OutpostSaveService(progress, null, bag, m_Root.gameObject);

            // WHAT THE FILE SAID: a hammer carried and worn.
            var loaded = new OutpostSaveData();
            loaded.bag = new InventoryService.SaveState { hasState = true };
            loaded.bag.itemNames.Add("hammer");
            loaded.bag.itemCounts.Add(1);
            loaded.bag.wornItems.Add("hammer");
            save.Adopt(loaded);

            // NO PLAYER YET — the level is still loading — and the bag already has it all.
            Assert.AreEqual(1, bag.Count("hammer"));
            Assert.IsTrue(bag.IsEquipped("hammer"), "worn, with the grant waiting for a body");

            // A save in that window writes the truth, because the truth is in the bag.
            OutpostSaveData written = save.Capture();
            Assert.That(written.bag.itemNames, Is.EqualTo(new[] { "hammer" }));
            Assert.That(written.bag.wornItems, Is.EqualTo(new[] { "hammer" }));
        }

        [Test]
        public void ABornLevel_HandsItsQuestLineToTheSave_AndADyingOneReleasesIt()
        {
            // M40.3: the save watches for nothing. The level, at its start, adopts; at its end,
            // releases — and the last cursors are captured at that moment, so a level with no
            // quest line (the ridge) leaves the file holding what the last one knew.
            var registry = ScriptableObject.CreateInstance<ObjectiveRegistry>();
            var first = new ObjectiveDef { id = "o.first", name = "first", kind = ObjectiveKind.Dialog };
            var second = new ObjectiveDef { id = "o.second", name = "second", kind = ObjectiveKind.Dialog };
            registry.entries.Add(first);
            registry.entries.Add(second);
            m_Junk.Add(registry);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "objectives";
            def.scope = StateTreeContextKind.Level;
            def.registry = registry;
            m_Junk.Add(def);

            var save = new OutpostSaveService(new OutpostProgressService(), null, null, m_Root.gameObject);
            save.Adopt(new OutpostSaveData
            {
                objectives = new ObjectiveService.SaveState { hasState = true, currentName = "second" }
            });

            var objectives = new ObjectiveService(m_Root, def);
            save.AdoptObjectives(objectives);
            Assert.AreSame(second, objectives.current, "the born quest line got the file's cursor");

            objectives.Activate(first);
            Assert.AreEqual("first", save.Capture().objectives.currentName, "captured live while adopted");

            save.ReleaseObjectives(objectives);
            objectives.Dispose();
            Assert.AreEqual("first", save.Capture().objectives.currentName,
                "released: the last cursors ride in the file, and no disposed service is read");
        }
    }
}

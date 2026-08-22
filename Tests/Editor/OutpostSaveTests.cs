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
    }
}

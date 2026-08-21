using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M35.2 — the loadout survives a round trip.
    ///
    /// What was wrong was not the wearing and not the reading: it was a SAVE that fired in the
    /// window between the file being loaded and the player arriving. The bag is fresh then and
    /// wears nothing, and the save captured that empty loadout over the one in the file — then
    /// the restore, which reads what the save had just written, re-wore nothing. The hammer was
    /// in the file right up until the session that was about to put it back on.
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
        public void ASaveBeforeThePlayerIsBack_KeepsTheLoadoutItHasNotRestoredYet()
        {
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
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
            loaded.itemNames.Add("hammer");
            loaded.itemCounts.Add(1);
            loaded.equipment = new InventoryService.SaveState { hasState = true };
            loaded.equipment.slotIds.Add("slot.hand");
            loaded.equipment.itemNames.Add("hammer");
            save.Adopt(loaded);

            // NO PLAYER YET — the level is still loading — and the live bag wears nothing.
            Assert.That(bag.CaptureState().itemNames, Is.Empty);

            // A save fires in that window (the progress restore marks the file dirty a second
            // before the level has a player). It must write what it LAST KNEW, not what the
            // fresh bag has, for the loadout exactly as it already did for the counts.
            OutpostSaveData written = save.Capture();
            Assert.That(written.equipment.itemNames, Is.EqualTo(new[] { "hammer" }),
                "the loadout the restore has not put back on yet is not 'nothing worn'");
            Assert.That(written.itemNames, Is.EqualTo(new[] { "hammer" }),
                "the counts already had this guard");
        }
    }
}

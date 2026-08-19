using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M31.2 — a tag's uses are known PER ENTITY, and a row that is wired cannot be deleted.
    ///
    /// The database rule, applied where this project needed it most: a tag is the most-wired
    /// thing here and the only wire that is not an id, so deleting its row leaves placements
    /// carrying a word no vocabulary holds — and nothing fails, the quest just never completes.
    /// We know the references, so the delete is refused instead.
    ///
    /// Per entity is the other half: "the manifest uses this tag" is true and useless; "the
    /// placement 'place.raider' wears it" is the thing an author can act on.
    /// </summary>
    [TestFixture]
    public sealed class WorldTagUsageTests
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
        public void APlacementsTag_IsIndexedAgainstThatPlacement_NotJustItsFile()
        {
            var manifest = Make<LevelObjectRegistry>("Yard");
            var raider = new LevelObjectDef { id = "place.raider", name = "Raider" };
            raider.tags.Add(new LevelObjectTagRef { tag = "objective.raider" });
            manifest.entries.Add(raider);
            manifest.entries.Add(new LevelObjectDef { id = "place.player", name = "Player" });

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(manifest, index);

            List<AssetWireScan.WireUse> uses =
                AssetWireScan.UsersOfTag(index, "objective.raider");
            Assert.That(uses.Count, Is.EqualTo(1));
            Assert.That(uses[0].viaRow, Is.SameAs(raider),
                "the ROW is the entity — a use rolled up to the manifest would name the file "
                + "and lose the object");
            Assert.That(uses[0].description, Does.Contain("objective.raider"));
            Assert.That(AssetWireScan.UsersOfTag(index, "objective.relic"), Is.Empty);
        }

        [Test]
        public void AListOfTags_IsIndexedElementByElement()
        {
            var abilities = Make<AbilityRegistry>("Abilities");
            var strike = new AbilityDef { id = "ability.strike", name = "strike" };
            strike.abilityTags.Add("Attack");
            strike.blockedByTags.Add("Guarded");
            strike.blockedByTags.Add("Recovering");
            abilities.entries.Add(strike);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(abilities, index);

            Assert.That(AssetWireScan.UsersOfTag(index, "Attack").Count, Is.EqualTo(1));
            Assert.That(AssetWireScan.UsersOfTag(index, "Guarded").Count, Is.EqualTo(1));
            Assert.That(AssetWireScan.UsersOfTag(index, "Recovering").Count, Is.EqualTo(1),
                "a tag list is a list of wires, not one wire");
        }

        [Test]
        public void AnUnmarkedStringIsNotATag()
        {
            var dialogs = Make<ItemRegistry>("Items");
            // 'player' as an item's display name is a word, not a tag — the FIELD says which,
            // and matching by value would have made every coincidence a wire.
            dialogs.entries.Add(new ItemDef
            {
                id = "item.doll", name = "doll", displayName = "player"
            });

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(dialogs, index);
            Assert.That(AssetWireScan.UsersOfTag(index, "player"), Is.Empty);
        }

        [Test]
        public void DeletingAFileIsRefusedByWhatPointsIntoIt_ButNotByItself()
        {
            // The asset-level gate reads the same index: everything OUTSIDE the file that names
            // anything INSIDE it. A registry naming its own rows must not veto its own deletion.
            var cues = Make<CueRegistry>("Cues");
            var impact = new CueDef { id = "cue.impact", name = "impact" };
            cues.entries.Add(impact);

            var effects = Make<EffectRegistry>("Effects");
            var hit = new EffectDef { id = "effect.hit", name = "strike-hit" };
            hit.cue.entryId = "cue.impact";
            hit.cue.entryName = "impact";
            effects.entries.Add(hit);

            var index = new AssetWireScan.Index();
            AssetWireScan.ScanRegistry(cues, index);
            AssetWireScan.ScanRegistry(effects, index);

            var breakers = new List<AssetWireScan.WireUse>();
            StateTreeRowGuard.Breakers(index, new Object[] { cues }, "", breakers);
            Assert.That(breakers.Count, Is.EqualTo(1),
                "the effect names a row of this file, so the file does not go");
            Assert.That(breakers[0].context, Is.SameAs(effects));

            breakers.Clear();
            StateTreeRowGuard.Breakers(index, new Object[] { effects }, "", breakers);
            Assert.That(breakers, Is.Empty,
                "nothing names the effects catalog, so deleting it breaks nobody");
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            m_Junk.Add(asset);
            return asset;
        }
    }
}

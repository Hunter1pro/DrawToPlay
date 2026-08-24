using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M37.2 — a sketch is filled by picking, and the validators ask now what the runtime would
    /// ask later: is the key free, is the action a word, is the class name taken.
    /// </summary>
    [TestFixture]
    public sealed class SubsystemSketchTests
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

        /// <summary>A sundial — the clock's shape under a name the project does not have, because the
        /// clock itself exists now and the validators say so, which is their job.</summary>
        private SubsystemSketch Sundial()
        {
            var sketch = ScriptableObject.CreateInstance<SubsystemSketch>();
            m_Junk.Add(sketch);
            sketch.serviceName = "sundial";
            sketch.scope = StateTreeContextKind.Root;
            sketch.capability = "sundial";
            sketch.requests.Add(new SketchRequest
            {
                key = "sundial.set", action = "set", valueHint = "hour, 0–23"
            });
            sketch.announcements.Add(new SketchAnnouncement
            {
                key = "sundial.dawn", description = "the hour crossed the start of day"
            });
            sketch.settings.Add(new SketchSetting
            {
                name = "secondsPerDay", kind = SketchSettingKind.Float, numberDefault = 120f,
                description = "How long a day takes."
            });
            sketch.settings.Add(new SketchSetting
            {
                name = "startHour", kind = SketchSettingKind.Int, numberDefault = 6f
            });
            return sketch;
        }

        [Test]
        public void TheSundialSketch_ValidatesGreen()
        {
            SubsystemSketch clock = Sundial();
            List<SketchFinding> findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings, Is.Empty, string.Join("\n", findings));
            Assert.That(clock.className, Is.EqualTo("SundialService"));
            Assert.That(clock.capabilityName, Is.EqualTo("ISundial"));
        }

        [Test]
        public void ATakenKey_ATakenClass_AndABadName_AreBlocked()
        {
            SubsystemSketch clock = Sundial();

            // THE KEY IS SERVED ALREADY: 'level.goto' belongs to the M21 level def.
            clock.requests.Add(new SketchRequest { key = "level.goto", action = "goto" });
            List<SketchFinding> findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("'level.goto' is already served")),
                Is.True, string.Join("\n", findings));
            clock.requests.RemoveAt(clock.requests.Count - 1);

            // THE CLASS EXISTS: 'craft' would become CraftService, which is somebody's file.
            clock.serviceName = "craft";
            findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("CraftService already exists")),
                Is.True, string.Join("\n", findings));

            // NOT ONE WORD.
            clock.serviceName = "Day Clock";
            findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("not one lowercase word")), Is.True);

            // A SETTING THAT IS NOT A NAME, and two actions spelt the same.
            clock.serviceName = "sundial";
            clock.settings.Add(new SketchSetting { name = "seconds per day" });
            clock.requests.Add(new SketchRequest { key = "sundial.reset", action = "set" });
            findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("needs a C# name")), Is.True);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("action 'set' is sketched twice")), Is.True);
        }

        [Test]
        public void APickedRowFromAnUndeclaredCatalog_IsAdviceNotABlock()
        {
            SubsystemSketch clock = Sundial();
            AttributeRegistry attributes = TempAttributes();
            var health = attributes.FindByName("health");
            Assert.That(health, Is.Not.Null, "the temporary catalog has health");
            clock.attributes.Add(new StateTreeEntryRef<AttributeDef>
            {
                entryId = health.id, entryName = health.name
            });

            List<SketchFinding> findings = SubsystemSketchValidator.Validate(clock);
            SketchFinding advice = findings.Find(f => f.section.StartsWith("Has"));
            Assert.That(advice, Is.Not.Null);
            Assert.That(advice.blocks, Is.False, "Generate will declare the catalog; no need to stop");
            Assert.That(advice.message, Does.Contain("Generate will add it"));

            clock.declares.Add(attributes);
            Assert.That(SubsystemSketchValidator.Validate(clock), Is.Empty);
            Assert.That(clock.DeclaredCatalogs, Does.Contain(attributes),
                "and the ⛃ pickers on the sketch offer what it declares");
        }

        [Test]
        public void TheGeneratedClass_DeclaresWhatTheSketchSaid()
        {
            SubsystemSketch clock = Sundial();
            clock.settings.Add(new SketchSetting
            {
                name = "stationTag", kind = SketchSettingKind.Tag, description = "what a station is"
            });
            clock.requests.Add(new SketchRequest { key = "sundial.fast-forward", action = "fast-forward" });
            string source = SubsystemGenerator.ClassSource(clock);

            Assert.That(source, Does.Contain("public sealed class SundialService : StateTreeService, ISundial"));
            Assert.That(source, Does.Contain("[ServiceActionContract(SetAction, \"hour, 0–23\")]"));
            Assert.That(source, Does.Contain("public const string SetAction = \"set\";"));
            Assert.That(source, Does.Contain("public const string FastForwardAction = \"fast-forward\";"),
                "a hyphenated action spells a PascalCase const");
            Assert.That(source, Does.Contain("public const string DawnKey = \"sundial.dawn\";"));
            Assert.That(source, Does.Contain("[ServiceSetting(120.0f, \"How long a day takes.\")]"));
            Assert.That(source, Does.Contain("public float secondsPerDay;"));
            Assert.That(source, Does.Contain("[ServiceSetting(6, \"\")]"));
            Assert.That(source, Does.Contain("public int startHour;"));
            Assert.That(source, Does.Contain("[WorldTag]"), "a tag setting is a picked setting");
            Assert.That(source, Does.Contain("case SetAction:"));
            Assert.That(source, Does.Contain("is not implemented yet"),
                "every verb starts out loud — a request that does nothing looks like one that worked");

            Assert.That(SubsystemGenerator.CapabilitySource(clock), Does.Contain("public interface ISundial"));
            Assert.That(SubsystemGenerator.ConstName("craft-start", "Action"), Is.EqualTo("CraftStartAction"));
            Assert.That(SubsystemGenerator.ConstName("3d", "Key"), Is.EqualTo("The3dKey"));
        }

        [Test]
        public void TheDefDeclaresTheHomeOfEveryPickedRow()
        {
            SubsystemSketch clock = Sundial();
            AttributeRegistry attributes = TempAttributes();
            var health = attributes.FindByName("health");
            clock.attributes.Add(new StateTreeEntryRef<AttributeDef> { entryId = health.id, entryName = health.name });

            List<StateTreeRegistryAsset> declared = SubsystemGenerator.DeclaredCatalogs(clock);
            Assert.That(declared, Does.Contain(attributes),
                "the def may NAME health only if it declares the catalog health lives in");
        }

        // A catalog the sketch can PICK FROM has to be an asset on disk — the generator finds
        // the home of a picked row by scanning the project. Owned by the test, gone after it.
        private const string k_TempFolder = "Assets/__SketchTests";
        private const string k_TempAttributes = k_TempFolder + "/Attributes.asset";

        private static AttributeRegistry TempAttributes()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AttributeRegistry>(k_TempAttributes);
            if (existing != null)
                return existing;
            if (!AssetDatabase.IsValidFolder(k_TempFolder))
                AssetDatabase.CreateFolder("Assets", "__SketchTests");
            var registry = ScriptableObject.CreateInstance<AttributeRegistry>();
            registry.entries.Add(new AttributeDef { id = "attr.health", name = "health" });
            AssetDatabase.CreateAsset(registry, k_TempAttributes);
            AssetDatabase.SaveAssets();
            return registry;
        }

        [TearDown]
        public void DropTempAssets()
        {
            if (AssetDatabase.IsValidFolder(k_TempFolder))
                AssetDatabase.DeleteAsset(k_TempFolder);
        }
    }
}

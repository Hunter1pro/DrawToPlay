using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M37.1 — the project, in subsystems: what the table of contents says about a def.
    /// </summary>
    [TestFixture]
    public sealed class SubsystemCatalogTests
    {
        private const string k_Folder = "Assets/DrawToPlay/Tests/Editor/_CatalogTemp";
        private const string k_DefPath = k_Folder + "/TocService.asset";
        private const string k_SketchPath = k_Folder + "/TocSketch.asset";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay/Tests/Editor", "_CatalogTemp");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(k_Folder);
        }

        [Test]
        public void ADefIsListedUnderItsScope_WithItsClassAndCounts_AndItsSketch()
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "toc";
            def.serviceTypeName = typeof(CraftService).FullName;
            def.scope = StateTreeContextKind.Level;
            def.requests.Add(new ServiceRequest { key = "toc.a", action = "a" });
            def.requests.Add(new ServiceRequest { key = "toc.b", action = "b" });
            def.settings.values.Add(new ServiceSettingValue { name = "benchRange", floatValue = 3f });
            AssetDatabase.CreateAsset(def, k_DefPath);

            var sketch = ScriptableObject.CreateInstance<SubsystemSketch>();
            sketch.serviceName = "toc";
            sketch.generatedDef = def;
            AssetDatabase.CreateAsset(sketch, k_SketchPath);
            AssetDatabase.SaveAssets();

            List<SubsystemCatalog.Entry> entries = SubsystemCatalog.Build();
            SubsystemCatalog.Entry mine = entries.Find(e => e.def == def);
            Assert.That(mine, Is.Not.Null, "every def in the project is in the table");
            Assert.That(mine.serviceType, Is.EqualTo(typeof(CraftService)));
            Assert.That(mine.isKind, Is.False);
            Assert.That(mine.requests, Is.EqualTo(2));
            Assert.That(mine.announcements, Is.EqualTo(1));
            Assert.That(mine.settings, Is.EqualTo(1));
            Assert.That(mine.installedIn, Is.Empty, "no scene references a temp def");
            Assert.That(mine.sketch, Is.SameAs(sketch), "the sketch it was generated from rides along");

            // THE M21 SPINE IS IN THE TABLE, installed where its scenes install it — read from
            // binary scene files through the dependency list, which is the only way to read them.
            SubsystemCatalog.Entry craft = entries.Find(e => e.def.name == "M21CraftService");
            Assert.That(craft, Is.Not.Null);
            Assert.That(craft.installedIn, Does.Contain("M21Root"));
            SubsystemCatalog.Entry world = entries.Find(e => e.def.name == "M21WorldService");
            Assert.That(world.installedIn, Is.EquivalentTo(new[] { "M21Cave", "M21Ridge", "M21Wreck", "M21Yard" }));

            // A KIND is a def that names no class and builds a body — listed apart.
            SubsystemCatalog.Entry kind = entries.Find(e => e.def.name == "M21Kind_Resource");
            Assert.That(kind.isKind, Is.True);
            Assert.That(kind.hasClass, Is.False);
        }

        [Test]
        public void ASketchNamesItsClassAndCapability_FromOneWord()
        {
            var sketch = ScriptableObject.CreateInstance<SubsystemSketch>();
            sketch.serviceName = "clock";
            Assert.That(sketch.className, Is.EqualTo("ClockService"));
            Assert.That(sketch.capabilityName, Is.EqualTo(""), "no capability unless asked");
            sketch.capability = "clock";
            Assert.That(sketch.capabilityName, Is.EqualTo("IClock"));
            sketch.capability = "ITimekeeper";
            Assert.That(sketch.capabilityName, Is.EqualTo("ITimekeeper"), "an I-name is kept");
            Object.DestroyImmediate(sketch);
        }
    }
}

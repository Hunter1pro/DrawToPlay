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

        /// <summary>The clock — the milestone's exit, as a sketch.</summary>
        private SubsystemSketch Clock()
        {
            var sketch = ScriptableObject.CreateInstance<SubsystemSketch>();
            m_Junk.Add(sketch);
            sketch.serviceName = "clock";
            sketch.scope = StateTreeContextKind.Root;
            sketch.capability = "clock";
            sketch.requests.Add(new SketchRequest
            {
                key = "clock.set", action = "set", valueHint = "hour, 0–23"
            });
            sketch.announcements.Add(new SketchAnnouncement
            {
                key = "clock.dawn", description = "the hour crossed the start of day"
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
        public void TheClockSketch_ValidatesGreen()
        {
            SubsystemSketch clock = Clock();
            List<SketchFinding> findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings, Is.Empty, string.Join("\n", findings));
            Assert.That(clock.className, Is.EqualTo("ClockService"));
            Assert.That(clock.capabilityName, Is.EqualTo("IClock"));
        }

        [Test]
        public void ATakenKey_ATakenClass_AndABadName_AreBlocked()
        {
            SubsystemSketch clock = Clock();

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
            clock.serviceName = "clock";
            clock.settings.Add(new SketchSetting { name = "seconds per day" });
            clock.requests.Add(new SketchRequest { key = "clock.reset", action = "set" });
            findings = SubsystemSketchValidator.Validate(clock);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("needs a C# name")), Is.True);
            Assert.That(findings.Exists(f => f.blocks && f.message.Contains("action 'set' is sketched twice")), Is.True);
        }

        [Test]
        public void APickedRowFromAnUndeclaredCatalog_IsAdviceNotABlock()
        {
            SubsystemSketch clock = Clock();
            var attributes = AssetDatabase.LoadAssetAtPath<AttributeRegistry>(
                AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:AttributeRegistry M21")[0]));
            var health = attributes.FindByName("health");
            Assert.That(health, Is.Not.Null, "the M21 attribute catalog has health");
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
    }
}

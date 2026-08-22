using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M37.4 — three things describe one subsystem, and the class is never rewritten to make
    /// them agree; the disagreement is a finding naming both sides.
    /// </summary>
    [TestFixture]
    public sealed class SubsystemDriftTests
    {
        private const string k_ClockDef = "Assets/DrawToPlayExamples/Demo/M21/Subsystems/ClockService.asset";
        private const string k_ClockSketch = "Assets/DrawToPlayExamples/Demo/M21/Subsystems/ClockSketch.asset";

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
        public void TheClockAgreesWithItself()
        {
            var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(k_ClockDef);
            var sketch = AssetDatabase.LoadAssetAtPath<SubsystemSketch>(k_ClockSketch);
            Assert.That(def, Is.Not.Null);
            Assert.That(sketch, Is.Not.Null);
            List<SketchFinding> drift = SubsystemDrift.Find(def, sketch);
            Assert.That(drift, Is.Empty, string.Join("\n", drift));
        }

        [Test]
        public void ARenamedAction_IsAFindingNamingBothSides()
        {
            var def = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ServiceDef>(k_ClockDef));
            m_Junk.Add(def);
            // SOMEBODY RENAMED THE DEF'S ACTION (or the class's const) — the request would be
            // refused at the door, and this says so before anyone plays.
            def.requests[0].action = "adjust";
            List<SketchFinding> drift = SubsystemDrift.Find(def, null);
            SketchFinding refused = drift.Find(f => f.blocks && f.message.Contains("'clock.set' is served by action 'adjust'"));
            Assert.That(refused, Is.Not.Null, string.Join("\n", drift));
            Assert.That(refused.message, Does.Contain("ClockService does not declare"));
            Assert.That(drift.Exists(f => !f.blocks && f.message.Contains("declares action 'set', and no row")),
                Is.True, "and the class's own 'set' is now a verb nobody can ask for");
        }

        [Test]
        public void ARowDeletedFromTheDef_IsAFindingThatSaysRegenerate()
        {
            var def = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ServiceDef>(k_ClockDef));
            m_Junk.Add(def);
            var sketch = AssetDatabase.LoadAssetAtPath<SubsystemSketch>(k_ClockSketch);
            def.announcements.Clear();
            List<SketchFinding> drift = SubsystemDrift.Find(def, sketch);
            Assert.That(drift.Exists(f => f.message.Contains("announcement 'clock.dawn' is sketched but not on the def — Regenerate def")),
                Is.True, string.Join("\n", drift));

            // AND A KNOB THE CLASS LOST: the def tunes something no longer declared.
            def.settings.values.Add(new ServiceSettingValue { name = "minutesPerDay", floatValue = 2f });
            drift = SubsystemDrift.Find(def, sketch);
            Assert.That(drift.Exists(f => f.blocks && f.message.Contains("tunes 'minutesPerDay', which ClockService no longer declares")),
                Is.True);
        }
    }
}

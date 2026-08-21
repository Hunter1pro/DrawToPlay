using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A service with every kind of knob, declared the way a real one declares them.
    /// Lives in this file because a test type must.</summary>
    internal sealed class TunedService : StateTreeService
    {
        public enum Mode { Gentle, Fierce }

        [ServiceSetting(2.4f, "a reach")]
        public float reach;

        [ServiceSetting(256, "a capacity")]
        public int capacity;

        [ServiceSetting(false, "a switch")]
        public bool loud;

        [ServiceSetting("", "what a station is")]
        [WorldTag("World")]
        public string stationTag;

        [ServiceSetting(Mode.Gentle, "how it behaves")]
        public Mode mode;

        /// <summary>What the constructor BODY saw — the whole point of where settings land.</summary>
        public readonly float reachAtConstruction;

        public TunedService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
            reachAtConstruction = reach;
        }
    }

    /// <summary>
    /// M36.1 — a subsystem is tuned on its def.
    ///
    /// The class declares the knobs and their defaults; the def stores only what differs; the
    /// base constructor writes defaults then overrides, so a derived constructor body sees the
    /// final numbers. A row naming a knob the class does not declare is refused out loud — the
    /// placement-attribute rule, one rung up.
    /// </summary>
    [TestFixture]
    public sealed class ServiceSettingsTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeContextHost m_Scope;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Scope") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Scope = go.AddComponent<StateTreeContextHost>();
            m_Scope.kind = StateTreeContextKind.Root;
            m_Scope.autoStart = false;
            m_Scope.Register();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Scope != null)
                m_Scope.Unregister();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void TheClassDeclaresTheKnobs_WithTheirDefaultsAndWhichAreTags()
        {
            var declared = ServiceSettings.DeclaredOn(typeof(TunedService));
            var names = new List<string>();
            for (int i = 0; i < declared.Count; i++)
                names.Add(declared[i].name);
            Assert.That(names, Is.EquivalentTo(new[]
                { "reach", "capacity", "loud", "stationTag", "mode" }));

            Assert.That(ServiceSettings.Find(typeof(TunedService), "reach").defaultValue,
                Is.EqualTo(2.4f), "the default is on the attribute, readable without a scope");
            Assert.That(ServiceSettings.Find(typeof(TunedService), "stationTag").isTag, Is.True,
                "a string marked [WorldTag] is a PICKED setting");
            Assert.That(ServiceSettings.Find(typeof(TunedService), "reach").isTag, Is.False);
        }

        [Test]
        public void NoDef_MeansEveryDefault_AndTheConstructorBodySeesThem()
        {
            var service = new TunedService(m_Scope, null);
            Assert.That(service.reach, Is.EqualTo(2.4f));
            Assert.That(service.capacity, Is.EqualTo(256));
            Assert.That(service.loud, Is.False);
            Assert.That(service.stationTag, Is.EqualTo(""));
            Assert.That(service.mode, Is.EqualTo(TunedService.Mode.Gentle));
            Assert.That(service.reachAtConstruction, Is.EqualTo(2.4f),
                "the defaults are written before the derived constructor body runs");
        }

        [Test]
        public void TheDefOverridesOnlyWhatItStores_AndTheBodySeesTheFinalNumber()
        {
            ServiceDef def = Def();
            def.settings.values.Add(new ServiceSettingValue { name = "reach", floatValue = 6f });
            def.settings.values.Add(new ServiceSettingValue
            {
                name = "stationTag", stringValue = "station", entryId = "tag.station"
            });
            def.settings.values.Add(new ServiceSettingValue { name = "mode", stringValue = "Fierce" });
            def.settings.values.Add(new ServiceSettingValue { name = "loud", floatValue = 1f });

            var service = new TunedService(m_Scope, def);
            Assert.That(service.reach, Is.EqualTo(6f));
            Assert.That(service.reachAtConstruction, Is.EqualTo(6f),
                "the override landed BEFORE the derived constructor body, which is the whole point");
            Assert.That(service.stationTag, Is.EqualTo("station"));
            Assert.That(service.mode, Is.EqualTo(TunedService.Mode.Fierce));
            Assert.That(service.loud, Is.True);
            Assert.That(service.capacity, Is.EqualTo(256), "untouched follows the class default");
        }

        [Test]
        public void AKnobTheClassDoesNotDeclare_IsRefusedWithItsNameAndTheVocabulary()
        {
            ServiceDef def = Def();
            def.settings.values.Add(new ServiceSettingValue { name = "reahc", floatValue = 6f });

            LogAssert.Expect(LogType.Error, new Regex("sets 'reahc', which TunedService does not "
                + "declare.*'reach'.*refused"));
            var service = new TunedService(m_Scope, def);
            Assert.That(service.reach, Is.EqualTo(2.4f), "the typo changed nothing");
        }

        [Test]
        public void AValueThatIsNotTheKnobsType_IsRefusedToo()
        {
            ServiceDef def = Def();
            def.settings.values.Add(new ServiceSettingValue { name = "mode", stringValue = "Angry" });

            LogAssert.Expect(LogType.Error, new Regex("sets 'mode' to 'Angry', which is not a Mode"));
            var service = new TunedService(m_Scope, def);
            Assert.That(service.mode, Is.EqualTo(TunedService.Mode.Gentle));
        }

        [Test]
        public void TheBenchsTagIsNoLongerWrittenInCode()
        {
            // The first real knob to move: CraftService used to say stationTag = "station" in
            // C#, where the map could not see it and a rename could not reach it.
            ServiceSettings.Declared knob = ServiceSettings.Find(typeof(CraftService),
                nameof(CraftService.stationTag));
            Assert.That(knob, Is.Not.Null);
            Assert.That(knob.isTag, Is.True);
            Assert.That(knob.defaultValue, Is.EqualTo(""), "no default: the def picks it");
            Assert.That(ServiceSettings.Find(typeof(CraftService),
                nameof(CraftService.benchRange)).defaultValue, Is.EqualTo(2.4f));
        }


        [Test]
        public void ThePanelOffersEveryDeclaredKnob_WithTheClassDefaultAsTheFallback()
        {
            ServiceDef def = Def();
            List<DeclaredOption> offered = DeclaredOptions.OfService(def);

            var names = new List<string>();
            for (int i = 0; i < offered.Count; i++)
                names.Add(offered[i].name);
            Assert.That(names, Is.EqualTo(new[] { "reach", "capacity", "loud", "stationTag", "mode" }),
                "every knob, in declaration order, with nothing overridden yet");

            DeclaredOption reach = offered[0];
            Assert.That(reach.kind, Is.EqualTo(DeclaredOptionKind.Float));
            Assert.That(reach.fallback, Is.EqualTo(2.4f), "the default is what it shows dimmed");

            DeclaredOption mode = offered[4];
            Assert.That(mode.kind, Is.EqualTo(DeclaredOptionKind.Enum));
            Assert.That(mode.enumType, Is.EqualTo(typeof(TunedService.Mode)));
            Assert.That(mode.fallback, Is.EqualTo(TunedService.Mode.Gentle));

            DeclaredOption tag = offered[3];
            Assert.That(tag.kind, Is.EqualTo(DeclaredOptionKind.Tag));
            Assert.That(tag.fallback, Is.Null,
                "a tag with no default has nothing honest to show — the panel says 'pick one'");
            Assert.That(tag.tagOffers, Is.Not.Null, "and offers what the def declares");

            // A def that names no type offers nothing, and says so instead of guessing.
            def.serviceTypeName = "";
            Assert.That(DeclaredOptions.OfService(def), Is.Empty);
        }

        [Test]
        public void TheSharedPanel_ReadsADefsRowsThroughTheSettingShape()
        {
            ServiceDef def = Def();
            def.settings.values.Add(new ServiceSettingValue { name = "reach", floatValue = 6f });
            def.settings.values.Add(new ServiceSettingValue { name = "gone", floatValue = 1f });

            var so = new SerializedObject(def);
            SerializedProperty rows = so.FindProperty("settings.values");
            List<DeclaredOption> offered = DeclaredOptions.OfService(def);

            Assert.That(DeclaredOptionsPanel.Strays(rows, offered,
                    DeclaredOptionRowShape.ServiceSetting), Is.EquivalentTo(new[] { 1 }),
                "'gone' names no knob the class declares — the same stray a placement shows");
            Assert.That(DeclaredOptionsPanel.Height(offered, rows,
                    DeclaredOptionRowShape.ServiceSetting),
                Is.GreaterThan(DeclaredOptionsPanel.Height(offered, rows,
                    DeclaredOptionRowShape.ServiceSetting) - 1f),
                "and the stray costs a line");
        }

        private ServiceDef Def()
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "tuned";
            def.serviceName = "tuned";
            def.serviceTypeName = typeof(TunedService).FullName;
            m_Junk.Add(def);
            return def;
        }
    }
}

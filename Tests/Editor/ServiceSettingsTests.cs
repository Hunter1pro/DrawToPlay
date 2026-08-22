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
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();
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
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
            m_Hosts.Clear();
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


        [Test]
        public void OneDef_TwoScopes_TwoReaches_AndEachNumberKnowsWhereItCameFrom()
        {
            ServiceDef def = Def();
            def.settings.values.Add(new ServiceSettingValue { name = "reach", floatValue = 6f });

            // THE YARD takes the def as it is; THE RIDGE tunes this one install.
            StateTreeServiceInstaller yard = Installer("Yard");
            StateTreeServiceInstaller ridge = Installer("Ridge");
            var tuned = new ServiceInstall(def);
            tuned.settings.values.Add(new ServiceSettingValue { name = "reach", floatValue = 9f });
            tuned.settings.values.Add(new ServiceSettingValue { name = "capacity", floatValue = 8f });

            var inYard = (TunedService)yard.Install(def).service;
            var onRidge = (TunedService)ridge.Install(tuned).service;

            Assert.That(inYard.reach, Is.EqualTo(6f), "the def's tuning");
            Assert.That(onRidge.reach, Is.EqualTo(9f), "this install's tuning, on top");
            Assert.That(onRidge.reachAtConstruction, Is.EqualTo(9f),
                "and the derived constructor body saw the install's number — the layer travels "
                + "through the scope before the body runs");
            Assert.That(onRidge.capacity, Is.EqualTo(8));
            Assert.That(inYard.capacity, Is.EqualTo(256), "an install does not leak into another");

            Assert.That(inYard.settingSources["reach"], Is.EqualTo(ServiceSettingSource.Def));
            Assert.That(inYard.settingSources["capacity"], Is.EqualTo(ServiceSettingSource.Code));
            Assert.That(onRidge.settingSources["reach"], Is.EqualTo(ServiceSettingSource.Install));
            Assert.That(onRidge.settingSources["capacity"], Is.EqualTo(ServiceSettingSource.Install));
            Assert.That(onRidge.settingSources["loud"], Is.EqualTo(ServiceSettingSource.Code));

            // THE LAYER IS GONE the moment construction ends: a service built by hand
            // afterwards on the same scope sees nothing of the ridge's row.
            var byHand = new TunedService(ridge.scope, def);
            Assert.That(byHand.reach, Is.EqualTo(6f));

            // AND A REINSTALL KEEPS THE ROW — the tuning belongs to the scope, not to the instance.
            var again = (TunedService)ridge.Reinstall(def).service;
            Assert.That(again.reach, Is.EqualTo(9f));
        }


        /// <summary>A consumer that asks for the CAPABILITY — what every count-only atom does now.</summary>
        private sealed class Counter
        {
            [InjectService] public IBag bag;
        }

        /// <summary>A BAG THAT ONLY COUNTS — the second implementation of <see cref="IBag"/>,
        /// twelve lines, living where the one test that needs it lives (M39 retired the
        /// example-assembly stub nobody installed). Point a def at it and every consumer that
        /// asked for the CAPABILITY keeps working.</summary>
        public sealed class CountingBag : StateTreeService, IBag
        {
            public CountingBag(StateTreeContextHost scope, ServiceDef definition) : base(scope, definition) { }
            private readonly System.Collections.Generic.Dictionary<string, int> m_Counts =
                new System.Collections.Generic.Dictionary<string, int>();
            public ItemDef Row(string itemName) => definition?.registry is ItemRegistry r ? r.FindByName(itemName) as ItemDef : null;
            public int Count(string itemName) => m_Counts.TryGetValue(itemName ?? "", out int held) ? held : 0;
            public bool Has(string itemName, int count = 1) => Count(itemName) >= count;
            public int Add(string itemName, int count = 1)
            {
                m_Counts[itemName] = Count(itemName) + count;
                return m_Counts[itemName];
            }
            public bool Remove(string itemName, int count = 1)
            {
                if (Count(itemName) < count)
                    return false;
                m_Counts[itemName] = Count(itemName) - count;
                return true;
            }
        }

        [Test]
        public void TheSwap_ADefNamesAnotherClass_AndACapabilityConsumerCannotTell()
        {
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            m_Junk.Add(items);
            items.entries.Add(new ItemDef { id = "item.wood", name = "wood", displayName = "Wood" });

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "inventory";
            def.serviceName = "inventory";
            def.registry = items;
            m_Junk.Add(def);

            // THE REAL BAG, then the stub — the same def, one field changed.
            foreach (System.Type implementation in new[]
                { typeof(InventoryService), typeof(CountingBag) })
            {
                def.serviceTypeName = implementation.FullName;
                StateTreeServiceInstaller installer = Installer("Scope " + implementation.Name);
                StateTreeSubsystem built = installer.Install(def);
                Assert.That(built.service, Is.InstanceOf(implementation));

                var consumer = new Counter();
                StateTreeServiceInjector.Inject(consumer, installer.scope.gameObject);
                Assert.That(consumer.bag, Is.SameAs(built.service),
                    implementation.Name + " was provided under IBag, so a consumer asking for the "
                    + "capability got it without naming the class");

                consumer.bag.Add("wood", 3);
                Assert.That(consumer.bag.Count("wood"), Is.EqualTo(3));
                Assert.That(consumer.bag.Remove("wood", 5), Is.False, "all-or-nothing");
                Assert.That(consumer.bag.Has("wood", 3), Is.True);

                // AND TAKING IT OUT forgets every name it was provided under.
                installer.Uninstall(def);
                Assert.That(installer.scope.GetService<IBag>(), Is.Null);
                Assert.That(installer.scope.GetService(implementation), Is.Null);
            }
        }

        private StateTreeServiceInstaller Installer(string scopeName)
        {
            var go = new GameObject(scopeName) { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            StateTreeContextHost host = go.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Level;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            StateTreeServiceInstaller installer = go.AddComponent<StateTreeServiceInstaller>();
            installer.scope = host;
            return installer;
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

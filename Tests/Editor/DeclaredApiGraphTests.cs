using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A service that announces on demand — the clock's shape, without a day.</summary>
    internal sealed class CrierService : StateTreeService
    {
        public const string Key = "crier.called";

        public CrierService(StateTreeContextHost scope, ServiceDef definition) : base(scope, definition)
        {
        }

        public void Cry(object payload)
        {
            Announce(Key, payload);
        }
    }

    /// <summary>
    /// M38.1 — the graph as the project's declared API.
    ///
    /// The graph assembly is firewalled from the tests (Graph Toolkit is experimental), so what
    /// is pinned here is the RUNTIME half the nodes bake to — the announcement serial and the
    /// condition that fires once per step of it — and the baked programs of the two graphs the
    /// waystation authors by picking, read back through the runtime types they bake into.
    /// </summary>
    [TestFixture]
    public sealed class DeclaredApiGraphTests
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
        public void AnAnnouncementFiresItsListenerOnce_PerAnnouncement_AndLeavesThePayload()
        {
            var crier = new CrierService(m_Root, null);
            var condition = ScriptableObject.CreateInstance<AnnouncementCondition>();
            m_Junk.Add(condition);
            condition.key = CrierService.Key;
            condition.scope = StateTreeContextKind.Root;
            var context = new StateTreeContext(m_Root.gameObject);

            Assert.That(condition.Evaluate(context), Is.False, "nothing announced yet");

            crier.Cry(6.0f);
            Assert.That(m_Root.Context.blackboard[CrierService.Key], Is.EqualTo(6.0f),
                "the payload stays on the key for whoever reads it");
            Assert.That(m_Root.Context.blackboard[StateTreeService.AnnouncementSerialKey(CrierService.Key)],
                Is.EqualTo(1), "and the announcement has a number beside it");

            // A LISTENER ALIVE BEFORE THE FIRST ANNOUNCEMENT hears the first one.
            Assert.That(condition.Evaluate(context), Is.True, "the first dawn is heard");
            Assert.That(condition.Evaluate(context), Is.False, "and not again while it stands");
            crier.Cry(6.1f);
            Assert.That(condition.Evaluate(context), Is.True, "once, when the serial moves");
            Assert.That(condition.Evaluate(context), Is.False, "and not again while it stands");
            Assert.That(condition.Evaluate(context), Is.False);

            crier.Cry(6.1f);
            Assert.That(condition.Evaluate(context), Is.True,
                "the same payload twice is two announcements — the serial says so, the payload could not");

            // A LISTENER THAT STARTS AFTER THREE ANNOUNCEMENTS has not just heard one: its first
            // look adopts. And two listeners, neither consuming, both hear the next.
            var other = ScriptableObject.CreateInstance<AnnouncementCondition>();
            m_Junk.Add(other);
            other.key = CrierService.Key;
            Assert.That(other.Evaluate(context), Is.False, "adopted, not fired");
            crier.Cry(7f);
            Assert.That(condition.Evaluate(context), Is.True);
            Assert.That(other.Evaluate(context), Is.True);
        }

        [Test]
        public void TheKeepersGift_BakedFromAnAsk_IsTheBagsDeclaredRequest()
        {
            Object[] parts = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/DrawToPlayExamples/Demo/M21/Dialogs/M21Dialog_Keeper.taskgraph");
            RequestTask gift = null;
            foreach (Object part in parts)
            {
                if (part is RequestTask request && request.key == "bag.add")
                    gift = request;
            }
            Assert.That(gift, Is.Not.Null, "the Ask node baked to the ordinary RequestTask");
            Assert.That(gift.value, Is.EqualTo("medkit"), "the value is a row of the catalog bag.add names");

            // AND THE BAG SERVES IT, typed: a row the catalog has lands; one it lacks is refused.
            var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21InventoryService.asset");
            ServiceRequest row = def.requests.Find(r => r.key == "bag.add");
            Assert.That(row, Is.Not.Null);
            Assert.That(row.action, Is.EqualTo(InventoryService.AddAction));
            Assert.That(row.namesRowOf, Is.Not.Null, "the value is typed by the item catalog");
            Assert.That(row.internalOnly, Is.False, "a gift is something others may ask for");
        }

        [Test]
        public void TheDawnReaction_BakedByPicking_HoldsNoTypedString()
        {
            Object[] parts = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/DrawToPlayExamples/Demo/M21/Reactions/M21Reaction_Dawn.taskgraph");
            AnnouncementCondition when = null;
            UiCallTask say = null;
            GraphTaskAsset program = null;
            foreach (Object part in parts)
            {
                when ??= part as AnnouncementCondition;
                say ??= part as UiCallTask;
                program ??= part as GraphTaskAsset;
            }
            Assert.That(when, Is.Not.Null, "When Announced baked to the once-per-announcement condition");
            Assert.That(when.key, Is.EqualTo("clock.dawn"), "the clock's declared announcement");
            Assert.That(say, Is.Not.Null, "Say To Screen baked to the ordinary UiCallTask");
            Assert.That(say.ui.entryName, Is.EqualTo("hud"));
            Assert.That(say.verb, Is.EqualTo("say"), "a verb the HUD's skin declares");
            Assert.That(program.inputBindings.Count, Is.EqualTo(1),
                "the hour reaches the verb's argument by a WIRE from Announced Payload, not a typed key");
        }
    }
}

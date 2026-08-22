using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M38.1b — the declared API in the state tree: the picker's presets make configured
    /// library tasks and conditions, the ops take them as they are, and the waystation's trees
    /// carry them as assets.
    /// </summary>
    [TestFixture]
    public sealed class DeclaredApiPresetTests
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
        public void ThePresets_AreTheDefsDeclarations_AsConfiguredInstances()
        {
            List<DeclaredApiPresets.Preset> tasks = DeclaredApiPresets.For(typeof(StateTreeTaskAsset));
            List<DeclaredApiPresets.Preset> conditions = DeclaredApiPresets.For(typeof(StateTreeConditionAsset));

            DeclaredApiPresets.Preset gift = tasks.Find(p => p.displayName == "Ask · inventory · bag.add");
            Assert.That(gift, Is.Not.Null, "the bag's public verb is an Ask row");
            Assert.That(gift.category, Is.EqualTo("Subsystems/Ask/inventory"));
            Assert.That(gift.description, Does.Contain("a row of M21Items"), "typed by the catalog");
            var made = gift.make() as RequestTask;
            m_Junk.Add(made);
            Assert.That(made, Is.Not.Null);
            Assert.That(made.key, Is.EqualTo("bag.add"), "the key is set — nothing typed");

            Assert.That(tasks.Exists(p => p.displayName == "Ask · inventory · bag.use"), Is.False,
                "an internal-only request is the bag's own button, not an offer");

            DeclaredApiPresets.Preset pulse = tasks.Find(p => p.displayName == "Say To · hud · pulse");
            Assert.That(pulse, Is.Not.Null, "a verb the HUD's skin declares");
            var call = pulse.make() as UiCallTask;
            m_Junk.Add(call);
            Assert.That(call.ui.entryName, Is.EqualTo("hud"));
            Assert.That(call.ui.entryId, Is.EqualTo("ui.hud"), "both halves — the id a rename follows");
            Assert.That(call.verb, Is.EqualTo("pulse"));

            DeclaredApiPresets.Preset dawn = conditions.Find(p => p.displayName == "When · clock · clock.dawn");
            Assert.That(dawn, Is.Not.Null);
            var when = dawn.make() as AnnouncementCondition;
            m_Junk.Add(when);
            Assert.That(when.key, Is.EqualTo("clock.dawn"));
            Assert.That(when.scope, Is.EqualTo(StateTreeContextKind.Root), "the clock's own scope");

            Assert.That(DeclaredApiPresets.For(typeof(StateTreeConditionAsset))
                .Exists(p => p.displayName.StartsWith("Ask")), Is.False,
                "a condition picker is offered conditions, not tasks");
        }

        private const string k_TempTree = "Assets/DrawToPlay/Tests/Editor/_PresetTempTree.asset";

        [Test]
        public void TheOps_TakeAConfiguredInstance_AsTheStatesOwn()
        {
            // The ops add sub-assets, so the tree has to be a real file for the test's duration.
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            AssetDatabase.CreateAsset(tree, k_TempTree);
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = "setup";
            AssetDatabase.AddObjectToAsset(node, tree);
            tree.root = node;
            try
            {
                OpsTakeInstances(tree, node);
            }
            finally
            {
                AssetDatabase.DeleteAsset(k_TempTree);
            }
        }

        private void OpsTakeInstances(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            RequestTask ask = DeclaredApiPresets.Ask("clock.set", "12");
            StateTreeTaskAsset added = StateTreeEditorOps.AddTask(tree, node, ask, "test");
            Assert.That(added, Is.SameAs(ask), "the instance itself, not a blank copy of its type");
            Assert.That(node.tasks, Does.Contain(ask));
            Assert.That(ask.key, Is.EqualTo("clock.set"), "and it kept what the preset set");

            var transition = new StateTreeTransition { targetNodeId = "dawned" };
            node.transitions.Add(transition);
            AnnouncementCondition when = DeclaredApiPresets.When("clock.dawn", StateTreeContextKind.Root);
            StateTreeConditionAsset set = StateTreeEditorOps.SetTransitionCondition(tree, node, transition, when, "test");
            Assert.That(set, Is.SameAs(when));
            Assert.That(transition.condition, Is.SameAs(when));
        }

        [Test]
        public void TheWaystationsTrees_CarryThePickedPresets()
        {
            Object[] player = AssetDatabase.LoadAllAssetsAtPath(
                AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:StateTreeAsset M21PlayerTree")[0]));
            int whens = 0, pulses = 0;
            foreach (Object part in player)
            {
                if (part is AnnouncementCondition when && when.key == "clock.dawn") whens++;
                if (part is UiCallTask say && say.verb == "pulse" && say.ui.entryName == "hud") pulses++;
            }
            Assert.That(whens, Is.EqualTo(2), "one edge per resting state, each with its own 'last heard'");
            Assert.That(pulses, Is.EqualTo(1), "the dawn state's beat");

            Object[] session = AssetDatabase.LoadAllAssetsAtPath(
                AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:StateTreeAsset M21SessionTree")[0]));
            RequestTask setClock = null;
            foreach (Object part in session)
                if (part is RequestTask r && r.key == "clock.set") setClock = r;
            Assert.That(setClock, Is.Not.Null, "the session sets the clock by a picked Ask");
            Assert.That(setClock.value, Is.EqualTo("12"));
        }
    }
}

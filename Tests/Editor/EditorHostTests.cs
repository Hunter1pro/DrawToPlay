using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// THE HOST RULE (CLAUDE.md, "Editor UI"): an inspector that hosts serialized properties is
    /// a UI Toolkit host, because a UI Toolkit drawer inside an IMGUI editor draws the words
    /// "No GUI Implemented" — which every kind def's tags did for three milestones.
    ///
    /// This builds each host's inspector and checks that the properties carrying UI Toolkit
    /// drawers are reached by a PropertyField — the one element that asks a drawer for its
    /// CreatePropertyGUI. An IMGUI host has no PropertyFields at all (it returns null from
    /// CreateInspectorGUI), and a property drawn inside an IMGUIContainer is the defect.
    /// </summary>
    [TestFixture]
    public sealed class EditorHostTests
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
        public void TheDefInspector_LetsTheTagDrawerDraw()
        {
            var kind = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Kind_Player.asset");
            Assert.That(kind.body.tags.Count, Is.GreaterThan(0), "a kind def wears tags");

            VisualElement root = Host(kind);
            Assert.That(Field(root, "body"), Is.Not.Null,
                "the body — whose tags are a UI Toolkit drawer — is a PropertyField, not IMGUI");
            Assert.That(Field(root, "declares"), Is.Null,
                "M41.4: 'declares' is drawn under Reads, beside the catalogs derived from picks");
        }

        /// <summary>M41.2: the inspector is four verbs, shown only where they apply. A bench
        /// hosts its body PropertyField hidden (it builds nothing) and its two IMGUI sections; a
        /// Kind shows the body; an infrastructure def builds the same host and says one
        /// sentence through it. The host shape is what can be pinned here — the IMGUI text is
        /// painted, not queried.</summary>
        [Test]
        public void TheDefInspector_IsFourVerbs_OnlyWhereTheyApply()
        {
            var bench = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21CraftService.asset");
            var ui = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21UiService.asset");
            var kind = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Kind_Station.asset");
            var shrine = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Kind_Shrine.asset");

            // ONE ASSET TYPE, DIFFERENT SECTIONS (M41.4): a bench, an infrastructure service, a
            // kind and a class-less kind with an ask are the same editor with the same shape.
            foreach (ServiceDef def in new[] { bench, ui, kind, shrine })
            {
                VisualElement root = Host(def);
                Assert.That(Field(root, "body"), Is.Not.Null, def.name + ": the body is a PropertyField");
                Assert.That(root.Query<IMGUIContainer>().ToList().Count, Is.EqualTo(3),
                    def.name + ": the sentence, Asks/Announces/Shows above the body, Is and the rest below");
            }

            Assert.That(bench.body.IsThing, Is.False, "a bench builds nothing…");
            Assert.That(kind.body.IsThing, Is.True, "…and a station is a body");
            Assert.That(bench.requests.Exists(r => r.action == CraftService.CraftAction), Is.True,
                "the bench's one Ask is the class's one action, offered");
            Assert.That(ui.requests.Count == 0 && ui.spawns.Count == 0 && !ui.body.IsThing, Is.True,
                "the UI service offers nothing to a flow — the inspector says so in a sentence");
        }

        [Test]
        public void TheSketchInspector_LetsTheEntryRefDrawerDraw()
        {
            var sketch = ScriptableObject.CreateInstance<SubsystemSketch>();
            m_Junk.Add(sketch);
            sketch.serviceName = "sundial";
            sketch.attributes.Add(new StateTreeEntryRef<AttributeDef> { entryName = "health" });

            VisualElement root = Host(sketch);
            Assert.That(root.Query<HelpBox>().ToList().Count, Is.GreaterThanOrEqualTo(2),
                "findings above, drift below");
            foreach (string picked in new[] { "attributes", "spawns" })
            {
                Assert.That(Field(root, picked), Is.Not.Null,
                    "'" + picked + "' is a PropertyField, so its ⛃ drawer is what draws it");
            }
        }

        [Test]
        public void TheWaterInspector_LetsTheTagDrawerDraw()
        {
            var go = new GameObject("Water") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            WaterVolumeBehaviour water = go.AddComponent<WaterVolumeBehaviour>();

            VisualElement root = Host(water);
            Assert.That(Field(root, "waterTag"), Is.Not.Null, "the water tag is reached by its drawer");
            Assert.That(root.Query<IMGUIContainer>().ToList(), Is.Empty,
                "and nothing in this inspector is IMGUI at all");
        }

        /// <summary>The inspector's tree, as the editor builds it. Null is the IMGUI answer.</summary>
        private VisualElement Host(Object target)
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            m_Junk.Add(editor);
            VisualElement root = editor.CreateInspectorGUI();
            Assert.That(root, Is.Not.Null, editor.GetType().Name + " is not a UI Toolkit host");
            return root;
        }

        /// <summary>The PropertyField bound to a top-level property, or null when the host draws
        /// it some other way.</summary>
        private static PropertyField Field(VisualElement root, string propertyName)
        {
            return root.Query<PropertyField>().Where(f => f.bindingPath == propertyName).First();
        }
    }
}

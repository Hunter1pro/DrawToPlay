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
            Assert.That(Field(root, "declares"), Is.Not.Null);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A game's section on a manifest row: the arguments it owns leave the row's Parameters list, the others stay,
    /// it is offered only on the rows it says, and its warnings join the manifest's.
    /// </summary>
    public sealed class LevelRowSectionTests
    {
        private const string k_Owned = "test.owned";
        private const string k_Kept = "test.kept";

        private sealed class Section : ILevelRowSection
        {
            public bool Offers(LevelObjectRegistry manifest, LevelObjectDef row) => row.kind.entryName == "find";

            public IReadOnlyCollection<string> Owns(LevelObjectDef row) => new[] { k_Owned };

            public VisualElement Build(LevelObjectRegistry manifest, LevelObjectDef row, Action changed) =>
                new Label("section of " + row.name);

            public IEnumerable<string> Warnings(LevelObjectRegistry manifest) =>
                manifest.entries.Where(r => r.kind.entryName == "find").Select(r => r.name + " is a find");
        }

        private readonly Section m_Section = new Section();
        private StateTreeAsset m_Tree;
        private LevelObjectRegistry m_Manifest;

        [SetUp]
        public void SetUp()
        {
            m_Tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            m_Tree.parameters.Add(new GraphTaskParameter { name = "owned", id = k_Owned, kind = GraphTaskParameterKind.Float });
            m_Tree.parameters.Add(new GraphTaskParameter { name = "kept", id = k_Kept, kind = GraphTaskParameterKind.Float });
            m_Manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            m_Manifest.entries.Add(Row("a find", "find"));
            m_Manifest.entries.Add(Row("a door", "door"));
            LevelRowSections.Register(m_Section);
        }

        [TearDown]
        public void TearDown()
        {
            LevelRowSections.Unregister(m_Section);
            Object.DestroyImmediate(m_Tree);
            Object.DestroyImmediate(m_Manifest);
        }

        [Test]
        public void OfferedOnlyOnTheRowsItSays()
        {
            CollectionAssert.Contains(LevelRowSections.Offered(m_Manifest, m_Manifest.entries[0]), m_Section);
            CollectionAssert.DoesNotContain(LevelRowSections.Offered(m_Manifest, m_Manifest.entries[1]), m_Section);
        }

        [Test]
        public void TheOwnedArgument_LeavesTheParametersList_OnlyOnItsRows()
        {
            CollectionAssert.AreEquivalent(new[] { "Kept" }, Labels(Draw(0)), "the section draws the owned one itself");
            CollectionAssert.AreEquivalent(new[] { "Owned", "Kept" }, Labels(Draw(1)), "a row it does not offer on keeps both");
        }

        [Test]
        public void ItsWarnings_JoinTheManifests()
        {
            CollectionAssert.Contains(LevelRowSections.Warnings(m_Manifest), "a find is a find");
        }

        private LevelObjectDef Row(string name, string kind)
        {
            var row = new LevelObjectDef
            {
                id = "test." + name, name = name, tree = m_Tree,
                kind = new StateTreeEntryRef<LevelObjectKindDef> { entryId = "kind." + kind, entryName = kind }
            };
            row.parameters.values.Add(new GraphTaskParameterOverride { name = "owned", id = k_Owned, enabled = true, floatValue = 1f });
            return row;
        }

        private VisualElement Draw(int index)
        {
            var serialized = new SerializedObject(m_Manifest);
            SerializedProperty property = serialized.FindProperty($"entries.Array.data[{index}].parameters");
            FieldInfo field = typeof(LevelObjectDef).GetField(nameof(LevelObjectDef.parameters));
            Type drawerType = typeof(StateTreePickField).Assembly.GetType("PowerOfFire.DrawToPlay.Editor.GraphTaskParametersDrawer");
            var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType, true);
            typeof(PropertyDrawer).GetField("m_FieldInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(drawer, field);
            return drawer.CreatePropertyGUI(property);
        }

        private static List<string> Labels(VisualElement root) =>
            root.Query<Label>().ToList().Select(l => l.text).Where(t => t == "Owned" || t == "Kept").ToList();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A manifest row's parameters, picked: a row-typed one offers the catalog the row's TREE declares (the
    /// manifest declares only its tags), and a placement-in-level one offers the placements of the level its
    /// sibling names — the way a portal's arrival is a place in its destination.
    /// </summary>
    public sealed class LevelManifestRowPickTests
    {
        private const string k_Destination = "test.destination";
        private const string k_Arrival = "test.arrival";
        private const string k_Interaction = "test.interaction";

        private readonly List<Object> m_Junk = new List<Object>();
        private LevelRegistry m_Levels;
        private PickStubRegistry m_Interactions;
        private StateTreeAsset m_Tree;
        private LevelObjectRegistry m_Hall;

        [SetUp]
        public void SetUp()
        {
            StateTreePickMenu.headless = true;
            m_Interactions = Junk(ScriptableObject.CreateInstance<PickStubRegistry>());
            m_Interactions.name = "Interactions";
            m_Interactions.entries.Add(new PickStubRow { id = "interaction.down", name = "down" });
            m_Interactions.entries.Add(new PickStubRow { id = "interaction.up", name = "up" });

            m_Hall = Manifest("HallObjects",
                Placement("hall.portal.stairs", "Down the stairs", "portal"),
                Placement("hall.portal.back", "Back door", "portal"),
                Placement("hall.anchor", "The way in", "anchor"));
            LevelObjectRegistry lobby = Manifest("LobbyObjects",
                Placement("lobby.portal.stairs", "Up the stairs", "portal"),
                Placement("lobby.anchor", "The desk", "anchor"));

            m_Levels = Junk(ScriptableObject.CreateInstance<LevelRegistry>());
            m_Levels.name = "Levels";
            m_Levels.entries.Add(new LevelDef { id = "level.hall", name = "hall", content = Content(m_Hall) });
            m_Levels.entries.Add(new LevelDef { id = "level.lobby", name = "lobby", content = Content(lobby) });

            m_Tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            m_Tree.name = "PortalTree";
            m_Tree.registries.Add(m_Levels);
            m_Tree.registries.Add(m_Interactions);
            m_Tree.parameters.Add(new GraphTaskParameter
            {
                name = "destination", id = k_Destination, kind = GraphTaskParameterKind.String,
                type = StateTreeValueType.RowsOf(m_Levels)
            });
            m_Tree.parameters.Add(new GraphTaskParameter
            {
                name = "arrival", id = k_Arrival, kind = GraphTaskParameterKind.String, placementInLevelOf = k_Destination
            });
            m_Tree.parameters.Add(new GraphTaskParameter
            {
                name = "interaction", id = k_Interaction, kind = GraphTaskParameterKind.String,
                type = StateTreeValueType.RowsOf(m_Interactions)
            });

            LevelObjectDef portal = m_Hall.entries[0];
            portal.tree = m_Tree;
            portal.parameters.values.Add(Value("destination", k_Destination, "lobby", "level.lobby"));
            portal.parameters.values.Add(Value("arrival", k_Arrival, "lobby.portal.stairs", ""));
            portal.parameters.values.Add(Value("interaction", k_Interaction, "down", "interaction.down"));
        }

        [TearDown]
        public void TearDown()
        {
            StateTreePickMenu.headless = false;
            foreach (Object junk in m_Junk)
            {
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
        }

        [Test]
        public void TheManifestAlone_DeclaresNoCatalog_TheRowsTreeDoes()
        {
            StateTreeValueType type = m_Tree.parameters[0].TypeOf();
            var rows = new List<StateTreeRegistryEntry>();
            GraphTaskParameterPicks.Rows(type, new Object[] { m_Hall }, rows);
            Assert.IsEmpty(rows, "the manifest lists its tags, not the catalogs its trees name — the old picker's only owner");

            GraphTaskParameterPicks.Rows(type, new Object[] { m_Tree, m_Hall }, rows);
            CollectionAssert.AreEquivalent(new[] { "hall", "lobby" }, rows.Select(r => r.name));
            StringAssert.Contains("not declared by HallObjects", GraphTaskParameterPicks.WhyEmpty(type, new Object[] { m_Hall }),
                "an empty offer names who would have to declare the catalog");
        }

        [Test]
        public void ARowTypedParameter_OffersTheTreesCatalog_AndResolvesItsValue()
        {
            VisualElement root = DrawPortal();

            StateTreePickField destination = Field(root, "Destination");
            Assert.IsFalse(destination.missing, "'lobby' is a row of the tree's level catalog: " + destination.button.text);
            StringAssert.StartsWith("lobby", destination.button.text);
            StateTreePickField interaction = Field(root, "Interaction");
            Assert.IsFalse(interaction.missing, interaction.button.text);
            Assert.AreEqual(0, root.Query<TextField>().ToList().Count, "no parameter of the row is typed");

            destination.Open();
            CollectionAssert.AreEquivalent(new[] { "hall", "lobby" }, StateTreePickMenu.lastShown.items.Select(i => i.value));
            Assert.AreEqual(1, StateTreePickMenu.lastShown.creates.Count, "+ New in the one catalog the type names");
            StringAssert.Contains("Levels", StateTreePickMenu.lastShown.creates[0].label);
        }

        [Test]
        public void APickedRow_WritesItsNameAndItsId()
        {
            VisualElement root = DrawPortal();
            Field(root, "Interaction").Open();
            Choose(StateTreePickMenu.lastShown, "up");

            GraphTaskParameterOverride written = m_Hall.entries[0].parameters.values.Single(v => v.id == k_Interaction);
            Assert.AreEqual("up", written.stringValue);
            Assert.AreEqual("interaction.up", written.entryId);
        }

        [Test]
        public void TheArrival_ListsTheDestinationsPlacements_ItsOwnKindFirst_AndFollowsADestinationChange()
        {
            VisualElement root = DrawPortal();
            StateTreePickField arrival = Field(root, "Arrival");
            Assert.IsFalse(arrival.missing, arrival.button.text);
            StringAssert.StartsWith("Up the stairs", arrival.button.text, "shown by the placement's name");

            arrival.Open();
            StateTreePickMenu menu = StateTreePickMenu.lastShown;
            CollectionAssert.AreEquivalent(new[] { "lobby.portal.stairs", "lobby.anchor" }, menu.items.Select(i => i.value));
            Assert.IsTrue(menu.items.Single(i => i.value == "lobby.portal.stairs").fits, "a portal arrives at a portal");
            Assert.IsFalse(menu.items.Single(i => i.value == "lobby.anchor").fits, "the rest come after");

            Field(root, "Destination").Open();
            Choose(StateTreePickMenu.lastShown, "hall");

            arrival = Field(root, "Arrival");
            Assert.IsTrue(arrival.missing, "the old arrival is no place in the new destination");
            arrival.Open();
            CollectionAssert.AreEquivalent(new[] { "hall.portal.stairs", "hall.portal.back", "hall.anchor" },
                StateTreePickMenu.lastShown.items.Select(i => i.value));
            Choose(StateTreePickMenu.lastShown, "hall.portal.back");
            Assert.AreEqual("hall.portal.back", m_Hall.entries[0].parameters.values.Single(v => v.id == k_Arrival).stringValue,
                "the placement's id is what is written");
            Assert.IsFalse(Field(root, "Arrival").missing);
        }

        [Test]
        public void TheArrival_WithoutADestination_SaysToPickTheDestinationFirst()
        {
            LevelObjectDef portal = m_Hall.entries[0];
            portal.parameters.values.RemoveAll(v => v.id == k_Destination || v.id == k_Arrival);

            VisualElement root = DrawPortal();
            Field(root, "Arrival").Open();
            Assert.IsEmpty(StateTreePickMenu.lastShown.items);
            Assert.AreEqual("Pick the destination first", StateTreePickMenu.lastShown.empty);
        }

        // ---- helpers ------------------------------------------------------------------------

        private VisualElement DrawPortal()
        {
            var serialized = new SerializedObject(m_Hall);
            SerializedProperty property = serialized.FindProperty("entries.Array.data[0].parameters");
            FieldInfo field = typeof(LevelObjectDef).GetField(nameof(LevelObjectDef.parameters));
            Type drawerType = typeof(StateTreePickField).Assembly.GetType("PowerOfFire.DrawToPlay.Editor.GraphTaskParametersDrawer");
            var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType, true);
            typeof(PropertyDrawer).GetField("m_FieldInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(drawer, field);
            return drawer.CreatePropertyGUI(property);
        }

        /// <summary>The pick on the parameter row labelled <paramref name="label"/>.</summary>
        private static StateTreePickField Field(VisualElement root, string label)
        {
            return root.Query<StateTreePickField>().ToList()
                .First(p => p.parent.Children().OfType<Label>().Any(l => l.text == label));
        }

        private static void Choose(StateTreePickMenu menu, string value)
        {
            StateTreePickDropdown dropdown = menu.Dropdown();
            dropdown.Choose(Leaves(dropdown.Root()).First(l => l.item != null && l.item.value == value));
        }

        private static IEnumerable<StateTreePickDropdown.Leaf> Leaves(AdvancedDropdownItem at)
        {
            foreach (AdvancedDropdownItem child in at.childList)
            {
                if (child is StateTreePickDropdown.Leaf leaf)
                    yield return leaf;
                foreach (StateTreePickDropdown.Leaf below in Leaves(child))
                    yield return below;
            }
        }

        private LevelObjectRegistry Manifest(string name, params LevelObjectDef[] rows)
        {
            var manifest = Junk(ScriptableObject.CreateInstance<LevelObjectRegistry>());
            manifest.name = name;
            manifest.entries.AddRange(rows);
            return manifest;
        }

        private LevelContent Content(LevelObjectRegistry manifest)
        {
            var content = Junk(ScriptableObject.CreateInstance<LevelContent>());
            content.objects = manifest;
            return content;
        }

        private static LevelObjectDef Placement(string id, string name, string kind)
        {
            return new LevelObjectDef
            {
                id = id, name = name,
                kind = new StateTreeEntryRef<LevelObjectKindDef> { entryId = "kind." + kind, entryName = kind }
            };
        }

        private static GraphTaskParameterOverride Value(string name, string id, string text, string entryId)
        {
            return new GraphTaskParameterOverride { name = name, id = id, enabled = true, stringValue = text, entryId = entryId };
        }

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }
    }
}

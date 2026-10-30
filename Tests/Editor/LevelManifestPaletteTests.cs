using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The Level Manifest's palette and grouped list, on throwaway assets: rows land in their kind's
    /// group sorted by name; a kind placed at a point or at the view's centre is one row of that kind
    /// with a fresh standard name, undone in one step; a drop lands on a surface before the ground plane;
    /// the window hosts the palette, the groups and the selected row's fields.
    /// </summary>
    [TestFixture]
    public sealed class LevelManifestPaletteTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private LevelObjectKindRegistry m_Kinds;
        private LevelObjectRegistry m_Manifest;
        private LevelContent m_Level;
        private LevelManifestWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            m_Kinds = Make<LevelObjectKindRegistry>();
            foreach (string kind in new[] { "door", "npc", "find" })
                m_Kinds.entries.Add(new LevelObjectKindDef { id = "kind." + kind, name = kind });

            m_Manifest = Make<LevelObjectRegistry>();
            m_Manifest.plane = LevelGroundPlane.XZ;
            Row("find 10", "find", "prop");
            Row("b door", "door", "door");
            Row("Nadine", "npc", "npc");
            Row("find 2", "find", "prop");
            Row("a door", "door", "door");
            Row("odd", "statue", "");
            Row("loose", "", "");

            m_Level = Make<LevelContent>();
            m_Level.name = "PaletteTest" + System.Guid.NewGuid().ToString("N");
            m_Level.objects = m_Manifest;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string kind in new[] { "door", "npc", "find", "statue", LevelManifestEdits.NoKind })
                EditorPrefs.DeleteKey(LevelManifestWindow.FoldKey(m_Level.name, kind));
            if (m_Window != null)
                Object.DestroyImmediate(m_Window);
            Undo.ClearUndo(m_Manifest);
            foreach (Object junk in m_Junk)
            {
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
        }

        [Test]
        public void Rows_LandInTheirKindsGroup_InTheRegistrysOrder_SortedByName()
        {
            List<LevelManifestEdits.KindGroup> groups =
                LevelManifestEdits.GroupByKind(m_Manifest.entries, m_Kinds);

            CollectionAssert.AreEqual(new[] { "door", "npc", "find", "statue", LevelManifestEdits.NoKind },
                groups.Select(group => group.kind).ToArray());
            CollectionAssert.AreEqual(new[] { "a door", "b door" }, Names(groups[0]));
            CollectionAssert.AreEqual(new[] { "find 2", "find 10" }, Names(groups[2]));
            Assert.AreEqual(m_Manifest.entries.Count, groups.Sum(group => group.rows.Count),
                "every row is listed once");
        }

        [Test]
        public void StandardName_IsLevelKindAndTheFirstFreeNumber()
        {
            Assert.AreEqual("hall.door.1", LevelManifestEdits.StandardName(m_Manifest, "hall", "door"));
            m_Manifest.entries[0].id = "hall.door.1";
            m_Manifest.entries[1].name = "hall.door.2";
            Assert.AreEqual("hall.door.3", LevelManifestEdits.StandardName(m_Manifest, "hall", "door"));
        }

        [Test]
        public void LevelKey_IsTheCatalogRowsName_ElseTheContentWithoutContent()
        {
            Assert.AreEqual("hall", LevelManifestEdits.LevelKey(m_Level, "hall"));
            m_Level.name = "Fire EscapeContent";
            Assert.AreEqual("fire-escape", LevelManifestEdits.LevelKey(m_Level, null));
        }

        [Test]
        public void CreateRow_AtAPoint_IsOneRowOfTheKind_FreshlyNamed_InItsGroup_OneUndo()
        {
            int before = m_Manifest.entries.Count;
            var point = new Vector3(3f, 0f, -2f);

            LevelObjectDef row = LevelManifestEdits.CreateRow(m_Manifest, m_Kinds, "find", "hall", point, false);

            Assert.AreEqual(before + 1, m_Manifest.entries.Count);
            Assert.AreSame(row, m_Manifest.entries[before]);
            Assert.AreEqual("kind.find", row.kind.entryId);
            Assert.AreEqual("find", row.kind.entryName);
            Assert.AreEqual("hall.find.1", row.id);
            Assert.AreEqual(row.id, row.name);
            Assert.AreEqual(1, m_Manifest.entries.Count(other => other.id == row.id), "the id is unique");
            Assert.AreEqual("prop", row.group, "the group the level already files finds under");
            Assert.AreEqual(new Vector2(3f, -2f), row.position);

            Assert.AreEqual("hall.find.2",
                LevelManifestEdits.CreateRow(m_Manifest, m_Kinds, "find", "hall", point, false).id);
            Assert.AreEqual("door", LevelManifestEdits.GroupFor(m_Manifest, "door"));
            Assert.AreEqual("npc", LevelManifestEdits.CreateRow(m_Manifest, m_Kinds, "npc", "hall", point, false).group);

            int after = m_Manifest.entries.Count;
            Undo.PerformUndo();
            Assert.AreEqual(after - 1, m_Manifest.entries.Count, "one placement is one undo step");
        }

        [Test]
        public void DropPoint_LandsOnTheSurface_BeforeTheGroundPlane(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            m_Manifest.plane = plane;
            Vector3 up = m_Manifest.ToWorld(Vector2.zero, 1f);
            Vector3 above = m_Manifest.ToWorld(new Vector2(1f, 2f), 10f);
            var ray = new Ray(above, -up);
            Vector3 desk = m_Manifest.ToWorld(new Vector2(1f, 2f), 0.8f);

            Assert.IsTrue(LevelManifestEdits.DropPoint(m_Manifest, ray,
                (Ray _, out Vector3 point) => { point = desk; return true; },
                out Vector3 onDesk, out bool onSurface));
            Assert.IsTrue(onSurface);
            Assert.AreEqual(desk, onDesk);

            Assert.IsTrue(LevelManifestEdits.DropPoint(m_Manifest, ray,
                (Ray _, out Vector3 point) => { point = default; return false; },
                out Vector3 onGround, out onSurface));
            Assert.IsFalse(onSurface);
            Assert.AreEqual(new Vector2(1f, 2f), m_Manifest.ToPlan(onGround));
            Assert.AreEqual(0f, Vector3.Dot(onGround, up), 1e-4f, "on the plane");

            var along = new Ray(above, m_Manifest.ToWorld(Vector2.right));
            Assert.IsFalse(LevelManifestEdits.DropPoint(m_Manifest, along, null, out _, out _),
                "a ray along the plane never lands");
        }

        [Test]
        public void Window_HostsThePalette_TheGroups_AndTheSelectedRowsFields()
        {
            m_Window = Open();

            CollectionAssert.AreEqual(new[] { "door", "npc", "find" }, m_Window.palette.Offered());
            Assert.AreEqual(3, m_Window.rootVisualElement.Query(className: LevelKindPalette.TileClass).ToList().Count);
            CollectionAssert.AreEqual(new[] { "door", "npc", "find", "statue", LevelManifestEdits.NoKind },
                Groups().Select(fold => (string)fold.userData).ToArray());

            LevelObjectDef row = m_Window.PlaceAtCentre("door");

            Assert.AreEqual(m_Manifest.entries.IndexOf(row), m_Window.selected);
            List<string> paths = m_Window.rootVisualElement.Query<PropertyField>().ToList()
                .Select(field => field.bindingPath).ToList();
            foreach (string field in new[] { "name", "group", "kind", "entry", "attributes", "tree",
                         "parameters", "position", "facing", "tags" })
            {
                string path = "entries.Array.data[" + m_Window.selected + "]." + field;
                CollectionAssert.Contains(paths, path, field + " is still reached by a PropertyField");
            }
        }

        [Test]
        public void Window_PlacesAtThePointOrTheViewsCentre_SelectsIt_OpensItsGroup_OneUndo()
        {
            EditorPrefs.SetBool(LevelManifestWindow.FoldKey(m_Level.name, "find"), true);
            m_Window = Open();
            Assert.IsFalse(Group("find").value, "a folded group stays folded");
            Assert.IsTrue(Group("door").value);

            int before = m_Manifest.entries.Count;
            LevelObjectDef dropped = m_Window.Place("find", new Vector3(4f, 0f, 5f), true);

            Assert.AreEqual(before + 1, m_Manifest.entries.Count);
            Assert.AreEqual("find", dropped.kind.entryName);
            Assert.AreEqual("hall.find.1", dropped.name);
            Assert.AreEqual(new Vector2(4f, 5f), dropped.position);
            Assert.AreEqual(before, m_Window.selected);
            Assert.IsTrue(Group("find").value, "the new row's group opens");
            Assert.IsFalse(EditorPrefs.GetBool(LevelManifestWindow.FoldKey(m_Level.name, "find"), false), "and stays open");

            LevelObjectDef centred = m_Window.PlaceAtCentre("npc");
            Assert.AreEqual(StateTreeRowCreate.SceneCentre(m_Manifest), centred.position);
            Assert.AreEqual("hall.npc.1", centred.id);

            Undo.PerformUndo();
            Assert.AreEqual(before + 1, m_Manifest.entries.Count, "one placement is one undo step");
            Assert.IsFalse(m_Manifest.entries.Contains(centred));
        }

        private LevelManifestWindow Open()
        {
            var window = ScriptableObject.CreateInstance<LevelManifestWindow>();
            window.Edit(m_Level, m_Kinds, "hall");
            return window;
        }

        private List<Foldout> Groups()
        {
            return m_Window.rootVisualElement.Query<Foldout>(className: LevelManifestWindow.GroupClass).ToList();
        }

        private Foldout Group(string kind)
        {
            return Groups().First(fold => (string)fold.userData == kind);
        }

        private string[] Names(LevelManifestEdits.KindGroup group)
        {
            return group.rows.Select(index => m_Manifest.entries[index].name).ToArray();
        }

        private void Row(string name, string kind, string group)
        {
            var row = new LevelObjectDef { id = "id." + name, name = name, group = group };
            row.kind.entryName = kind;
            row.kind.entryId = string.IsNullOrEmpty(kind) ? "" : "kind." + kind;
            m_Manifest.entries.Add(row);
        }

        private T Make<T>() where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            m_Junk.Add(made);
            return made;
        }
    }
}

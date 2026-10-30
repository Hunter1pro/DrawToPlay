using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A placement's own height (<see cref="LevelObjectDef.overrideHeight"/>): unset, a row stands
    /// at its kind's body height exactly as before; set, it stands there; a moved handle keeps it;
    /// and a row written before the field existed reads as unset.
    /// </summary>
    [TestFixture]
    public sealed class LevelObjectHeightTests
    {
        private const float k_KindHeight = 0.75f;
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
        public void AnUnsetHeight_SpawnsAtTheKindsHeight(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            var row = Row(new Vector2(3f, -4f));

            Vector3 at = SpawnedAt(plane, row);

            Assert.AreEqual(Manifest(plane).ToWorld(new Vector2(3f, -4f), k_KindHeight), at);
        }

        [Test]
        public void ASetHeight_SpawnsThere(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            var row = Row(new Vector2(3f, -4f));
            row.overrideHeight = true;
            row.height = 1.009f;

            Vector3 at = SpawnedAt(plane, row);

            Assert.AreEqual(Manifest(plane).ToWorld(new Vector2(3f, -4f), 1.009f), at);
        }

        [Test]
        public void ASetHeightOfZero_IsTheFloor_NotTheKind()
        {
            var row = Row(Vector2.one);
            row.overrideHeight = true;
            row.height = 0f;

            Assert.AreEqual(0f, Manifest(LevelGroundPlane.XZ).HeightOf(
                SpawnedAt(LevelGroundPlane.XZ, row)));
        }

        [Test]
        public void HeightOf_ReadsBackWhatToWorldTook(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            LevelObjectRegistry manifest = Manifest(plane);
            Vector3 world = manifest.ToWorld(new Vector2(3f, -4f), 1.25f);

            Assert.AreEqual(new Vector2(3f, -4f), manifest.ToPlan(world));
            Assert.AreEqual(1.25f, manifest.HeightOf(world));
        }

        [Test]
        public void ASidewaysDrag_MovesThePlan_AndLeavesTheRowOnItsKind(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            LevelObjectRegistry manifest = Manifest(plane);
            var row = Row(new Vector2(3f, -4f));

            manifest.Place(row, manifest.ToWorld(new Vector2(5f, 6f), k_KindHeight), k_KindHeight);

            Assert.AreEqual(new Vector2(5f, 6f), row.position);
            Assert.IsFalse(row.overrideHeight, "a drag along the ground does not pin today's kind height");
        }

        [Test]
        public void AVerticalDrag_GivesTheRowItsOwnHeight_AndTheHandleRoundTrips(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            LevelObjectRegistry manifest = Manifest(plane);
            var row = Row(new Vector2(3f, -4f));
            Vector3 dragged = manifest.ToWorld(new Vector2(3f, -4f), 1.009f);

            manifest.Place(row, dragged, k_KindHeight);

            Assert.IsTrue(row.overrideHeight);
            Assert.AreEqual(1.009f, row.height);
            Assert.AreEqual(dragged, manifest.ToWorld(row.position, row.HeightOr(k_KindHeight)),
                "where the handle is drawn next is where it was let go");

            manifest.Place(row, manifest.ToWorld(new Vector2(7f, 8f), 1.009f), k_KindHeight);
            Assert.AreEqual(1.009f, row.height, "a later sideways drag keeps the row's own height");
            Assert.IsTrue(row.overrideHeight);
        }

        [Test]
        public void APaletteDrop_OnARaisedSurface_StandsThere_OnTheFloorOrAtTheCentre_KeepsTheKind(
            [Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)] LevelGroundPlane plane)
        {
            LevelObjectRegistry manifest = Manifest(plane);
            var kinds = ScriptableObject.CreateInstance<LevelObjectKindRegistry>();
            kinds.entries.Add(new LevelObjectKindDef { id = "kind.crate", name = "crate" });
            m_Junk.Add(kinds);
            var at = new Vector2(1f, 2f);

            LevelObjectDef onDesk = PowerOfFire.DrawToPlay.Editor.LevelManifestEdits.CreateRow(
                manifest, kinds, "crate", "hall", manifest.ToWorld(at, 0.8f), true);
            LevelObjectDef onFloor = PowerOfFire.DrawToPlay.Editor.LevelManifestEdits.CreateRow(
                manifest, kinds, "crate", "hall", manifest.ToWorld(at, 0.01f), true);
            LevelObjectDef atCentre = PowerOfFire.DrawToPlay.Editor.LevelManifestEdits.CreateRow(
                manifest, kinds, "crate", "hall", manifest.ToWorld(at, 1.5f), false);
            UnityEditor.Undo.ClearUndo(manifest);

            Assert.AreEqual(at, onDesk.position);
            Assert.IsTrue(onDesk.overrideHeight);
            Assert.AreEqual(0.8f, onDesk.height, 1e-5f);
            Assert.IsFalse(onFloor.overrideHeight, "a floor mesh's few centimetres are the floor");
            Assert.IsFalse(atCentre.overrideHeight, "the view's centre is a spot on the plan, not a surface");
        }

        [Test]
        public void ARowWrittenBeforeTheField_ReadsAsUnset()
        {
            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            m_Junk.Add(manifest);
            JsonUtility.FromJsonOverwrite(
                "{\"entries\":[{\"id\":\"old\",\"name\":\"old\",\"position\":{\"x\":1.0,\"y\":2.0},\"facing\":90.0}]}",
                manifest);

            LevelObjectDef old = manifest.entries[0];
            Assert.AreEqual(new Vector2(1f, 2f), old.position);
            Assert.IsFalse(old.overrideHeight);
            Assert.AreEqual(k_KindHeight, old.HeightOr(k_KindHeight));
        }

        private static LevelObjectDef Row(Vector2 at) => new LevelObjectDef
        {
            id = "place.crate",
            name = "Crate",
            kind = new StateTreeEntryRef<LevelObjectKindDef> { entryName = "crate" },
            position = at
        };

        private LevelObjectRegistry Manifest(LevelGroundPlane plane)
        {
            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            manifest.plane = plane;
            m_Junk.Add(manifest);
            return manifest;
        }

        /// <summary>Where the spawner puts this one row, built through a kind whose body sits at
        /// <see cref="k_KindHeight"/>.</summary>
        private Vector3 SpawnedAt(LevelGroundPlane plane, LevelObjectDef row)
        {
            var prefab = new GameObject("Crate") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(prefab);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "crate";
            def.serviceName = "crate";
            def.body.prefab = prefab;
            def.body.height = k_KindHeight;
            m_Junk.Add(def);

            var kinds = ScriptableObject.CreateInstance<LevelObjectKindRegistry>();
            kinds.entries.Add(new LevelObjectKindDef { id = "kind.crate", name = "crate", service = def });
            m_Junk.Add(kinds);

            LevelObjectRegistry manifest = Manifest(plane);
            manifest.entries.Add(row);
            var content = ScriptableObject.CreateInstance<LevelContent>();
            content.objects = manifest;
            m_Junk.Add(content);

            var go = new GameObject("Level");
            m_Junk.Add(go);
            var spawner = go.AddComponent<ManifestSpawner>();
            spawner.level = content;
            spawner.kinds = kinds;
            typeof(ManifestSpawner)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(spawner, null);

            Assert.AreEqual(1, go.transform.childCount, "the row was built under the spawner");
            return go.transform.GetChild(0).position;
        }
    }
}

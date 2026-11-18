using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A row's own ghost: the game answers what a row looks like (a prop's model from its table), the Level
    /// Manifest draws that instead of the kind's empty body; a row nobody answers, or no game at all, keeps the kind's
    /// prefab.</summary>
    [TestFixture]
    public sealed class LevelRowGhostTests
    {
        private sealed class ModelOfM : ILevelRowGhost
        {
            public GameObject model;
            public Matrix4x4 at = Matrix4x4.Translate(new Vector3(0f, 0.5f, 0f));
            public string variant;

            public string VariantOf(LevelObjectKindDef kind, LevelObjectDef row) => variant;

            public GameObject GhostOf(LevelObjectKindDef kind, LevelObjectDef row, out Matrix4x4 local)
            {
                local = at;
                return row.entry != null && row.entry.entryName == "m" ? model : null;
            }
        }

        private GameObject m_Body;
        private GameObject m_Model;
        private LevelObjectKindDef m_Prop;
        private ModelOfM m_Ghost;

        [SetUp]
        public void SetUp()
        {
            m_Body = new GameObject("PropBody") { hideFlags = HideFlags.HideAndDontSave };
            m_Model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Model.hideFlags = HideFlags.HideAndDontSave;
            m_Prop = new LevelObjectKindDef { id = "kind.prop", name = "prop", prefab = m_Body };
            m_Ghost = new ModelOfM { model = m_Model };
            LevelRowGhosts.Register(m_Ghost);
        }

        [TearDown]
        public void TearDown()
        {
            LevelRowGhosts.Unregister(m_Ghost);
            LevelGhostMeshes.Clear();
            Object.DestroyImmediate(m_Body);
            Object.DestroyImmediate(m_Model);
        }

        private static LevelObjectDef Row(string entry)
        {
            var row = new LevelObjectDef { id = "r." + entry, name = entry };
            row.kind.entryName = "prop";
            row.entry = new LevelObjectEntryRef { entryName = entry };
            return row;
        }

        [Test]
        public void ARowTheGameAnswers_GhostsItsModel_WhereItSits()
        {
            Assert.AreSame(m_Model, LevelRowGhosts.Of(m_Prop, Row("m"), out Matrix4x4 local));
            Assert.AreEqual(m_Ghost.at, local);
            Assert.Greater(LevelGhostMeshes.Of(m_Model).Count, 0, "the model draws");
            Assert.AreEqual(0, LevelGhostMeshes.Of(m_Body).Count, "the kind's body alone was the capsule");
        }

        [Test]
        public void ARowNobodyAnswers_OrNoGame_KeepsTheKindsPrefab()
        {
            Assert.AreSame(m_Body, LevelRowGhosts.Of(m_Prop, Row("other"), out Matrix4x4 local));
            Assert.AreEqual(Matrix4x4.identity, local);
            LevelRowGhosts.Unregister(m_Ghost);
            LevelRowGhosts.Unregister(m_Ghost);
            Assert.AreSame(m_Body, LevelRowGhosts.Of(m_Prop, Row("m"), out _), "unregistered, twice harmlessly");
            Assert.IsNull(LevelRowGhosts.Of(null, Row("m"), out _), "no kind, nothing");
        }

        [Test]
        public void AGhostsParts_KeepTheirMaterials_AndASwitchedOffRendererIsNotPartOfTheLook()
        {
            var hull = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hull.transform.SetParent(m_Model.transform, false);
            hull.GetComponent<MeshRenderer>().enabled = false;
            LevelGhostMeshes.Clear();

            IReadOnlyList<LevelGhostMeshes.Part> parts = LevelGhostMeshes.Of(m_Model);
            Assert.AreEqual(1, parts.Count, "the hull, off until lit, is left out");
            Material own = m_Model.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreSame(own, parts[0].MaterialOf(0), "drawn in its own material");
            Assert.AreSame(own, parts[0].MaterialOf(3), "the last material repeats, as Unity draws it");
            Assert.IsNull(new LevelGhostMeshes.Part(parts[0].mesh, Matrix4x4.identity).MaterialOf(0), "none: the hologram");
        }

        [Test]
        public void ASwitchedOffVariant_IsLeftOut_UnlessTheRowNamesIt()
        {
            foreach (string name in new[] { "Made", "Folded" })
            {
                var variant = GameObject.CreatePrimitive(PrimitiveType.Cube);
                variant.name = name;
                variant.transform.SetParent(m_Model.transform, false);
                variant.SetActive(false);
            }
            LevelGhostMeshes.Clear();

            Assert.AreEqual(1, LevelGhostMeshes.Of(m_Model).Count, "only what the model has on");
            Assert.AreEqual(2, LevelGhostMeshes.Of(m_Model, "Folded").Count, "and the variant the row picks");
            Assert.AreEqual(1, LevelGhostMeshes.Of(m_Model, "Burnt").Count, "a name it lacks adds nothing");

            m_Ghost.variant = "Folded";
            LevelRowGhosts.Of(m_Prop, Row("m"), out _, out string named);
            Assert.AreEqual("Folded", named, "the game names the variant");
            LevelRowGhosts.Of(m_Prop, Row("other"), out _, out string none);
            Assert.IsNull(none, "the kind's own prefab names none");
        }

        [Test]
        public void RegisteringTwice_AsksOnce()
        {
            var counting = new Counting();
            LevelRowGhosts.Register(counting);
            LevelRowGhosts.Register(counting);
            try
            {
                LevelRowGhosts.Of(m_Prop, Row("other"), out _);
                Assert.AreEqual(1, counting.asked);
            }
            finally
            {
                LevelRowGhosts.Unregister(counting);
            }
        }

        private sealed class Counting : ILevelRowGhost
        {
            public int asked;

            public GameObject GhostOf(LevelObjectKindDef kind, LevelObjectDef row, out Matrix4x4 local)
            {
                asked++;
                local = Matrix4x4.identity;
                return null;
            }
        }
    }
}

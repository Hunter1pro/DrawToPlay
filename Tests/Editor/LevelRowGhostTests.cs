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

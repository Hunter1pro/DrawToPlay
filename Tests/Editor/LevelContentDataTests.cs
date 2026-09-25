using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A level's own data rides the level: <see cref="LevelContent.Data{T}"/> answers by type
    /// (first of the type, null when absent), and the list survives serialisation with its
    /// references intact.
    /// </summary>
    [TestFixture]
    public sealed class LevelContentDataTests
    {
        private readonly List<Object> m_Objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            m_Objects.Clear();
        }

        private T Make<T>(string assetName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            m_Objects.Add(asset);
            return asset;
        }

        [Test]
        public void Data_ReturnsTheFirstOfTheType()
        {
            LevelContent content = Make<LevelContent>("Level");
            LevelObjectRegistry other = Make<LevelObjectRegistry>("Other");
            LevelRegistry first = Make<LevelRegistry>("First");
            LevelRegistry second = Make<LevelRegistry>("Second");
            content.data.Add(other);
            content.data.Add(first);
            content.data.Add(second);

            Assert.AreSame(first, content.Data<LevelRegistry>());
            Assert.AreSame(other, content.Data<LevelObjectRegistry>());
        }

        [Test]
        public void Data_IsNull_WhenAbsent()
        {
            LevelContent content = Make<LevelContent>("Level");
            Assert.IsNull(content.Data<LevelRegistry>());

            content.data.Add(null);
            content.data.Add(Make<LevelObjectRegistry>("Other"));
            Assert.IsNull(content.Data<LevelRegistry>());

            content.data = null;
            Assert.IsNull(content.Data<LevelRegistry>());
        }

        [Test]
        public void Data_SurvivesSerialisation()
        {
            LevelContent content = Make<LevelContent>("Level");
            LevelRegistry carried = Make<LevelRegistry>("Carried");
            content.data.Add(carried);

            LevelContent copy = Object.Instantiate(content);
            m_Objects.Add(copy);
            Assert.AreEqual(1, copy.data.Count);
            Assert.AreSame(carried, copy.Data<LevelRegistry>());

            var serialized = new SerializedObject(content);
            SerializedProperty list = serialized.FindProperty(nameof(LevelContent.data));
            Assert.IsNotNull(list);
            Assert.AreEqual(1, list.arraySize);
            Assert.AreSame(carried, list.GetArrayElementAtIndex(0).objectReferenceValue);
        }
    }
}

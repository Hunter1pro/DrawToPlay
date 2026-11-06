using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    public sealed class StateTreeFoldsTests
    {
        private const string k_Folder = "Assets/__FoldsTests";

        private string m_GuidA;
        private string m_GuidB;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__FoldsTests");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (StateTreeEditorWindow window in Resources.FindObjectsOfTypeAll<StateTreeEditorWindow>())
                window.Close();
            foreach (string guid in new[] { m_GuidA, m_GuidB })
            {
                if (string.IsNullOrEmpty(guid))
                    continue;
                EditorPrefs.DeleteKey(StateTreeFolds.FoldsKey(guid));
                EditorPrefs.DeleteKey(StateTreeFolds.SelectionKey(guid));
            }
            AssetDatabase.DeleteAsset(k_Folder);
        }

        [Test]
        public void Folds_AreKeptPerTree_ThroughANewWindow_UnknownRowsOpen_AnotherTreeUntouched()
        {
            StateTreeAsset a = Tree(k_Folder + "/A.asset");
            StateTreeAsset b = Tree(k_Folder + "/B.asset");
            m_GuidA = StateTreeFolds.GuidOf(a);
            m_GuidB = StateTreeFolds.GuidOf(b);
            Assert.IsNotEmpty(m_GuidA);

            StateTreeEditorWindow window = StateTreeEditorWindow.Open(a);
            Assert.IsFalse(window.IsFolded("chapter"), "a row never folded starts open");
            window.SetFolded("chapter", true);
            Assert.IsTrue(window.SelectNode("other"));
            CollectionAssert.AreEquivalent(new[] { "chapter" }, StateTreeFolds.Folded(m_GuidA));
            Assert.AreEqual("other", StateTreeFolds.Selected(m_GuidA));

            window.Close();
            window = StateTreeEditorWindow.Open(a);
            Assert.IsTrue(window.IsFolded("chapter"), "the fold outlives the window (a reload or a restart)");
            Assert.IsFalse(window.IsFolded("other"), "a row with no saved fold keeps the default");
            Assert.AreEqual("other", window.selectedNodeId, "the selection comes back");

            window.LoadTree(b);
            Assert.IsFalse(window.IsFolded("chapter"), "another tree with the same ids is not folded");
            Assert.AreEqual("", window.selectedNodeId);
            Assert.IsFalse(EditorPrefs.HasKey(StateTreeFolds.FoldsKey(m_GuidB)));

            window.LoadTree(a);
            window.SetFolded("chapter", false);
            Assert.IsFalse(EditorPrefs.HasKey(StateTreeFolds.FoldsKey(m_GuidA)), "nothing folded leaves no key");
        }

        [Test]
        public void SelectingARowOnPurpose_OpensTheRowsHoldingIt()
        {
            StateTreeAsset a = Tree(k_Folder + "/A.asset");
            m_GuidA = StateTreeFolds.GuidOf(a);
            StateTreeEditorWindow window = StateTreeEditorWindow.Open(a);
            window.SetFolded("chapter", true);

            Assert.IsTrue(window.SelectNode("leaf"));

            Assert.IsFalse(window.IsFolded("chapter"));
            CollectionAssert.IsEmpty(StateTreeFolds.Folded(m_GuidA));
        }

        private static StateTreeAsset Tree(string path)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            AssetDatabase.CreateAsset(tree, path);
            StateTreeNodeAsset root = StateTreeEditorOps.CreateNode(tree, null, "root", "Root", "t");
            StateTreeNodeAsset chapter = StateTreeEditorOps.CreateNode(tree, root, "chapter", "Chapter", "t");
            StateTreeEditorOps.CreateNode(tree, chapter, "leaf", "Leaf", "t");
            StateTreeNodeAsset other = StateTreeEditorOps.CreateNode(tree, root, "other", "Other", "t");
            StateTreeEditorOps.CreateNode(tree, other, "other.leaf", "Other leaf", "t");
            return tree;
        }
    }
}

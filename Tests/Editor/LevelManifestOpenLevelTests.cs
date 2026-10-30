using System.IO;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The Level Manifest follows the open scene, on two levels saved in a throwaway folder: a level made while another
    /// is shown (New level builds its scene with NewScene and saves it, which raises no sceneOpened) is the one shown
    /// and the one a placement writes into; with no level owning the scene the window shows none, offers New level, and
    /// a placement writes nowhere. The open scene is a path the test hands the window; no scene is opened.
    /// </summary>
    [TestFixture]
    public sealed class LevelManifestOpenLevelTests
    {
        private const string Root = "Assets/__DtpManifestOpenLevel";

        private LevelManifestWindow m_Window;
        private string m_Open;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(AssetDatabase.IsValidFolder(Root), Root + " is left from an earlier run");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(Root));
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Window != null)
                Object.DestroyImmediate(m_Window);
            foreach (string path in new[] { Root + "/OldContent.asset", Root + "/NewContent.asset" })
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelContent>(path);
                if (level != null && level.objects != null)
                    Undo.ClearUndo(level.objects);
            }
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void ALevelMadeWhileAnotherIsShown_IsShown_AndTakesTheNextObject()
        {
            LevelContent old = Level("Old");
            m_Open = old.scenePath;
            m_Window = Follow();
            Assert.AreSame(old, m_Window.level);

            // New level: its files, then its scene saved under its path — no sceneOpened.
            LevelContent made = Level("New");
            m_Open = made.scenePath;

            Assert.AreSame(made, m_Window.level, "the open scene's level, not the one shown before");
            Assert.IsNotNull(m_Window.PlaceAtCentre("door"));
            Assert.AreEqual(1, made.objects.entries.Count, "the object is the new level's");
            Assert.AreEqual(0, old.objects.entries.Count, "the level shown before is not written");
            Assert.IsFalse(EditorUtility.IsDirty(old.objects));
        }

        [Test]
        public void NoLevelOwnsTheScene_TheWindowShowsNone_AndAPlacementWritesNowhere()
        {
            LevelContent old = Level("Old");
            m_Open = old.scenePath;
            m_Window = Follow();
            Assert.AreSame(old, m_Window.level);

            m_Open = Root + "/NoLevel.unity";
            Assert.IsNull(m_Window.level, "none — never the last level");
            Assert.IsNull(m_Window.PlaceAtCentre("door"));
            Assert.AreEqual(0, old.objects.entries.Count);
            Assert.IsFalse(EditorUtility.IsDirty(old.objects));

            m_Window.Follow(() => m_Open);
            if (LevelSetups.current != null)
                Assert.AreEqual(1, m_Window.rootVisualElement.Query<Button>(className: LevelManifestWindow.NewLevelClass)
                    .ToList().Count, "+ New level… instead of a manifest");

            m_Open = "";
            Assert.IsNull(m_Window.level, "an unsaved scene is no level's");
        }

        [Test]
        public void LevelOfScene_FindsALevelSavedAMomentAgo()
        {
            LevelContent made = Level("New");
            Assert.AreSame(made, LevelSetups.LevelOfScene(made.scenePath));
            Assert.IsNull(LevelSetups.LevelOfScene(Root + "/NoLevel.unity"));
            Assert.IsNull(LevelSetups.LevelOfScene(""));
        }

        private LevelManifestWindow Follow()
        {
            var window = ScriptableObject.CreateInstance<LevelManifestWindow>();
            window.Follow(() => m_Open);
            return window;
        }

        private static LevelContent Level(string stem)
        {
            var objects = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            objects.plane = LevelGroundPlane.XZ;
            AssetDatabase.CreateAsset(objects, Root + "/" + stem + "Objects.asset");
            var level = ScriptableObject.CreateInstance<LevelContent>();
            level.scenePath = Root + "/" + stem + ".unity";
            level.objects = objects;
            AssetDatabase.CreateAsset(level, Root + "/" + stem + "Content.asset");
            return level;
        }
    }
}

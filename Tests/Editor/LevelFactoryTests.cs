using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A level in one click: the factory makes the content, the manifest and the scene in one
    /// folder, wires them to each other and to the registry row, lists the scene in the build,
    /// and the game's template fills the place and its starter rows. Run against the
    /// waystation's own level registry, into a temporary folder that is removed again.
    /// </summary>
    [TestFixture]
    public sealed class LevelFactoryTests
    {
        private const string k_Levels = "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Levels.asset";
        private const string k_Folder = "Assets/DrawToPlay/Tests/Editor/Temp_Quarry";
        private const string k_Name = "quarry-test";

        private int m_RowsBefore;
        private LevelRegistry m_Levels;

        [SetUp]
        public void SetUp()
        {
            m_Levels = AssetDatabase.LoadAssetAtPath<LevelRegistry>(k_Levels);
            Assume.That(m_Levels, Is.Not.Null, "run Verify M21 Waystation first");
            m_RowsBefore = m_Levels.entries.Count;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Levels != null)
            {
                m_Levels.entries.RemoveAll(row => row != null && row.name == k_Name);
                EditorUtility.SetDirty(m_Levels);
            }
            LevelFactory.UnregisterFromBuild(k_Folder + "/QuarryTest.unity");
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void OneClick_MakesAWiredLevel_ReadyToTravelTo()
        {
            ILevelTemplate outpost = LevelTemplates.All().Find(t => t.title == "Outpost room");
            Assert.That(outpost, Is.Not.Null, "the waystation offers its recipe for a place");

            LevelDef made = LevelFactory.Create(m_Levels, k_Name, "Tests", k_Folder, outpost, out string report);
            Assert.That(made, Is.Not.Null, report);
            Assert.That(m_Levels.entries.Count, Is.EqualTo(m_RowsBefore + 1), "one row added");
            Assert.That(made.id, Is.EqualTo("level." + k_Name));
            Assert.That(made.group, Is.EqualTo("Tests"));

            // THE ASSETS, in the folder, wired to each other.
            var content = AssetDatabase.LoadAssetAtPath<LevelContent>(k_Folder + "/QuarryTestContent.asset");
            var manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(k_Folder + "/QuarryTestObjects.asset");
            Assert.That(content, Is.Not.Null);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(made.content, Is.SameAs(content));
            Assert.That(content.objects, Is.SameAs(manifest));
            Assert.That(content.scenePath, Is.EqualTo(k_Folder + "/QuarryTest.unity"));
            Assert.That(content.displayName, Is.EqualTo("Quarry Test"));
            Assert.That(manifest.dependsOn, Does.Contain(m_Levels), "destinations pick from the level catalog");
            Assert.That(manifest.tags, Is.Not.Empty, "wears the siblings' vocabularies");
            Assert.That(manifest.plane, Is.EqualTo(LevelGroundPlane.XZ), "the siblings' plane");

            // THE STARTER ROWS the template copied: a player, a camera, a way back.
            Assert.That(manifest.entries.Exists(r => r.kind.entryName == "player" && r.tree != null), Is.True, report);
            Assert.That(manifest.entries.Exists(r => r.kind.entryName == "camera"), Is.True, report);
            LevelObjectDef back = manifest.entries.Find(r => r.kind.entryName == "exit");
            Assert.That(back, Is.Not.Null, report);
            Assert.That(back.parameters.values.Exists(p => p.name == "destination" && p.stringValue == "yard"),
                Is.True, "the exit leads back to the sibling it was copied from");

            // THE BUILD knows the scene, and THE SCENE holds the place.
            Assert.That(System.Array.Exists(EditorBuildSettings.scenes, s => s.path == content.scenePath && s.enabled), Is.True);
            Scene scene = EditorSceneManager.OpenScene(content.scenePath, OpenSceneMode.Additive);
            try
            {
                StateTreeContextHost host = null;
                StateTreeServiceInstaller installer = null;
                Component spawner = null;
                var hasGround = false;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    host ??= go.GetComponentInChildren<StateTreeContextHost>(true);
                    installer ??= go.GetComponentInChildren<StateTreeServiceInstaller>(true);
                    spawner ??= go.GetComponentInChildren<Examples.OutpostManifestSpawner>(true);
                    if (go.transform.Find("Ground") != null)
                        hasGround = true;
                }
                Assert.That(host, Is.Not.Null, "a Level host");
                Assert.That(host.kind, Is.EqualTo(StateTreeContextKind.Level));
                Assert.That(installer, Is.Not.Null);
                Assert.That(installer.install.Count, Is.EqualTo(2), "the world and the ability subsystems");
                Assert.That(spawner, Is.Not.Null, "the spawner that builds the manifest");
                Assert.That(((Examples.OutpostManifestSpawner)spawner).level, Is.SameAs(content));
                Assert.That(hasGround, Is.True, "the template put the floor in");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            // And a second click with the same name is refused, not duplicated.
            Assert.That(LevelFactory.Create(m_Levels, k_Name, "Tests", k_Folder, outpost, out string again), Is.Null);
            Assert.That(again, Does.Contain("already"));
        }

        /// <summary>The registry's inspector carries the box: name, group, folder (defaulting
        /// to where the first level keeps its content), the game's template, one button.</summary>
        [Test]
        public void TheLevelRegistryInspector_OffersTheNewLevelBox()
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(m_Levels);
            try
            {
                UnityEngine.UIElements.VisualElement root = editor.CreateInspectorGUI();
                Assert.That(root, Is.Not.Null, "a UI Toolkit host");
                var panel = root.Q<NewLevelPanel>();
                Assert.That(panel, Is.Not.Null, "the New level box");
                var button = panel.Q<UnityEngine.UIElements.Button>();
                Assert.That(panel.Query<UnityEngine.UIElements.Button>().ToList()
                    .Exists(b => b.text == "Create level"), Is.True);
                var template = panel.Q<UnityEngine.UIElements.DropdownField>();
                Assert.That(template.choices, Does.Contain("Outpost room"));
                var folder = panel.Query<UnityEngine.UIElements.TextField>().ToList()
                    .Find(f => f.label == "Folder");
                Assert.That(folder.value, Is.EqualTo("Assets/DrawToPlayExamples/Demo/M21/Levels"),
                    "where the first level keeps its content");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        /// <summary>The content's scene is picked as the scene ASSET and kept as the path the
        /// loader reads; the field follows the string, and writes it.</summary>
        [Test]
        public void TheLevelContentsScene_IsPickedAsAnAsset_AndKeptAsThePath()
        {
            var content = ScriptableObject.CreateInstance<LevelContent>();
            content.scenePath = "Assets/DrawToPlayExamples/Demo/M21/Levels/M21Yard.unity";
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(content);
            try
            {
                // The drawer's own element — a PropertyField asks its drawer only once it is
                // bound inside a panel, which a test has none of.
                SerializedProperty property = editor.serializedObject.FindProperty("scenePath");
                VisualElement root = new ScenePathDrawer().CreatePropertyGUI(property);
                var field = root.Q<ObjectField>();
                Assert.That(field, Is.Not.Null, "the path is drawn as a scene field");
                Assert.That(field.objectType, Is.EqualTo(typeof(SceneAsset)));
                Assert.That(field.value, Is.SameAs(AssetDatabase.LoadAssetAtPath<SceneAsset>(content.scenePath)),
                    "the field reads the scene the string names");

                // The drop's write (a change event only dispatches inside a panel).
                var ridge = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    "Assets/DrawToPlayExamples/Demo/M21/Levels/M21Ridge.unity");
                ScenePathDrawer.Write(editor.serializedObject, "scenePath", ridge);
                Assert.That(content.scenePath, Is.EqualTo("Assets/DrawToPlayExamples/Demo/M21/Levels/M21Ridge.unity"),
                    "dropping a scene writes its path");
                ScenePathDrawer.Write(editor.serializedObject, "scenePath", null);
                Assert.That(content.scenePath, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(content);
            }
        }

        [Test]
        public void Names_BecomeStemsAndTitles()
        {
            Assert.That(LevelFactory.Stem("sunken-cave"), Is.EqualTo("SunkenCave"));
            Assert.That(LevelFactory.Title("sunken-cave"), Is.EqualTo("Sunken Cave"));
            Assert.That(LevelFactory.Stem("  "), Is.EqualTo("Level"));
        }
    }
}

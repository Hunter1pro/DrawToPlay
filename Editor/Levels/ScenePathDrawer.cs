using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE SCENE FIELD over a path string: drop the scene, the path is written; the path
    /// reads underneath so what the loader will use is never hidden. A path that names no
    /// scene says so (the scene was moved or renamed — the string did not follow), and a
    /// scene not listed in Build Settings offers the one click that makes travel work.
    /// </summary>
    [CustomPropertyDrawer(typeof(ScenePathAttribute))]
    internal sealed class ScenePathDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new PropertyField(property);

            var container = new VisualElement();
            var field = new ObjectField(property.displayName)
            {
                objectType = typeof(SceneAsset), allowSceneObjects = false
            };
            field.tooltip = "The level's scene. Stored as its project path — what the loader "
                + "reads — and shown underneath.";
            var status = new Label();
            status.style.marginLeft = 12f;
            status.style.opacity = 0.7f;
            status.style.whiteSpace = WhiteSpace.Normal;
            var list = new Button { text = "list in Build Settings" };
            list.style.alignSelf = Align.FlexStart;
            list.style.marginLeft = 12f;
            list.tooltip = "An additive load by path is a build lookup: a scene not listed "
                + "never arrives, and travel fails with a message about Build Settings.";
            container.Add(field);
            container.Add(status);
            container.Add(list);

            string path = property.propertyPath;
            SerializedObject owner = property.serializedObject;

            void Refresh()
            {
                SerializedProperty live = owner.FindProperty(path);
                string scenePath = live != null ? live.stringValue : "";
                var scene = string.IsNullOrEmpty(scenePath)
                    ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                field.SetValueWithoutNotify(scene);
                bool listed = scene != null && IsListed(scenePath);
                status.text = string.IsNullOrEmpty(scenePath)
                    ? "no scene — drop one here"
                    : scene == null
                        ? scenePath + "  — no scene at this path (moved or renamed?)"
                        : scenePath + (listed ? "" : "  — not in Build Settings");
                status.style.color = scene == null && !string.IsNullOrEmpty(scenePath)
                    ? new Color(1f, 0.55f, 0.45f)
                    : listed || scene == null ? new Color(0.75f, 0.75f, 0.75f) : new Color(1f, 0.8f, 0.4f);
                list.style.display = scene != null && !listed ? DisplayStyle.Flex : DisplayStyle.None;
            }

            field.RegisterValueChangedCallback(evt =>
            {
                Write(owner, path, evt.newValue as SceneAsset);
                Refresh();
            });
            list.clicked += () =>
            {
                SerializedProperty live = owner.FindProperty(path);
                if (live != null && !string.IsNullOrEmpty(live.stringValue))
                    LevelFactory.RegisterInBuild(live.stringValue);
                Refresh();
            };
            // The string can change under the field — the level factory, an undo, a script —
            // and the field follows the string, never the other way round.
            container.TrackPropertyValue(property, _ => Refresh());
            Refresh();
            return container;
        }

        /// <summary>The one write: the picked scene's project path, or empty for none.</summary>
        internal static void Write(SerializedObject owner, string propertyPath, SceneAsset picked)
        {
            owner.Update();
            SerializedProperty live = owner.FindProperty(propertyPath);
            if (live == null)
                return;
            live.stringValue = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
            owner.ApplyModifiedProperties();
        }

        private static bool IsListed(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled && string.Equals(scenes[i].path, scenePath, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}

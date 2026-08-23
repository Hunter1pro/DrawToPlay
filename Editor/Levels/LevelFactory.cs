using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A LEVEL IN ONE CLICK. A level was four things made by hand and wired by hand — a
    /// content asset, a manifest, a scene with a level host, a registry row — plus a line in
    /// Build Settings, and forgetting any one of them gives a destination that "never
    /// arrives". This makes all of them in one folder, wires them to each other, lists the
    /// scene in the build, adds the row, and lets the game's template fill the place. The
    /// waystation's Verify still builds its own levels; this is for the next one.
    /// </summary>
    public static class LevelFactory
    {
        public static LevelDef Create(LevelRegistry levels, string levelName, string group,
            string folder, ILevelTemplate template, out string report)
        {
            report = "";
            if (levels == null)
            {
                report = "no level registry";
                return null;
            }
            string key = (levelName ?? "").Trim();
            if (key.Length == 0)
            {
                report = "name the level first";
                return null;
            }
            if (levels.FindByName(key) != null)
            {
                report = "'" + key + "' is already a level in " + levels.name;
                return null;
            }
            folder = (folder ?? "").Trim().TrimEnd('/');
            if (!folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                report = "the folder must be under Assets/";
                return null;
            }
            // A FOLDER PER LEVEL: a level is several assets — scene, content, manifest, its
            // material, its nav mesh, its zones — and they live together, under the folder the
            // author chose, in a folder named for the level.
            string stem = Stem(key);
            folder = folder + "/" + stem;
            if (AssetDatabase.IsValidFolder(folder))
            {
                report = folder + " already exists";
                return null;
            }
            if (!EnsureFolder(folder))
            {
                report = "could not create " + folder;
                return null;
            }

            string scenePath = folder + "/" + stem + ".unity";
            string contentPath = folder + "/" + stem + "Content.asset";
            string objectsPath = folder + "/" + stem + "Objects.asset";
            foreach (string path in new[] { scenePath, contentPath, objectsPath })
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    report = path + " already exists";
                    return null;
                }
            }

            // THE SCENE THAT IS OPEN stays open: the level is made beside it and closed again.
            // Unity cannot add a scene beside an UNTITLED one, so on a blank editor the level
            // replaces the blank and a blank is put back; an untitled scene with work in it is
            // the author's to save first — nothing here discards it.
            Scene active = SceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(active.path);
            if (untitled && active.isDirty)
            {
                report = "save or close the untitled scene first — the level is made beside the open scene";
                return null;
            }

            // THE SIBLINGS say what a level here looks like: which ground plane the manifests
            // use and which vocabularies they wear, so the new one matches without asking.
            var siblings = new List<LevelDef>();
            for (int i = 0; i < levels.entries.Count; i++)
            {
                if (levels.entries[i] != null)
                    siblings.Add(levels.entries[i]);
            }

            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            manifest.dependsOn.Add(levels);   // destinations are picked from the level catalog
            LevelObjectRegistry example = FirstManifest(siblings);
            if (example != null)
            {
                manifest.plane = example.plane;
                for (int i = 0; i < example.tags.Count; i++)
                {
                    if (example.tags[i] != null && !manifest.tags.Contains(example.tags[i]))
                        manifest.tags.Add(example.tags[i]);
                }
                for (int i = 0; i < example.dependsOn.Count; i++)
                {
                    if (example.dependsOn[i] != null && !manifest.dependsOn.Contains(example.dependsOn[i]))
                        manifest.dependsOn.Add(example.dependsOn[i]);
                }
            }
            AssetDatabase.CreateAsset(manifest, objectsPath);

            var content = ScriptableObject.CreateInstance<LevelContent>();
            content.displayName = Title(key);
            content.scenePath = scenePath;
            content.objects = manifest;
            AssetDatabase.CreateAsset(content, contentPath);

            var row = new LevelDef
            {
                id = "level." + key, name = key, group = group ?? "", content = content
            };

            // THE SCENE: a light, the level host with its installer, and whatever the game's
            // template puts in. Made beside the open scene and closed again, so nothing the
            // author had open is disturbed.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            // A SCENE CHANGE REIMPORTS: the instances CreateAsset handed back can be replaced
            // under us, so the assets are taken by path again — here and after the save.
            manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(objectsPath);
            content = AssetDatabase.LoadAssetAtPath<LevelContent>(contentPath);
            content.objects = manifest;
            row.content = content;
            var build = new LevelBuild
            {
                scene = scene, levels = levels, kinds = levels.kinds, row = row,
                content = content, manifest = manifest, folder = folder
            };
            build.siblings.AddRange(siblings);
            try
            {
                var light = new GameObject("Directional Light");
                SceneManager.MoveGameObjectToScene(light, scene);
                Light lightComponent = light.AddComponent<Light>();
                lightComponent.type = LightType.Directional;
                lightComponent.intensity = 1f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                build.levelObject = new GameObject("Level");
                SceneManager.MoveGameObjectToScene(build.levelObject, scene);
                build.host = build.levelObject.AddComponent<StateTreeContextHost>();
                build.host.kind = StateTreeContextKind.Level;
                build.host.autoStart = false;
                build.installer = build.levelObject.AddComponent<StateTreeServiceInstaller>();
                build.installer.scope = build.host;

                if (template != null)
                {
                    try
                    {
                        template.Build(build);
                    }
                    catch (Exception e)
                    {
                        build.notes.Add("template '" + template.title + "' failed: " + e.Message);
                        Debug.LogException(e);
                    }
                }
                EditorUtility.SetDirty(manifest);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally
            {
                if (untitled)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else
                    EditorSceneManager.CloseScene(scene, true);
            }

            RegisterInBuild(scenePath);

            manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(objectsPath);
            content = AssetDatabase.LoadAssetAtPath<LevelContent>(contentPath);
            if (content.objects != manifest)
                content.objects = manifest;
            row.content = content;
            Undo.RecordObject(levels, "Create Level");
            levels.entries.Add(row);
            EditorUtility.SetDirty(levels);
            EditorUtility.SetDirty(content);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();

            var lines = new List<string>
            {
                "'" + key + "' → " + scenePath,
                "content " + Path.GetFileName(contentPath) + ", manifest "
                    + Path.GetFileName(objectsPath) + " (" + manifest.entries.Count + " rows), listed in Build Settings"
            };
            lines.AddRange(build.notes);
            report = string.Join("\n", lines);
            return row;
        }

        /// <summary>The folder a new level defaults to — the one the levels' own folders sit in:
        /// the parent of where the registry's first level keeps its content (each level has a
        /// folder of its own), or the registry's own folder.</summary>
        public static string DefaultFolder(LevelRegistry levels)
        {
            if (levels == null)
                return "Assets";
            for (int i = 0; i < levels.entries.Count; i++)
            {
                LevelContent content = levels.entries[i] != null ? levels.entries[i].content : null;
                string path = content != null ? AssetDatabase.GetAssetPath(content) : "";
                if (string.IsNullOrEmpty(path))
                    continue;
                string levelFolder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                string parent = Path.GetDirectoryName(levelFolder)?.Replace('\\', '/');
                return string.IsNullOrEmpty(parent) || parent.Length < "Assets".Length ? levelFolder : parent;
            }
            string own = AssetDatabase.GetAssetPath(levels);
            return string.IsNullOrEmpty(own) ? "Assets" : Path.GetDirectoryName(own).Replace('\\', '/');
        }

        private static LevelObjectRegistry FirstManifest(List<LevelDef> siblings)
        {
            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].objects != null)
                    return siblings[i].objects;
            }
            return null;
        }

        /// <summary>"sunken-cave" → "SunkenCave"; the file stem every asset of the level shares.</summary>
        public static string Stem(string levelName)
        {
            var stem = new System.Text.StringBuilder();
            bool upper = true;
            foreach (char c in levelName)
            {
                if (char.IsLetterOrDigit(c))
                {
                    stem.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else
                    upper = true;
            }
            return stem.Length > 0 ? stem.ToString() : "Level";
        }

        /// <summary>"sunken-cave" → "Sunken Cave"; what the HUD calls the place until renamed.</summary>
        public static string Title(string levelName)
        {
            var title = new System.Text.StringBuilder();
            bool upper = true;
            foreach (char c in levelName)
            {
                if (char.IsLetterOrDigit(c))
                {
                    title.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else if (title.Length > 0 && title[title.Length - 1] != ' ')
                {
                    title.Append(' ');
                    upper = true;
                }
            }
            return title.ToString().Trim();
        }

        private static bool EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return true;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || !EnsureFolder(parent))
                return false;
            return !string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder)));
        }

        /// <summary>An additive load by path is a build lookup: a scene not listed never
        /// arrives, and travel fails with a message about Build Settings rather than the level.</summary>
        public static void RegisterInBuild(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    if (!scenes[i].enabled)
                    {
                        scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                        EditorBuildSettings.scenes = scenes.ToArray();
                    }
                    return;
                }
            }
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public static void UnregisterFromBuild(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int before = scenes.Count;
            scenes.RemoveAll(s => s.path == scenePath);
            if (scenes.Count != before)
                EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

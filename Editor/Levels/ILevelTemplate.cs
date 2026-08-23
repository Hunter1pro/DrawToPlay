using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHAT FILLS A NEW LEVEL'S SCENE — the game's recipe for "a place" (meta-rule 5: the
    /// runtime defines the seam, the game implements it). The level factory makes and wires
    /// everything a level IS — the content asset, the manifest, the scene with its level host
    /// and installer, the registry row, the build-settings entry — and hands the scene to a
    /// template to put the ground, the walls, the level's services and its starter rows in.
    /// A project with no template still gets a bare, wired level.
    /// </summary>
    public interface ILevelTemplate
    {
        /// <summary>What the dropdown calls it — "Outpost room".</summary>
        string title { get; }

        void Build(LevelBuild build);
    }

    /// <summary>Everything a template may touch while a level is being made.</summary>
    public sealed class LevelBuild
    {
        public Scene scene;

        /// <summary>The object carrying the Level host and its installer.</summary>
        public GameObject levelObject;
        public StateTreeContextHost host;
        public StateTreeServiceInstaller installer;

        public LevelRegistry levels;
        public LevelObjectKindRegistry kinds;
        public LevelDef row;
        public LevelContent content;
        public LevelObjectRegistry manifest;
        public string folder;

        /// <summary>The other levels in the registry — what a template copies a player or an
        /// exit from, and where an exit back may lead.</summary>
        public readonly List<LevelDef> siblings = new List<LevelDef>();

        /// <summary>What the template did, one line each — shown under the button.</summary>
        public readonly List<string> notes = new List<string>();

        /// <summary>A sibling's placement of this kind, or null — the template copies its tree
        /// and kind rather than knowing how a player is wired.</summary>
        public LevelObjectDef SiblingPlacement(string kindName)
        {
            for (int i = 0; i < siblings.Count; i++)
            {
                LevelObjectRegistry objects = siblings[i] != null ? siblings[i].objects : null;
                for (int j = 0; objects != null && j < objects.entries.Count; j++)
                {
                    LevelObjectDef candidate = objects.entries[j];
                    if (candidate != null && candidate.kind != null
                        && string.Equals(candidate.kind.entryName, kindName, StringComparison.Ordinal))
                        return candidate;
                }
            }
            return null;
        }
    }

    /// <summary>The templates the project offers, found by type — a game adds one by writing
    /// a class, not by registering it anywhere.</summary>
    public static class LevelTemplates
    {
        public static List<ILevelTemplate> All()
        {
            var found = new List<ILevelTemplate>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ILevelTemplate>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                try
                {
                    found.Add((ILevelTemplate)Activator.CreateInstance(type));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Levels] template " + type.Name + " could not be made: " + e.Message);
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.title, b.title));
            return found;
        }
    }
}

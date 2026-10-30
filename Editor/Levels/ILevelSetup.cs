using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A game's way to make a level and to connect it to its story (meta-rule 5: the package defines the seam, the
    /// game implements it). The package asks it when the designer makes a new level — from the level catalogue, the
    /// Level Manifest with no level open, or a mission node — and when the first object is placed in a level whose
    /// episode entry or own tree is still missing. Registered from the game's <c>[InitializeOnLoad]</c> class.
    /// </summary>
    public interface ILevelSetup
    {
        /// <summary>The catalogue a new level's row goes in — the one the session travels by.</summary>
        LevelRegistry levels { get; }

        /// <summary>What a new level's place is called ("Episode").</summary>
        string placeLabel { get; }

        /// <summary>The places a new level may go, the default first.</summary>
        IReadOnlyList<string> Places();

        /// <summary>Makes the level named <paramref name="name"/> in <paramref name="place"/>: its scene and data, and
        /// its row in <see cref="levels"/>. Null, with why, when it makes nothing. May open the new scene; the caller has
        /// already offered to save what was open.</summary>
        LevelDef Create(string name, string place, out string report);

        /// <summary>The mission tree whose Level node this level belongs under; null when it is in no episode.</summary>
        StateTreeAsset EpisodeOf(LevelContent level);

        /// <summary>The tree the level itself runs; null while it has none.</summary>
        StateTreeAsset SessionOf(LevelContent level);

        /// <summary>Makes what is missing of the level's episode entry and its tree, and nothing that exists; a line
        /// per thing made.</summary>
        List<string> Connect(LevelContent level);
    }

    /// <summary>A level's two connections: its Level node in the episode, and the tree it runs.</summary>
    public sealed class LevelLinks
    {
        /// <summary>The level's catalogue row, or null.</summary>
        public LevelDef row;

        /// <summary>The mission tree the level belongs under; null when it is in no episode.</summary>
        public StateTreeAsset episode;

        /// <summary>Its Level node there, or null while missing.</summary>
        public StateTreeNodeAsset episodeNode;

        /// <summary>The level's own tree, or null while missing.</summary>
        public StateTreeAsset session;

        public bool inEpisode => episode != null;

        /// <summary>Nothing to make: in no episode, or both connections there.</summary>
        public bool complete => episode == null || (episodeNode != null && session != null);

        /// <summary>The links as the registered setup sees them; empty when none is registered.</summary>
        public static LevelLinks Of(LevelContent level)
        {
            var links = new LevelLinks();
            ILevelSetup setup = LevelSetups.current;
            if (setup == null || level == null)
                return links;
            links.row = RowOf(setup.levels, level);
            links.episode = setup.EpisodeOf(level);
            links.episodeNode = LevelNode(links.episode, level, links.row != null ? links.row.name : null);
            links.session = setup.SessionOf(level);
            return links;
        }

        /// <summary>The catalogue's row whose content is <paramref name="level"/>.</summary>
        public static LevelDef RowOf(LevelRegistry levels, LevelContent level)
        {
            for (int i = 0; levels != null && level != null && i < levels.entries.Count; i++)
            {
                if (levels.entries[i] != null && levels.entries[i].content == level)
                    return levels.entries[i];
            }
            return null;
        }

        /// <summary>The node of <paramref name="episode"/> that is this level: an <see cref="IStateTreeLevel"/> context
        /// naming its row, or carrying its manifest.</summary>
        public static StateTreeNodeAsset LevelNode(StateTreeAsset episode, LevelContent level, string rowName)
        {
            if (episode == null || level == null)
                return null;
            foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(episode))
            {
                foreach (StateTreeTaskAsset task in node.tasks)
                {
                    if (task is not IStateTreeLevel context)
                        continue;
                    if (!string.IsNullOrEmpty(rowName) && context.levelName == rowName)
                        return node;
                    if (level.objects != null && Carries(task, level.objects))
                        return node;
                }
            }
            return null;
        }

        private static bool Carries(StateTreeTaskAsset context, LevelObjectRegistry manifest)
        {
            foreach (System.Reflection.FieldInfo field in context.GetType().GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (field.FieldType == typeof(LevelObjectRegistry) && (Object)field.GetValue(context) == manifest)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The registered <see cref="ILevelSetup"/>, and the flows the package runs through it.</summary>
    public static class LevelSetups
    {
        private static readonly List<ILevelSetup> s_Setups = new List<ILevelSetup>();

        public static void Register(ILevelSetup setup)
        {
            if (setup != null && !s_Setups.Contains(setup))
                s_Setups.Add(setup);
        }

        public static void Unregister(ILevelSetup setup) => s_Setups.Remove(setup);

        /// <summary>The last registered — a test's stand-in over the game's.</summary>
        public static ILevelSetup current => s_Setups.Count > 0 ? s_Setups[s_Setups.Count - 1] : null;

        /// <summary>Makes what the level is missing, once: nothing when no setup is registered, the level is in no
        /// episode, or both connections exist. The lines say what was made.</summary>
        public static List<string> ConnectIfMissing(LevelContent level)
        {
            ILevelSetup setup = current;
            if (setup == null || level == null || LevelLinks.Of(level).complete)
                return new List<string>();
            return setup.Connect(level) ?? new List<string>();
        }

        /// <summary>The designer's New level: what is open may be saved first (Unity's own prompt), then the level is
        /// made, its scene opened and the Level Manifest shown.</summary>
        public static LevelDef CreateAndOpen(string name, string place, out string report)
        {
            ILevelSetup setup = current;
            report = "";
            if (setup == null)
            {
                report = "no level setup is registered";
                return null;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                report = "the open scene was kept: nothing made";
                return null;
            }
            LevelDef row = setup.Create(name, place, out report);
            string scene = row != null && row.content != null ? row.content.scenePath : null;
            if (string.IsNullOrEmpty(scene))
                return row;
            if (SceneManager.GetActiveScene().path != scene)
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            LevelManifestWindow.Open();
            return row;
        }

        /// <summary>The New level box, as its own small window.</summary>
        public static void Prompt(string place = null)
        {
            if (current == null)
                return;
            NewLevelPrompt.Show(place);
        }
    }
}

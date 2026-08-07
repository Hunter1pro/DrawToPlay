using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The additive level machinery (M16), placed on the persistent ROOT: only Root's scene
    /// never unloads; a level is an additively loaded scene carrying its own Level host and
    /// WorldService, so the registry of what exists dies with the level — no cleanup code,
    /// and no GameObject-lifecycle coupling between the rungs (the Level host chains to Root
    /// through the registry, not through parenting).
    ///
    /// The transition is the Unreal sequence under a veil: raise <see cref="LoadingKey"/> on
    /// the Root scope (the loading overlay is a Root overlay watching that key — no
    /// transition scene), unload the current level, load the next, seed the entry's
    /// parameters onto the fresh Level scope, announce <see cref="CurrentKey"/>, drop the
    /// veil. Nothing here decides WHICH level — that is the session tree's job through
    /// <see cref="LoadLevelTask"/>; this component only executes transitions.
    /// </summary>
    [AddComponentMenu("Draw To Play/Level Service")]
    public sealed class LevelService : StateTreeServiceBehaviour
    {
        /// <summary>Root-scope key holding the incoming level's label while a transition
        /// runs; absent otherwise. Views watch it; trees may too.</summary>
        public const string LoadingKey = "level:loading";

        /// <summary>Root-scope key naming the level currently up (the entry's name — the
        /// wiring-facing string, as everywhere).</summary>
        public const string CurrentKey = "level:current";

        public LevelDef current { get; private set; }

        public bool isLoading { get; private set; }

        private Scene m_LoadedScene;

        /// <summary>Run one transition. One at a time: a second call while loading fails
        /// fast — the session tree's states make overlapping requests unrepresentable, and a
        /// queue here would hide a wiring mistake.</summary>
        public void Load(LevelDef level, Action<bool> done)
        {
            if (level == null || string.IsNullOrEmpty(level.scenePath))
            {
                Debug.LogError("[LevelService] Load called with no level / empty scenePath.",
                    this);
                done?.Invoke(false);
                return;
            }
            if (isLoading)
            {
                Debug.LogError($"[LevelService] already loading — '{level.name}' refused. One "
                    + "transition at a time; the session tree's states should make this "
                    + "unrepresentable.", this);
                done?.Invoke(false);
                return;
            }

            StartCoroutine(Transition(level, done));
        }

        private IEnumerator Transition(LevelDef level, Action<bool> done)
        {
            isLoading = true;
            WriteRootKey(LoadingKey, level.Label);

            if (m_LoadedScene.IsValid() && m_LoadedScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(m_LoadedScene);
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            current = null;
            ClearRootKey(CurrentKey);

            AsyncOperation load = SceneManager.LoadSceneAsync(level.scenePath,
                LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"[LevelService] scene '{level.scenePath}' cannot load — is it "
                    + "in Build Settings?", this);
                ClearRootKey(LoadingKey);
                isLoading = false;
                done?.Invoke(false);
                yield break;
            }
            while (!load.isDone)
                yield return null;

            m_LoadedScene = SceneManager.GetSceneByPath(level.scenePath);
            if (m_LoadedScene.IsValid())
                SceneManager.SetActiveScene(m_LoadedScene);

            SeedLevelParameters(level);

            current = level;
            WriteRootKey(CurrentKey, level.name);
            ClearRootKey(LoadingKey);
            isLoading = false;
            done?.Invoke(true);
        }

        /// <summary>The entry's parameters land on the LEVEL host's blackboard before
        /// anything ticks — same boxing rules as every parameter surface (String as string,
        /// Bool as 1/0 float, the rest as float), so the library atoms read them unchanged.
        /// A level scene without a Level host is a wiring error worth saying once.</summary>
        private void SeedLevelParameters(LevelDef level)
        {
            StateTreeContextHost levelHost = FindLevelHost(m_LoadedScene);
            if (levelHost == null)
            {
                Debug.LogWarning($"[LevelService] scene '{level.scenePath}' has no Level "
                    + "context host — entry parameters have nowhere to land and nothing "
                    + "scopes this level.", this);
                return;
            }

            var parameters = level.parameters;
            for (int i = 0; parameters != null && i < parameters.Count; i++)
            {
                GraphTaskParameter row = parameters[i];
                if (row == null || string.IsNullOrEmpty(row.name))
                    continue;
                object boxed;
                switch (row.kind)
                {
                    case GraphTaskParameterKind.String:
                        boxed = row.stringValue ?? string.Empty;
                        break;
                    case GraphTaskParameterKind.Bool:
                        boxed = row.floatValue != 0f ? 1f : 0f;
                        break;
                    default:
                        boxed = row.floatValue;
                        break;
                }
                levelHost.Context.blackboard[row.name] = boxed;
            }
        }

        private static StateTreeContextHost FindLevelHost(Scene scene)
        {
            if (!scene.IsValid())
                return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var host = roots[i].GetComponentInChildren<StateTreeContextHost>(true);
                if (host != null && host.kind == StateTreeContextKind.Level)
                    return host;
            }
            return null;
        }

        private void WriteRootKey(string key, string value)
        {
            if (connectedTo != null)
                connectedTo.Context.blackboard[key] = value ?? string.Empty;
        }

        private void ClearRootKey(string key)
        {
            if (connectedTo != null)
                connectedTo.Context.blackboard.Remove(key);
        }
    }
}

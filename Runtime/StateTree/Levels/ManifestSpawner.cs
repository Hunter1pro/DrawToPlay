using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE LEVEL'S ROWS BECOME ITS THINGS. Each manifest row names a kind; the kind's def owns
    /// the body; <see cref="ServiceBodyFactory"/> builds it where the row says. Trees start on
    /// FRAME TWO: a citizen announces itself on enable and the world adopts it next frame, so a
    /// tree started the same frame asks about an object the world has not adopted yet.
    ///
    /// A game with MEMORY (what was killed, what walked, what was dropped) derives and answers
    /// the three questions — should this row spawn, where is it now, what else does the level
    /// hold — without touching the spawning itself.
    ///
    /// THE LEVEL'S OWN TREE IS HELD THE SAME WAY. A level host on this object with
    /// <c>autoStart</c> off is started with the bodies, on frame two — its first state asks
    /// about the citizens the rows just became, and cannot before they are adopted.
    /// </summary>
    [AddComponentMenu("Draw To Play/Levels/Manifest Spawner")]
    public class ManifestSpawner : MonoBehaviour
    {
        [Tooltip("The level whose manifest this builds. Its rows become the things in it.")]
        public LevelContent level;

        [Tooltip("The project's spawnable kinds — where a row's kind finds its def.")]
        public LevelObjectKindRegistry kinds;

        private bool m_Spawned;
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();
        private readonly List<WorldObjectBehaviour> m_Citizens = new List<WorldObjectBehaviour>();

        /// <summary>Hosts spawned this pass, started on frame two.</summary>
        protected List<StateTreeContextHost> hosts => m_Hosts;

        private void Update()
        {
            if (m_Spawned)
            {
                StartSpawnedTrees();
                return;
            }
            m_Spawned = true;
            if (level == null || level.objects == null)
            {
                Debug.LogWarning("[ManifestSpawner] no level manifest — the level will be "
                    + "empty. Wire the level's LevelContent.", this);
                return;
            }
            OnBeforeRows();
            List<LevelObjectDef> rows = level.objects.entries;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && !ShouldSpawn(rows[i]))
                    continue;
                Spawn(rows[i]);
            }
            OnRowsSpawned();
            HoldOwnHost();
        }

        /// <summary>The level's own host, if it waits to be started — held with the rows'
        /// bodies rather than started here, so it runs once the world knows them.</summary>
        private void HoldOwnHost()
        {
            var own = GetComponent<StateTreeContextHost>();
            if (own != null && !own.autoStart && own.tree != null && !own.isRunning)
                m_Hosts.Add(own);
        }

        /// <summary>Once, before the rows — a place to find the services the answers need.</summary>
        protected virtual void OnBeforeRows()
        {
        }

        /// <summary>Whether an authored row still exists — false for what the player finished.</summary>
        protected virtual bool ShouldSpawn(LevelObjectDef row) => true;

        /// <summary>Where the row IS — its authored place unless something walked it elsewhere.</summary>
        protected virtual Vector2 GroundOf(LevelObjectDef row) => row.position;

        /// <summary>After the rows — what the level GAINED since it was authored goes here, so a
        /// drop always lands on top of the level rather than being overwritten by it.</summary>
        protected virtual void OnRowsSpawned()
        {
        }

        private void StartSpawnedTrees()
        {
            if (m_Hosts.Count == 0)
                return;
            // Announce everything again first: an OnEnable attempt can run before the world
            // service is reachable, and RegisterToWorld is idempotent by design.
            for (int i = 0; i < m_Citizens.Count; i++)
            {
                if (m_Citizens[i] != null)
                    m_Citizens[i].RegisterToWorld();
            }
            m_Citizens.Clear();
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                StateTreeContextHost host = m_Hosts[i];
                if (host != null && host.tree != null && !host.isRunning)
                    host.StartTree();
            }
            m_Hosts.Clear();
        }

        private void Spawn(LevelObjectDef row)
        {
            if (row == null)
                return;
            string kindName = row.kind.entryName;
            LevelObjectKindDef kind = kinds != null
                ? kinds.FindByName(kindName) as LevelObjectKindDef
                : null;
            if (kind == null)
            {
                Debug.LogError($"[ManifestSpawner] row '{row.name}' is kind '{kindName}', "
                    + "which the project's kind registry has no row for.", this);
                return;
            }
            // THE DEF OWNS THE BODY: this spawner's whole job is the level's business — where
            // the row goes and what has happened to it since it was authored.
            ServiceDef definition = kind.service;
            if (definition != null && definition.body.IsThing)
            {
                GameObject spawned = ServiceBodyFactory.Build(definition, row, transform,
                    level.objects.ToWorld(GroundOf(row), definition.body.height),
                    level.objects.Facing(row.facing), m_Hosts);
                if (spawned != null)
                    m_Citizens.AddRange(spawned.GetComponentsInChildren<WorldObjectBehaviour>(true));
                return;
            }
            Debug.LogError($"[ManifestSpawner] kind '{kindName}' has no def that owns a body, "
                + $"so row '{row.name}' cannot be built. Give the kind a Service Def with a "
                + "prefab in its Body.", this);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The world registry (brief §3.3): every interactive object, findable by TAG or by stable
    /// id from any tree or graph — and the first REAL service atom on the M8 spine: put it on
    /// the Root object and it is global by placement, which is the §3.3 default. It holds no
    /// game rules (the §3.7 boundary): registration bookkeeping, indexed lookup, and logs —
    /// what happens when something is found is the trees' business.
    ///
    /// DEEP LOGS FROM THE FIRST STAGE, by explicit design decision: every registration and
    /// every query appends one line to a capped ring the editor tooling (and the tests) can
    /// read, whether or not <see cref="logToConsole"/> mirrors it — debugging is reading what
    /// the world was asked, not adding printfs later. The ring is small and always on because
    /// the day it is needed is never the day it was enabled.
    ///
    /// Adoption is ORDER-FREE: objects that enabled first are swept up when the service
    /// enables (<see cref="AdoptStrays"/>); objects that enable later self-register; both are
    /// idempotent. An id collision keeps the LAST registration and logs the fact — a warning
    /// with two names in it, because a silent overwrite of an identity is a save-corruption
    /// seed.
    /// </summary>
    public sealed class WorldService : StateTreeServiceBehaviour
    {
        /// <summary>Mirror every log line to the Unity console. The ring records regardless.</summary>
        public bool logToConsole;

        public int logCapacity = 256;

        private readonly List<WorldObjectBehaviour> m_Objects = new List<WorldObjectBehaviour>();
        private readonly Dictionary<string, List<WorldObjectBehaviour>> m_ByTag =
            new Dictionary<string, List<WorldObjectBehaviour>>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorldObjectBehaviour> m_ById =
            new Dictionary<string, WorldObjectBehaviour>(StringComparer.Ordinal);
        private readonly Dictionary<GameObject, WorldObjectBehaviour> m_ByGameObject =
            new Dictionary<GameObject, WorldObjectBehaviour>();
        private readonly List<string> m_Log = new List<string>();

        /// <summary>The deep log, oldest first — what the world was told and asked.</summary>
        public IReadOnlyList<string> recentLog => m_Log;

        public int registeredCount => m_Objects.Count;

        /// <summary>Every live citizen, first-registered order — the sweep surface for a
        /// service that adopts a KIND of object (a unit setup, a save system).</summary>
        public IReadOnlyList<WorldObjectBehaviour> allObjects => m_Objects;

        /// <summary>A citizen ARRIVED (registration, or a facet exposed on one already
        /// registered — see <see cref="Announce"/>). This is how "drag it into the scene"
        /// becomes setup with no manual call: the object self-registers, the interested
        /// service listens. Listeners MUST be idempotent — the same citizen can be announced
        /// more than once.</summary>
        public event Action<WorldObjectBehaviour> citizenAdded;

        /// <summary>A citizen LEFT (disable, destroy, level unload). The interested service
        /// forgets it here.</summary>
        public event Action<WorldObjectBehaviour> citizenRemoved;

        protected override void OnEnable()
        {
            base.OnEnable();
            AdoptStrays();
        }

        /// <summary>The base retry connects to the host after every OnEnable has run; the
        /// second sweep catches citizens whose own quiet first attempt ran before this service
        /// was reachable.</summary>
        protected override void Start()
        {
            base.Start();
            AdoptStrays();
        }

        // --- registration ---------------------------------------------------------------

        public void Register(WorldObjectBehaviour obj)
        {
            if (obj == null || m_Objects.Contains(obj))
                return;

            m_Objects.Add(obj);
            for (int i = 0; i < obj.tags.Count; i++)
            {
                string tag = obj.tags[i];
                if (string.IsNullOrEmpty(tag))
                    continue;
                if (!m_ByTag.TryGetValue(tag, out List<WorldObjectBehaviour> bucket))
                {
                    bucket = new List<WorldObjectBehaviour>();
                    m_ByTag.Add(tag, bucket);
                }
                bucket.Add(obj);
            }

            if (!string.IsNullOrEmpty(obj.stableId))
            {
                if (m_ById.TryGetValue(obj.stableId, out WorldObjectBehaviour holder)
                    && holder != null && !ReferenceEquals(holder, obj))
                {
                    Emit("id collision '" + obj.stableId + "': '" + holder.name + "' replaced by '"
                        + obj.name + "'", true);
                }
                m_ById[obj.stableId] = obj;
            }

            m_ByGameObject[obj.gameObject] = obj;
            obj.MarkRegistered(this);
            Emit("register '" + obj.name + "' id=" + obj.stableId + " tags=[" + Join(obj.tags)
                + "]", false);
            citizenAdded?.Invoke(obj);
        }

        /// <summary>Re-announce a citizen that is already registered — called by
        /// <see cref="WorldObjectBehaviour.Expose"/> when a facet lands after registration,
        /// so listeners keyed on the TYPED object never miss it to enable-order.</summary>
        internal void Announce(WorldObjectBehaviour obj)
        {
            if (obj != null && m_Objects.Contains(obj))
                citizenAdded?.Invoke(obj);
        }

        public void Unregister(WorldObjectBehaviour obj)
        {
            if (obj == null || !m_Objects.Remove(obj))
                return;

            for (int i = 0; i < obj.tags.Count; i++)
            {
                if (!string.IsNullOrEmpty(obj.tags[i])
                    && m_ByTag.TryGetValue(obj.tags[i], out List<WorldObjectBehaviour> bucket))
                    bucket.Remove(obj);
            }
            if (!string.IsNullOrEmpty(obj.stableId)
                && m_ById.TryGetValue(obj.stableId, out WorldObjectBehaviour held)
                && ReferenceEquals(held, obj))
                m_ById.Remove(obj.stableId);
            if (obj.gameObject != null
                && m_ByGameObject.TryGetValue(obj.gameObject, out WorldObjectBehaviour mapped)
                && ReferenceEquals(mapped, obj))
                m_ByGameObject.Remove(obj.gameObject);

            Emit("unregister '" + obj.name + "' id=" + obj.stableId, false);
            citizenRemoved?.Invoke(obj);
        }

        /// <summary>Sweep the scene for citizens that enabled before this service existed —
        /// the other half of order-free adoption. Public so tests and a manual re-sync can do
        /// what OnEnable does.</summary>
        public void AdoptStrays()
        {
            WorldObjectBehaviour[] strays =
                UnityEngine.Object.FindObjectsByType<WorldObjectBehaviour>(
                    FindObjectsInactive.Exclude);
            for (int i = 0; i < strays.Length; i++)
            {
                if (strays[i].registeredWith == null)
                    Register(strays[i]);
            }
        }

        // --- queries --------------------------------------------------------------------

        public WorldObjectBehaviour FindById(string id)
        {
            if (string.IsNullOrEmpty(id) || !m_ById.TryGetValue(id, out WorldObjectBehaviour found)
                || found == null)
                return null;
            return found;
        }

        /// <summary>Nearest LIVE object carrying the tag, by squared distance from
        /// <paramref name="from"/>. <paramref name="maxDistance"/> zero-or-less means
        /// unlimited. Every query is logged with its answer — the deep-log rule.</summary>
        public WorldObjectBehaviour FindNearest(string tag, Vector3 from, float maxDistance = 0f)
        {
            WorldObjectBehaviour best = null;
            float bestSqr = float.PositiveInfinity;
            float limitSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;

            if (!string.IsNullOrEmpty(tag)
                && m_ByTag.TryGetValue(tag, out List<WorldObjectBehaviour> bucket))
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    WorldObjectBehaviour candidate = bucket[i];
                    if (candidate == null)
                        continue;
                    float sqr = (candidate.transform.position - from).sqrMagnitude;
                    if (sqr <= limitSqr && sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = candidate;
                    }
                }
            }

            Emit(best != null
                ? "query nearest '" + tag + "' -> '" + best.name + "'"
                : "query nearest '" + tag + "' -> none", false);
            return best;
        }

        /// <summary>Append every live object carrying the tag; returns how many. First-registered
        /// order, which is stable across calls.</summary>
        public int CollectByTag(string tag, List<WorldObjectBehaviour> into)
        {
            int count = 0;
            if (!string.IsNullOrEmpty(tag)
                && m_ByTag.TryGetValue(tag, out List<WorldObjectBehaviour> bucket))
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (bucket[i] == null)
                        continue;
                    ++count;
                    into?.Add(bucket[i]);
                }
            }

            Emit("query collect '" + tag + "' -> " + count, false);
            return count;
        }

        /// <summary>The TYPED scripted object behind a GameObject the world knows — a
        /// dictionary hit plus a facet scan, no GetComponent. This is the per-tick lookup
        /// tasks use for "who is this owner / this target?", so like <see cref="HasTag"/> it
        /// stays out of the log.</summary>
        public T FacetOf<T>(GameObject go) where T : class
        {
            if (go == null
                || !m_ByGameObject.TryGetValue(go, out WorldObjectBehaviour citizen)
                || citizen == null)
                return null;
            return citizen.As<T>();
        }

        /// <summary>Existence without a log line: this is the per-tick condition's question,
        /// and a condition polling every frame would flood the ring with the least
        /// interesting entry it can hold.</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)
                || !m_ByTag.TryGetValue(tag, out List<WorldObjectBehaviour> bucket))
                return false;
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i] != null)
                    return true;
            }
            return false;
        }

        // --- the deep log ---------------------------------------------------------------

        private void Emit(string line, bool asWarning)
        {
            m_Log.Add(line);
            int cap = logCapacity > 0 ? logCapacity : 1;
            while (m_Log.Count > cap)
                m_Log.RemoveAt(0);

            if (asWarning)
                Debug.LogWarning("[World] " + line, this);
            else if (logToConsole)
                Debug.Log("[World] " + line, this);
        }

        private static string Join(List<string> parts)
        {
            if (parts == null || parts.Count == 0)
                return "";
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }
    }
}

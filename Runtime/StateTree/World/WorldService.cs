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
    public sealed class WorldService : StateTreeService
    {

        /// <summary>Built by its scope's installer (M33) — the level's index of who is here.
        /// It adopts whatever registered before it existed, which is the same sweep its
        /// OnEnable and Start used to do twice.</summary>
        public WorldService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
            AdoptStrays();
        }

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


        /// <summary>The second sweep, once the world is assembled: a citizen that registered
        /// before this service existed is adopted here rather than lost.</summary>
        protected override void OnStarted()
        {
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

        /// <summary>Append every live BODY carrying the tag; returns how many. First-registered
        /// order, which is stable across calls.
        ///
        /// ONE ENTRY PER BODY (M35.6). A composed object is several citizens on one transform
        /// — a raider is its character and its ability host — and the factory tags every one of
        /// them, so the bucket holds the same body twice. Every consumer of this list wants
        /// bodies: the bench counted two stations, the cannon's broadside saw four raiders on a
        /// two-raider sea, and the world index called the player "not a singular tag" at every
        /// startup. The facet a caller needs is a GetComponent away on the body it gets.</summary>
        public int CollectByTag(string tag, List<WorldObjectBehaviour> into)
        {
            int count = 0;
            if (!string.IsNullOrEmpty(tag)
                && m_ByTag.TryGetValue(tag, out List<WorldObjectBehaviour> bucket))
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (bucket[i] == null || IsFacetOfEarlier(bucket, i))
                        continue;
                    ++count;
                    into?.Add(bucket[i]);
                }
            }

            Emit("query collect '" + tag + "' -> " + count, false);
            return count;
        }

        /// <summary>True when an earlier live entry in the bucket sits on the same GameObject —
        /// this one is a second facet of a body the list already has.</summary>
        private static bool IsFacetOfEarlier(List<WorldObjectBehaviour> bucket, int index)
        {
            GameObject body = bucket[index].gameObject;
            for (int i = 0; i < index; i++)
            {
                if (bucket[i] != null && bucket[i].gameObject == body)
                    return true;
            }
            return false;
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

            T found = citizen.As<T>();
            if (found != null)
                return found;

            // See SiblingAs — the miss path, and only the miss path.
            var siblings = SiblingsOf(go);
            for (int i = 0; i < siblings.Length; i++)
            {
                if (ReferenceEquals(siblings[i], citizen) || siblings[i] == null)
                    continue;
                found = siblings[i].As<T>();
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>The reflection-driven twin, for the [InjectOwner] field injector.</summary>
        public object FacetOf(Type type, GameObject go)
        {
            if (type == null || go == null
                || !m_ByGameObject.TryGetValue(go, out WorldObjectBehaviour citizen)
                || citizen == null)
                return null;

            object found = citizen.As(type);
            if (found != null)
                return found;

            var siblings = SiblingsOf(go);
            for (int i = 0; i < siblings.Length; i++)
            {
                if (ReferenceEquals(siblings[i], citizen) || siblings[i] == null)
                    continue;
                found = siblings[i].As(type);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// THE OTHER CITIZENS ON ONE GAME OBJECT — the answer to a question the by-GameObject
        /// index cannot give alone.
        ///
        /// <see cref="Register"/> maps a GameObject to ONE citizen, last registration winning, and
        /// for almost everything that is right: an object is a thing, and the thing answers for
        /// itself. But a composed object is legitimately more than one citizen on one transform —
        /// M21's NPC is an OutpostCharacter (a body that animates and moves) AND an OutpostNpc (a
        /// person with a conversation), registered separately because each is its own concern. The
        /// map keeps whichever enabled last, so "is this owner an OutpostCharacter?" answered NO
        /// for an object that plainly is one, and the tree that asked refused to run — an NPC
        /// standing still with one line in the console about a design requirement.
        ///
        /// So a miss consults the object's other citizens before giving up. It costs a
        /// GetComponents ONLY when the mapped citizen cannot answer, which for a single-citizen
        /// object — every other object in both demos — never happens. The dictionary hit is still
        /// the whole cost of the common path, which is what the per-tick callers need.
        ///
        /// The alternative was to make one of the two components stop being a citizen and become a
        /// facet of the other (<see cref="WorldObjectBehaviour.Expose"/>). That is a fine shape
        /// when a type is designed for it, but it makes composition an ORDERING problem — who
        /// exposes whom, and what happens when only one of them is present — and it would leave
        /// this trap armed for the next object that is honestly two things.
        /// </summary>
        /// <param name="go">The object being asked about.</param>
        /// <returns>Every citizen component on it.</returns>
        private static WorldObjectBehaviour[] SiblingsOf(GameObject go)
        {
            return go.GetComponents<WorldObjectBehaviour>();
        }

        /// <summary>
        /// THE object carrying a tag — for the ones a level has exactly one of: the player,
        /// the exit, the boss. A caller that knows the thing is unique should not be asking
        /// "which is nearest?" and paying a sweep to be told the only answer; it asks the
        /// registry, which already knows. Log-free, like the other per-tick questions.
        ///
        /// More than one carrier means the tag is not the singular thing the caller believes
        /// it is — that is a wiring error worth ONE line, and the first is returned so
        /// behaviour degrades to the sweep's answer rather than stopping.
        /// </summary>
        public WorldObjectBehaviour FindKnown(string tag)
        {
            if (string.IsNullOrEmpty(tag)
                || !m_ByTag.TryGetValue(tag, out List<WorldObjectBehaviour> bucket))
                return null;

            WorldObjectBehaviour known = null;
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i] == null)
                    continue;
                if (known == null)
                {
                    known = bucket[i];
                    continue;
                }
                // A second FACET of the same body is not a second carrier (M35.6).
                if (bucket[i].gameObject == known.gameObject)
                    continue;
                if (m_AmbiguousKnown.Add(tag))
                {
                    Emit("known '" + tag + "' is carried by more than one object ('"
                        + known.name + "', '" + bucket[i].name + "') — it is not a singular "
                        + "tag; taking the first.", true);
                }
                break;
            }
            return known;
        }

        private readonly HashSet<string> m_AmbiguousKnown = new HashSet<string>(StringComparer.Ordinal);

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
                Debug.LogWarning("[World] " + line);
            else if (logToConsole)
                Debug.Log("[World] " + line);
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

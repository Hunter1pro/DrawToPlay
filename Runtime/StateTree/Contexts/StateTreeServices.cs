using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// FIELD-LEVEL service injection — the lean flavor of the M15 capability wire, for atoms
    /// whose only need is "give me the service":
    ///
    /// <code>[InjectService] private ArcadeProgressService progress;</code>
    ///
    /// The FRAMEWORK fills it before the task runs — the executor's StartTree pass for state
    /// tasks, the graph VM at per-activation copy creation for graph-hosted atoms — resolved
    /// through the owner's view of the spine (nearest host, else the unique Root; behaviours
    /// and Provide&lt;T&gt; recipes alike). The atom body just USES the field: no Resolve
    /// helper, no per-atom null guard — a spine that cannot provide the capability is ONE
    /// clear error at injection time, and the NullReference that follows names the real
    /// problem (wiring), not the atom.
    ///
    /// Private fields welcome (they never serialize, which is exactly right for a live
    /// reference). Use <see cref="StateTreeServiceRef{T}"/> instead when a task wants to
    /// EXPRESS the dependency on its authoring surface.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InjectServiceAttribute : Attribute
    {
    }

    /// <summary>
    /// A SCOPE injected as a plain field — the host flavor of <see cref="InjectServiceAttribute"/>,
    /// for atoms whose scope is fixed by their DESIGN (a screen lives on Root, a spawn beat on
    /// its Level):
    ///
    /// <code>[InjectHost] private StateTreeContextHost m_Scope;              // Root
    /// [InjectHost(StateTreeContextKind.Level)] private StateTreeContextHost m_Level;</code>
    ///
    /// The framework binds it per activation, both worlds. A spine without the scope is ONE
    /// clear error at bind time and the NullReference that follows names the wiring — the
    /// atom body carries NO null guard, exactly the [InjectService] rule.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InjectHostAttribute : Attribute
    {
        public InjectHostAttribute(StateTreeContextKind kind = StateTreeContextKind.Root,
            string scopeId = "", bool optional = false)
        {
            this.kind = kind;
            this.scopeId = scopeId;
            this.optional = optional;
        }

        public StateTreeContextKind kind { get; }

        public string scopeId { get; }

        /// <summary>A scope that legitimately COMES AND GOES — the spawned player between
        /// levels. Missing is not a wiring error, so the loud pass stays quiet about it;
        /// the field simply holds null until the scope exists (re-ask at point of use).</summary>
        public bool optional { get; }
    }

    /// <summary>
    /// The OWNER's typed world object as a plain field — the third wire flavor: an atom that
    /// acts ON its owner declares it once,
    ///
    /// <code>[InjectOwner] private RaiderUnit m_Unit;</code>
    ///
    /// bound at per-activation copy creation from the world registry
    /// (<see cref="WorldService.FacetOf{T}"/>). This is a DESIGN requirement, not runtime
    /// state: a graph of unit atoms mounted on something that is not a unit is a wiring
    /// error — ONE clear message and the WHOLE program refuses to run. There is no
    /// missing-owner flow, on purpose; atom bodies carry no guard.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InjectOwnerAttribute : Attribute
    {
    }

    /// <summary>The one injector both worlds call, for the wire flavors: [InjectService]
    /// fields (resolved by TYPE through the spine), [InjectHost] scopes, and [InjectOwner]
    /// owner objects (resolved through the world registry). Fields are discovered once per
    /// type (whole hierarchy, private included) and cached for the domain.</summary>
    public static class StateTreeServiceInjector
    {
        private static readonly Dictionary<Type, FieldInfo[]> s_Fields =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> s_HostFields =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> s_OwnerFields =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly HashSet<string> s_Reported = new HashSet<string>();

        /// <summary>Fill every [InjectService] field of <paramref name="target"/> from
        /// <paramref name="owner"/>'s spine. A field already holding a value (hand-set, or a
        /// previous pass) is left alone. Returns false when an [InjectOwner] requirement
        /// could not be met — the caller's cue to refuse the whole program.</summary>
        public static bool Inject(object target, GameObject owner)
        {
            return Inject(target, owner, false);
        }

        /// <summary>The two-pass flavor: <paramref name="quiet"/> = a settle attempt during
        /// scene load (services register in an order nobody owns), where an unresolved field
        /// is not yet a wiring error — the loud pass follows once the scene has settled.</summary>
        public static bool Inject(object target, GameObject owner, bool quiet)
        {
            return Inject(target, owner, quiet, out _);
        }

        /// <summary>
        /// The same, reporting whether anything was actually WIRED this pass (M32.6).
        ///
        /// A service needs to know that, and not because it is curious: the moment a
        /// collaborator arrives — or is replaced on a level swap — is the moment its
        /// subscriptions have to be made again. Without this signal every service invented its
        /// own way of noticing, which is how one subsystem came to hook things in OnEnable, in
        /// Update, and lazily inside a verb, three answers to one question.
        /// </summary>
        public static bool Inject(object target, GameObject owner, bool quiet, out bool filled)
        {
            filled = false;
            if (target == null)
                return true;

            BindHostRefs(target, owner, quiet, ref filled);

            FieldInfo[] fields = InjectedFields(target.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (Holds(field.GetValue(target)))
                    continue;

                object service = StateTreeContextHost.FindService(field.FieldType, owner);
                if (service == null)
                {
                    if (quiet)
                        continue;
                    string report = target.GetType().Name + "." + field.Name;
                    if (s_Reported.Add(report))
                    {
                        Debug.LogError($"[InjectService] no {field.FieldType.Name} reachable "
                            + $"from '{(owner != null ? owner.name : "(no owner)")}' for "
                            + $"{report} — the atom will throw where it uses it. Put the "
                            + "service on a context host in the spine.");
                    }
                    continue;
                }
                field.SetValue(target, service);
                filled = true;
            }

            return BindOwnerFields(target, owner, quiet, ref filled);
        }

        /// <summary>Bind every [InjectOwner] field to the owner's typed world object. False
        /// when any stays unbound (loud pass): by design there is no missing-owner flow, so
        /// the caller refuses the whole program.</summary>
        private static bool BindOwnerFields(object target, GameObject owner, bool quiet,
            ref bool filled)
        {
            FieldInfo[] fields = OwnerFields(target.GetType());
            var allBound = true;
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.GetValue(target) != null)
                    continue;

                WorldService world = StateTreeContextHost.FindService<WorldService>(owner);
                object facet = world != null ? world.FacetOf(field.FieldType, owner) : null;
                if (facet == null)
                {
                    if (quiet)
                        continue;
                    allBound = false;
                    string report = target.GetType().Name + "." + field.Name;
                    if (s_Reported.Add(report))
                    {
                        Debug.LogError($"[InjectOwner] "
                            + $"'{(owner != null ? owner.name : "(no owner)")}' is not a "
                            + $"{field.FieldType.Name} the world knows — {report} is a design "
                            + "requirement, so the program it lives in will not run.");
                    }
                    continue;
                }
                field.SetValue(target, facet);
                filled = true;
            }
            return allBound;
        }

        private static FieldInfo[] OwnerFields(Type type)
        {
            if (s_OwnerFields.TryGetValue(type, out FieldInfo[] cached))
                return cached;

            var collected = new List<FieldInfo>();
            for (Type walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                FieldInfo[] declared = walk.GetFields(BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                for (int i = 0; i < declared.Length; i++)
                {
                    if (Attribute.IsDefined(declared[i], typeof(InjectOwnerAttribute)))
                        collected.Add(declared[i]);
                }
            }

            cached = collected.ToArray();
            s_OwnerFields[type] = cached;
            return cached;
        }

        /// <summary>Bind every [InjectHost] field from the owner — the address lives on the
        /// attribute, because these scopes are design constants, not per-instance data.</summary>
        /// <summary>Whether a field actually HOLDS something. Unity's two nulls meet here:
        /// a destroyed host or service is null to every caller but still boxes a non-null
        /// reference — treating that corpse as "filled" would make every replaced scope
        /// (the spawned player between levels) stick stale forever. A dead Unity object is
        /// an empty slot, refillable.</summary>
        private static bool Holds(object value)
        {
            return value is UnityEngine.Object unityValue ? unityValue != null : value != null;
        }

        private static void BindHostRefs(object target, GameObject owner, bool quiet,
            ref bool filled)
        {
            FieldInfo[] fields = HostRefFields(target.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (Holds(field.GetValue(target)))
                    continue;

                var address = (InjectHostAttribute)Attribute.GetCustomAttribute(field,
                    typeof(InjectHostAttribute));
                StateTreeContextHost host = StateTreeContextHost.Resolve(owner,
                    address.kind, address.scopeId);
                if (host == null)
                {
                    if (quiet || address.optional)
                        continue;
                    string report = target.GetType().Name + "." + field.Name;
                    if (s_Reported.Add(report))
                    {
                        Debug.LogError($"[InjectHost] no '{address.kind}"
                            + (string.IsNullOrEmpty(address.scopeId)
                                ? "" : ":" + address.scopeId)
                            + $"' context reachable from "
                            + $"'{(owner != null ? owner.name : "(no owner)")}' for {report} "
                            + "— the atom will throw where it uses it.");
                    }
                    continue;
                }
                field.SetValue(target, host);
                filled = true;
            }
        }

        private static FieldInfo[] HostRefFields(Type type)
        {
            if (s_HostFields.TryGetValue(type, out FieldInfo[] cached))
                return cached;

            var collected = new List<FieldInfo>();
            for (Type walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                FieldInfo[] declared = walk.GetFields(BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                for (int i = 0; i < declared.Length; i++)
                {
                    if (declared[i].FieldType == typeof(StateTreeContextHost)
                        && Attribute.IsDefined(declared[i], typeof(InjectHostAttribute)))
                        collected.Add(declared[i]);
                }
            }

            cached = collected.ToArray();
            s_HostFields[type] = cached;
            return cached;
        }

        private static FieldInfo[] InjectedFields(Type type)
        {
            if (s_Fields.TryGetValue(type, out FieldInfo[] cached))
                return cached;

            var collected = new List<FieldInfo>();
            for (Type walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                FieldInfo[] declared = walk.GetFields(BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                for (int i = 0; i < declared.Length; i++)
                {
                    if (Attribute.IsDefined(declared[i], typeof(InjectServiceAttribute)))
                        collected.Add(declared[i]);
                }
            }

            cached = collected.ToArray();
            s_Fields[type] = cached;
            return cached;
        }
    }

    /// <summary>The executor's view of a typed service reference — what the StartTree
    /// injection pass talks to without knowing the generic argument, exactly like the
    /// registry-reference seam.</summary>
    public interface IStateTreeServiceRef
    {
        Type ServiceType { get; }

        void Bind(object service);
    }

    /// <summary>
    /// A task's typed CAPABILITY requirement (M15) — the third self-describing wire field,
    /// after entries and keys: <c>public StateTreeServiceRef&lt;IWorldQuery&gt; world;</c>
    /// declares what a task needs, and the executor injects it at StartTree from the
    /// OWNER's view of the spine — the same tree mounted under two Player hosts gets each
    /// player's own service, and the tree never knows. Nothing is serialized: which
    /// instance answers is a runtime fact of where the tree runs, never authored state.
    /// A spine that provides no such capability is one error and a null the task's own
    /// guard answers for.
    /// </summary>
    [Serializable]
    public sealed class StateTreeServiceRef<T> : IStateTreeServiceRef
        where T : class
    {
        [NonSerialized]
        private T m_Service;

        /// <summary>The live service, injected at StartTree. Null until then, or when the
        /// spine provides no <typeparamref name="T"/>.</summary>
        public T service => m_Service;

        Type IStateTreeServiceRef.ServiceType => typeof(T);

        void IStateTreeServiceRef.Bind(object service)
        {
            m_Service = service as T;
        }
    }
}

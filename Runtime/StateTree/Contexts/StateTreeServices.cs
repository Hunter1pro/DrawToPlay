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
    /// A SCOPE as a wire-field — the host twin of <see cref="StateTreeServiceRef{T}"/>:
    /// <c>kind</c> and <c>scopeId</c> are the authored address (Root, this Level, player
    /// "p2"), <see cref="host"/> is the live end, bound by the same injector that fills
    /// service fields — in both worlds, with no per-tick Resolve and no per-atom guard.
    /// An atom just reads <c>scope.host.Context.blackboard</c>.
    /// </summary>
    [Serializable]
    public sealed class StateTreeHostRef
    {
        public StateTreeContextKind kind = StateTreeContextKind.Root;

        /// <summary>Disambiguates sibling scopes ("p2"); empty = nearest/unique of the
        /// kind — the <see cref="StateTreeContextHost.Resolve"/> rule.</summary>
        public string scopeId = "";

        [NonSerialized]
        private StateTreeContextHost m_Host;

        /// <summary>The live host, framework-bound per activation. Null only when the spine
        /// has no such scope — reported once by the injector.</summary>
        public StateTreeContextHost host => m_Host;

        internal void Bind(StateTreeContextHost value)
        {
            m_Host = value;
        }
    }

    /// <summary>The one injector both worlds call, for both wire flavors: [InjectService]
    /// fields (resolved by TYPE through the spine) and <see cref="StateTreeHostRef"/> fields
    /// (resolved by KIND + id — the scope itself). Fields are discovered once per type
    /// (whole hierarchy, private included) and cached for the domain.</summary>
    public static class StateTreeServiceInjector
    {
        private static readonly Dictionary<Type, FieldInfo[]> s_Fields =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> s_HostFields =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly HashSet<string> s_Reported = new HashSet<string>();

        /// <summary>Fill every [InjectService] field of <paramref name="target"/> from
        /// <paramref name="owner"/>'s spine. A field already holding a value (hand-set, or a
        /// previous pass) is left alone.</summary>
        public static void Inject(object target, GameObject owner)
        {
            Inject(target, owner, false);
        }

        /// <summary>The two-pass flavor: <paramref name="quiet"/> = a settle attempt during
        /// scene load (services register in an order nobody owns), where an unresolved field
        /// is not yet a wiring error — the loud pass follows once the scene has settled.</summary>
        public static void Inject(object target, GameObject owner, bool quiet)
        {
            if (target == null)
                return;

            BindHostRefs(target, owner, quiet);

            FieldInfo[] fields = InjectedFields(target.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.GetValue(target) != null)
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
            }
        }

        /// <summary>Resolve every <see cref="StateTreeHostRef"/> field's scope from the
        /// owner — the address is the field's own authored kind + id, so nothing but the
        /// resolution is the framework's.</summary>
        private static void BindHostRefs(object target, GameObject owner, bool quiet)
        {
            FieldInfo[] fields = HostRefFields(target.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                if (!(fields[i].GetValue(target) is StateTreeHostRef reference))
                    continue;
                if (reference.host != null)
                    continue;

                StateTreeContextHost host = StateTreeContextHost.Resolve(owner,
                    reference.kind, reference.scopeId);
                if (host == null)
                {
                    if (quiet)
                        continue;
                    string report = target.GetType().Name + "." + fields[i].Name;
                    if (s_Reported.Add(report))
                    {
                        Debug.LogError($"[HostRef] no '{reference.kind}"
                            + (string.IsNullOrEmpty(reference.scopeId)
                                ? "" : ":" + reference.scopeId)
                            + $"' context reachable from "
                            + $"'{(owner != null ? owner.name : "(no owner)")}' for {report} "
                            + "— the atom will fail where it uses it.");
                    }
                    continue;
                }
                reference.Bind(host);
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
                    if (declared[i].FieldType == typeof(StateTreeHostRef))
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

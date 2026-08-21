using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE INSTALLER (M33) — one component where eleven used to be, and the place a scope's
    /// subsystems are made.
    ///
    /// A def already names its service type, its scope, the catalog it manages, the requests it
    /// serves and the screens it spawns. That is a recipe, so this reads the list and builds it:
    /// each service is constructed with the host it belongs to and the def it runs, registered
    /// under its own type, ticked by the host and disposed with it.
    ///
    /// ORDER IS DEPENDENCY ORDER, stated by the list rather than discovered by Unity: a service
    /// whose constructor asks the scope for another (the bag asking for the screen) needs that
    /// one installed first, and the list is where an author says so. This is the same sentence
    /// HT's entry point makes when it registers its systems before resolving them.
    ///
    /// It runs in Awake, before any host starts a tree, because a tree that ticks before its
    /// subsystems exist is the ordering bug this whole milestone is about.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Service Installer")]
    [DefaultExecutionOrder(-100)]
    public sealed class StateTreeServiceInstaller : MonoBehaviour
    {
        [Tooltip("The subsystems this scope runs, in the order they depend on each other. Each "
            + "def names its own service type; a row may tune the def's settings for this scope.")]
        public List<ServiceInstall> install = new List<ServiceInstall>();

        [Tooltip("Subsystems that have no def yet, by type name — the world index, the level "
            + "loader. A declaration is better and these should grow one; this is the honest "
            + "way to say a service exists before it has one.")]
        public List<string> undeclared = new List<string>();

        [Tooltip("The scope they belong to. Empty uses the host on this object.")]
        public StateTreeContextHost scope;

        private void Awake()
        {
            StateTreeContextHost host = Scope();
            if (host == null)
                return;

            for (int i = 0; i < install.Count; i++)
                Build(host, install[i]);
            for (int i = 0; i < undeclared.Count; i++)
                Build(host, null, undeclared[i]);
        }

        private void OnDestroy()
        {
            // IN REVERSE, because install order is dependency order: the screen ledger the
            // others were handed goes last.
            for (int i = m_Installed.Count - 1; i >= 0; i--)
                m_Installed[i]?.Dispose();
            m_Installed.Clear();
        }

        /// <summary>The subsystems this installer built, in install order.</summary>
        public IReadOnlyList<StateTreeSubsystem> installed => m_Installed;

        /// <summary>
        /// Build one subsystem now — the operation behind "install this def", usable while the
        /// game runs. Returns the handle that takes it back out.
        /// </summary>
        public StateTreeSubsystem Install(ServiceDef def)
        {
            return Install(new ServiceInstall(def));
        }

        /// <summary>Build one subsystem with this scope's tuning on top of the def's.</summary>
        public StateTreeSubsystem Install(ServiceInstall row)
        {
            StateTreeContextHost host = Scope();
            return host != null ? Build(host, row) : null;
        }

        /// <summary>Take one out: its screens hidden, its service forgotten and disposed.</summary>
        public bool Uninstall(ServiceDef def)
        {
            for (int i = 0; i < m_Installed.Count; i++)
            {
                if (m_Installed[i] == null || m_Installed[i].definition != def)
                    continue;
                m_Installed[i].Dispose();
                m_Installed.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Out and back in — one subsystem rebuilt while everything around it keeps
        /// running, which is the thing a per-scope container could not do.</summary>
        public StateTreeSubsystem Reinstall(ServiceDef def)
        {
            ServiceInstall row = InstalledRowFor(def) ?? RowFor(def) ?? new ServiceInstall(def);
            Uninstall(def);
            return Install(row);
        }

        /// <summary>The row a LIVE subsystem was built from — which may have been handed in at
        /// runtime rather than authored on this component.</summary>
        private ServiceInstall InstalledRowFor(ServiceDef def)
        {
            for (int i = 0; def != null && i < m_Installed.Count; i++)
            {
                if (m_Installed[i] != null && m_Installed[i].definition == def)
                    return m_Installed[i].row;
            }
            return null;
        }

        /// <summary>The authored row for a def, so a reinstall keeps this scope's tuning.</summary>
        public ServiceInstall RowFor(ServiceDef def)
        {
            for (int i = 0; def != null && i < install.Count; i++)
            {
                if (install[i] != null && install[i].def == def)
                    return install[i];
            }
            return null;
        }

        private StateTreeContextHost Scope()
        {
            StateTreeContextHost host = scope != null ? scope : GetComponent<StateTreeContextHost>();
            if (host == null)
            {
                Debug.LogError("[Install] '" + name + "' has no context host to install into — "
                    + "a subsystem belongs to a scope.", this);
            }
            return host;
        }

        private readonly List<StateTreeSubsystem> m_Installed = new List<StateTreeSubsystem>();

        private StateTreeSubsystem Build(StateTreeContextHost host, ServiceInstall row)
        {
            ServiceDef def = row != null ? row.def : null;
            StateTreeSubsystem built = Build(host, def, def != null ? def.serviceTypeName : "",
                row != null ? row.settings : null);
            if (built != null)
                built.row = row;
            return built;
        }

        private StateTreeSubsystem Build(StateTreeContextHost host, ServiceDef def,
            string typeName, ServiceSettingSet tuning = null)
        {
            if (def == null && string.IsNullOrEmpty(typeName))
                return null;

            Type type = Resolve(typeName);
            if (type == null)
            {
                Debug.LogError("[Install] '" + (def != null ? def.name : name) + "' names the "
                    + "service type '" + typeName + "', which no assembly has. Nothing serves "
                    + "its requests.", def);
                return null;
            }
            if (!typeof(StateTreeService).IsAssignableFrom(type))
            {
                Debug.LogError("[Install] '" + type.Name + "' is not a StateTreeService, so it "
                    + "cannot be installed from a def.", def);
                return null;
            }

            object instance;
            // THIS SCOPE'S TUNING (M36.3) travels through the scope for the length of one
            // construction: the contract every subsystem signs is (scope, def), and a third
            // parameter would tax every class for a layer most installs never use. The base
            // constructor takes it off the scope before the derived body runs — so that body
            // sees the final numbers, which is the whole promise of where settings land.
            host.tuning = tuning;
            try
            {
                // THE CONTRACT EVERY SUBSYSTEM SIGNS: (scope, def). Anything else it needs it
                // asks the scope for in its own constructor, where a missing collaborator is a
                // loud failure at install time rather than a null three frames into play.
                instance = Activator.CreateInstance(type, host, def);
            }
            catch (Exception failure)
            {
                Debug.LogError("[Install] '" + type.Name + "' could not be built: "
                    + (failure.InnerException ?? failure).Message, def);
                return null;
            }
            finally
            {
                host.tuning = null;
            }

            // UNDER ITS CLASS, AND UNDER EVERY CAPABILITY IT DECLARES (M36.4): a consumer that
            // asks for IBag gets whichever class the def named — which is the whole of what
            // "swap the implementation by changing one def field" means. Only this toolset's
            // own interfaces count; IDisposable and friends are not capabilities.
            host.Provide(type, instance);
            foreach (Type capability in Capabilities(type))
                host.Provide(capability, instance);
            var subsystem = new StateTreeSubsystem(host, def, instance as StateTreeService, type);
            m_Installed.Add(subsystem);
            return subsystem;
        }

        /// <summary>The interfaces a service type offers as capabilities — the ones declared in
        /// this toolset, so a swap is asked for by something the project named.</summary>
        public static IEnumerable<Type> Capabilities(Type serviceType)
        {
            Type[] interfaces = serviceType != null ? serviceType.GetInterfaces() : Type.EmptyTypes;
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type candidate = interfaces[i];
                if (candidate.Namespace != null
                    && candidate.Namespace.StartsWith("PowerOfFire.", StringComparison.Ordinal))
                    yield return candidate;
            }
        }

        private static Type Resolve(string typeName)
        {
            return ServiceDef.ResolveServiceType(typeName);
        }
    }
}

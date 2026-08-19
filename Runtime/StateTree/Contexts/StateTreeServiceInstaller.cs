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
            + "def names its own service type.")]
        public List<ServiceDef> install = new List<ServiceDef>();

        [Tooltip("Subsystems that have no def yet, by type name — the world index, the level "
            + "loader. A declaration is better and these should grow one; this is the honest "
            + "way to say a service exists before it has one.")]
        public List<string> undeclared = new List<string>();

        [Tooltip("The scope they belong to. Empty uses the host on this object.")]
        public StateTreeContextHost scope;

        private void Awake()
        {
            StateTreeContextHost host = scope != null ? scope : GetComponent<StateTreeContextHost>();
            if (host == null)
            {
                Debug.LogError("[Install] '" + name + "' has no context host to install into — "
                    + "a subsystem belongs to a scope.", this);
                return;
            }

            for (int i = 0; i < install.Count; i++)
                Build(host, install[i], install[i] != null ? install[i].serviceTypeName : "");
            for (int i = 0; i < undeclared.Count; i++)
                Build(host, null, undeclared[i]);
        }

        private void Build(StateTreeContextHost host, ServiceDef def, string typeName)
        {
            if (def == null && string.IsNullOrEmpty(typeName))
                return;

            Type type = Resolve(typeName);
            if (type == null)
            {
                Debug.LogError("[Install] '" + (def != null ? def.name : name) + "' names the "
                    + "service type '" + typeName + "', which no assembly has. Nothing serves "
                    + "its requests.", def);
                return;
            }
            if (!typeof(StateTreeService).IsAssignableFrom(type))
            {
                Debug.LogError("[Install] '" + type.Name + "' is not a StateTreeService, so it "
                    + "cannot be installed from a def.", def);
                return;
            }

            object instance;
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
                return;
            }

            host.Provide(type, instance);
        }

        private static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            Type direct = Type.GetType(typeName);
            if (direct != null)
                return direct;
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }
                for (int i = 0; i < types.Length; i++)
                {
                    if (types[i].Name == typeName || types[i].FullName == typeName)
                        return types[i];
                }
            }
            return null;
        }
    }
}

using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE SUBSYSTEM, WITH A LIFETIME OF ITS OWN (M34.5).
    ///
    /// HT builds a container for its resource system and disposes it whole; ours were built into
    /// the SCOPE's container, so a subsystem could only die when its scope did. That is fine
    /// until you want to swap an implementation, unload the craft rules with the shipyard that
    /// used them, or simply rebuild one thing while the game runs — all of which were refactors
    /// rather than operations.
    ///
    /// This is the handle that makes them operations: what was built, from which def, on which
    /// scope, and how to take it back out. Disposing it hides the screens the def declared,
    /// takes the service off the scope (so nothing resolves it any more), and disposes the
    /// service itself — the same three steps in the same order, every time.
    /// </summary>
    public sealed class StateTreeSubsystem : IDisposable
    {
        internal StateTreeSubsystem(StateTreeContextHost scope, ServiceDef definition,
            StateTreeService service, Type capability)
        {
            this.scope = scope;
            this.definition = definition;
            this.service = service;
            this.capability = capability;
        }

        /// <summary>Where it lives.</summary>
        public StateTreeContextHost scope { get; }

        /// <summary>What it was built from, or null for one installed by type alone.</summary>
        public ServiceDef definition { get; }

        /// <summary>The thing itself, until it is disposed.</summary>
        public StateTreeService service { get; private set; }

        /// <summary>The type it answers for.</summary>
        public Type capability { get; }

        /// <summary>True until it has been taken out.</summary>
        public bool installed => service != null;

        public void Dispose()
        {
            if (service == null)
                return;

            // THE SCREENS FIRST: a def that shows a panel owns that panel, and a subsystem
            // taken out while its screen is up leaves a skin talking to nothing.
            UiService ui = scope != null ? scope.GetService<UiService>() : null;
            for (int i = 0; definition != null && ui != null && i < definition.spawns.Count; i++)
            {
                var spawn = definition.spawns[i];
                if (spawn != null && !string.IsNullOrEmpty(spawn.entryName))
                    ui.Hide(spawn.entryName);
            }

            scope?.Forget(service);
            service.Dispose();
            service = null;
        }
    }
}

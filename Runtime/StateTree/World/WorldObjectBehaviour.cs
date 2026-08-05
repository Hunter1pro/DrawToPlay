using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Makes a GameObject a citizen of the world registry (brief §3.3): a set of TAGS any tree
    /// or graph can find it by, and a STABLE ID that outlives sessions — minted at edit time in
    /// OnValidate so it is serialized into the scene/prefab, which is the identity the save
    /// system (§3.4) will key on; a runtime-spawned copy mints its own on first enable.
    ///
    /// Registration goes through the spine (<see cref="StateTreeContextHost.FindService{T}"/>),
    /// and is deliberately forgiving about ORDER: an object that enables before the service
    /// exists just stays quiet — the service adopts every stray when IT enables — and an object
    /// that enables after registers itself. Both paths are idempotent, so scene load order is
    /// nobody's problem. Unregistration uses the CACHED service reference, because the moment
    /// it runs (level teardown) is exactly the moment resolution can no longer be trusted.
    ///
    /// The tag set is fixed while registered (v1): to change tags at runtime, unregister,
    /// edit, re-register — an explicit gesture, so the by-tag index never drifts.
    /// </summary>
    public sealed class WorldObjectBehaviour : MonoBehaviour
    {
        public List<string> tags = new List<string>();

        /// <summary>Stable identity for saves and cross-session references. Minted once in the
        /// editor (OnValidate) and kept forever; empty only on a runtime-spawned instance,
        /// which mints its own on first enable.</summary>
        public string stableId = "";

        private WorldService m_RegisteredWith;

        /// <summary>The service this object is currently registered with — null while the
        /// world has not adopted it (yet).</summary>
        public WorldService registeredWith => m_RegisteredWith;

        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void OnValidate()
        {
            EnsureStableId();
        }

        private void OnEnable()
        {
            EnsureStableId();
            RegisterToWorld();
        }

        /// <summary>Scene-load enable order is nobody's problem: the OnEnable attempt can run
        /// before the world service is reachable, so Start — after every OnEnable — retries.</summary>
        private void Start()
        {
            RegisterToWorld();
        }

        /// <summary>Mint the identity if there is none yet — idempotent, public like the other
        /// lifecycle mirrors. An authored object gets its id at edit time (OnValidate) and
        /// keeps it forever; this is the path a runtime-spawned copy takes instead.</summary>
        public void EnsureStableId()
        {
            if (string.IsNullOrEmpty(stableId))
                stableId = MintId();
        }

        private void OnDisable()
        {
            UnregisterFromWorld();
        }

        /// <summary>Public and idempotent, like every lifecycle mirror in this toolset: the
        /// EditMode tests and the service's adoption sweep call exactly what OnEnable calls.
        /// Quiet when no service is reachable — the service adopts strays when it arrives, and
        /// wiring errors surface on the QUERY side, where behavior actually notices.</summary>
        public void RegisterToWorld()
        {
            if (m_RegisteredWith != null)
                return;

            WorldService service = StateTreeContextHost.FindService<WorldService>(gameObject);
            if (service == null)
                return;

            service.Register(this);
            m_RegisteredWith = service;
        }

        public void UnregisterFromWorld()
        {
            if (m_RegisteredWith == null)
                return;
            m_RegisteredWith.Unregister(this);
            m_RegisteredWith = null;
        }

        /// <summary>Called by the service when it adopts a stray, so the object's cached
        /// reference (the one OnDisable uses) is set no matter which side made the
        /// connection.</summary>
        internal void MarkRegistered(WorldService service)
        {
            m_RegisteredWith = service;
        }

        /// <summary>Add a tag the object does not carry yet — through the documented
        /// re-register gesture when it is already in a registry, so the by-tag index never
        /// drifts from the tag list. The idempotent form code uses; authors just type tags in
        /// the inspector.</summary>
        public void EnsureTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || HasTag(tag))
                return;

            WorldService registry = m_RegisteredWith;
            if (registry != null)
                UnregisterFromWorld();
            tags.Add(tag);
            if (registry != null)
            {
                registry.Register(this);
                m_RegisteredWith = registry;
            }
        }

        /// <summary>Make <paramref name="go"/> a citizen carrying <paramref name="tag"/>,
        /// creating the component when it is absent — how a CODE feature (a HealthComponent,
        /// a future interactable) enrolls its object without asking the author to remember a
        /// second component. Registration still follows the normal order-free paths.</summary>
        public static WorldObjectBehaviour EnsureCitizen(GameObject go, string tag)
        {
            if (go == null)
                return null;

            WorldObjectBehaviour citizen = go.GetComponent<WorldObjectBehaviour>();
            if (citizen == null)
                citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.EnsureStableId();
            citizen.EnsureTag(tag);
            return citizen;
        }

        private static string MintId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }
}

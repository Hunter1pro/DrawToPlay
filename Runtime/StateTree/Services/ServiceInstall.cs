using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ROW OF AN INSTALLER (M36.3) — a def, and what THIS scope tunes it to.
    ///
    /// The third layer of a setting: the class says the default, the def says what the project
    /// tuned this kind to, and this row says what this install differs in — the same def on the
    /// ridge and in the yard at two reaches, without a second asset. Only what differs is stored.
    ///
    /// It converts from a bare def implicitly so the ordinary case — install this, no overrides —
    /// reads as it always did: <c>install.Add(def)</c>.
    /// </summary>
    [Serializable]
    public sealed class ServiceInstall
    {
        [Tooltip("The subsystem to build. Its class and its scope come from the def.")]
        public ServiceDef def;

        [Tooltip("What this install tunes differently from the def. Empty follows the def.")]
        public ServiceSettingSet settings = new ServiceSettingSet();

        public ServiceInstall()
        {
        }

        public ServiceInstall(ServiceDef def)
        {
            this.def = def;
        }

        public static implicit operator ServiceInstall(ServiceDef def)
        {
            return new ServiceInstall(def);
        }
    }
}

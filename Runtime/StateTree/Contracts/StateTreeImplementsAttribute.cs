using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ASK BY PROMISE, NOT BY CATALOG (M30.2b) — mark a string field with the contract whatever it
    /// names has to keep, and the field offers exactly the things that keep it.
    ///
    /// This is the difference a contract was for. A field typed against a registry can only say
    /// "one of THESE rows", so every new kind of damageable thing has to be added to the catalog
    /// the field happens to point at, and a field that wants two catalogs cannot be written at all.
    /// A field typed against a promise says what it actually needs, and anything that claims the
    /// promise — in any catalog this asset declares — is offered.
    ///
    /// IT STORES A ROW NAME, exactly as it did before, so nothing at runtime changes: the promise
    /// is an authoring constraint on which name may be typed there, and the lookup that reads it
    /// is the one that was already there.
    ///
    /// The neighbourhood rule holds: the offers come from the catalogs the inspected asset
    /// DECLARES. A field showing every implementer in the project would be the same undifferentiated
    /// list the row pickers were built to stop being.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class StateTreeImplementsAttribute : PropertyAttribute
    {
        /// <summary>The contract's row name — "damageable", "openable".</summary>
        public readonly string contractName;

        public StateTreeImplementsAttribute(string contractName)
        {
            this.contractName = contractName ?? "";
        }
    }
}

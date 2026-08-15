using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ZONE, ONE ASSET (reviewed: the container should hold fully customizable
    /// objectives) — a registry OF objectives: its entries ARE the stack, in order, and the
    /// standard registry dashboard is therefore the whole zone editor — add a row, fill its
    /// kind and fields, done. List the registries its rows pick from (dialogs, items) in
    /// dependsOn like any other registry. The zone catalog's thin <see cref="ZoneDef"/> row
    /// points here — that row is only the identity the placer picks and the world tags.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Zone", fileName = "Zone")]
    public sealed class ZoneAsset : StateTreeRegistry<ObjectiveDef>
    {
        [Tooltip("The zone's title on screen while its stack is asked.")]
        public string displayName = "";
    }
}

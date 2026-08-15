using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The screen's catalog — every screen, popup and widget as rows, panel order
    /// as data, parameters as each row's tunable surface. List the registries whose rows a
    /// view may pick (attributes for a bound bar) in dependsOn.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Ui Registry", fileName = "UiRegistry")]
    public sealed class UiRegistry : StateTreeRegistry<UiDef>
    {
    }
}

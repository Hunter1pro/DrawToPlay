using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The cutscene catalog — the M13 registry pair again: the entry class plus this line, and
    /// the dashboard, the tree-header Data list and every picker come from the base by
    /// reflection. Its <c>dependsOn</c> names nothing: a cutscene's script is a tree it picks
    /// directly, and its cast is tags rather than rows.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Cutscenes/Cutscene Registry",
        fileName = "CutsceneRegistry")]
    public sealed class CutsceneRegistry : StateTreeRegistry<CutsceneDef>
    {
    }
}

using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The cue catalog — what effects may show. Effects pick rows from here through
    /// their registry's dependsOn.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Cue Registry", fileName = "CueRegistry")]
    public sealed class CueRegistry : StateTreeRegistry<CueDef>
    {
    }
}

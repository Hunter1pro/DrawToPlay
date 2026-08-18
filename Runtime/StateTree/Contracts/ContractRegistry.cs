using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The vocabulary of promises — every contract a project speaks, in one catalog that
    /// other assets declare in Depends On when they want to speak it.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Contract Registry",
        fileName = "ContractRegistry")]
    public sealed class ContractRegistry : StateTreeRegistry<ContractDef>
    {
    }
}

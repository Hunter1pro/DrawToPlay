using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE CODE SEAM (M30.2). A C# interface marked with this IS the contract of the given name:
    /// any component implementing it keeps the promise, with no row authored and no def claiming
    /// anything.
    ///
    /// For the edge cases, deliberately — a promise that is easier to state in C# than in data
    /// ("anything that can be serialized into a save", "anything the camera may frame") should
    /// not have to be pushed through the authoring surface to be usable by it. The row remains
    /// the place a contract is DESCRIBED to authors; this is how code joins in.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, Inherited = false)]
    public sealed class StateTreeContractAttribute : Attribute
    {
        public readonly string contractName;

        public StateTreeContractAttribute(string contractName)
        {
            this.contractName = contractName;
        }
    }
}

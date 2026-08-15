using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The attribute catalog — what effects land on, what seeds start from, what a
    /// parameter can name. Effect registries list it in dependsOn.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Attribute Registry",
        fileName = "AttributeRegistry")]
    public sealed class AttributeRegistry : StateTreeRegistry<AttributeDef>
    {
    }
}

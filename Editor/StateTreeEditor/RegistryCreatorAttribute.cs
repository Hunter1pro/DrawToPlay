using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A REGISTRY THAT CAN MAKE ITS OWN ROWS. A row is often more than a row — a level is a
    /// scene, a content and a manifest; a dialog is a graph — and the place to make the whole
    /// thing is the registry's inspector. Put this on a <see cref="VisualElement"/> with a
    /// constructor <c>(TRegistry registry, Action changed)</c> and the registry editor shows
    /// it above the rows of every registry of that type. Found by type, from any assembly:
    /// a game offers a creator for its own registries without the package knowing them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RegistryCreatorAttribute : Attribute
    {
        public Type registryType { get; }

        public RegistryCreatorAttribute(Type registryType)
        {
            this.registryType = registryType;
        }
    }

    /// <summary>The creators, by registry type — read once per domain load.</summary>
    public static class RegistryCreators
    {
        private static Dictionary<Type, Type> s_ByRegistry;

        /// <summary>The creator panel for this registry, or null when none is offered.</summary>
        public static VisualElement For(StateTreeRegistryAsset registry, Action changed)
        {
            if (registry == null)
                return null;
            if (s_ByRegistry == null)
            {
                s_ByRegistry = new Dictionary<Type, Type>();
                foreach (Type panel in TypeCache.GetTypesWithAttribute<RegistryCreatorAttribute>())
                {
                    var attribute = (RegistryCreatorAttribute)Attribute.GetCustomAttribute(panel, typeof(RegistryCreatorAttribute));
                    if (attribute?.registryType != null && typeof(VisualElement).IsAssignableFrom(panel))
                        s_ByRegistry[attribute.registryType] = panel;
                }
            }
            for (Type type = registry.GetType(); type != null; type = type.BaseType)
            {
                if (!s_ByRegistry.TryGetValue(type, out Type panelType))
                    continue;
                try
                {
                    return Activator.CreateInstance(panelType, registry, changed) as VisualElement;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[RegistryCreator] " + panelType.Name + " could not be built for "
                        + registry.name + ": " + e.GetBaseException().Message);
                    return null;
                }
            }
            return null;
        }
    }
}

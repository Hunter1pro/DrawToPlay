using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>The core's own vocabulary, registered like any game's: a
    /// [ServiceRequestKey] field may hold any request a ServiceDef declares (§4g).</summary>
    [InitializeOnLoad]
    internal static class ServiceRequestOffers
    {
        static ServiceRequestOffers()
        {
            StateTreeFieldOffers.sources += Offer;
        }

        private static List<string> Offer(FieldInfo field)
        {
            if (field == null || field.FieldType != typeof(string)
                || !field.IsDefined(typeof(ServiceRequestKeyAttribute), true))
                return null;

            var choices = new List<string> { string.Empty };
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ServiceDef)))
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                for (int i = 0; def != null && i < def.requests.Count; i++)
                {
                    ServiceRequest row = def.requests[i];
                    if (row != null && !string.IsNullOrEmpty(row.key)
                        && !choices.Contains(row.key))
                        choices.Add(row.key);
                }
            }
            return choices.Count > 1 ? choices : null;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHICH KIND IS THIS PLACEMENT'S — answered from a cache, never from a project walk per
    /// repaint (editor rule 4). The placement panel's IMGUI drawer used to run
    /// <c>AssetDatabase.FindAssets</c> + <c>LoadAssetAtPath</c> in BOTH <c>OnGUI</c> and
    /// <c>GetPropertyHeight</c>, per row, per repaint: a twenty-row manifest cost ~100 project
    /// walks per scroll frame and scrolled at half a second a frame. The kind registries are
    /// listed once and forgotten on <see cref="EditorApplication.projectChanged"/>, the same
    /// precedent as <c>ServiceDef.ResolveServiceType</c>.
    /// </summary>
    internal static class LevelObjectKinds
    {
        private static List<LevelObjectKindRegistry> s_Registries;

        [InitializeOnLoadMethod]
        private static void ForgetOnProjectChange()
        {
            EditorApplication.projectChanged += Forget;
        }

        internal static void Forget()
        {
            s_Registries = null;
        }

        internal static IReadOnlyList<LevelObjectKindRegistry> Registries()
        {
            if (s_Registries != null)
                return s_Registries;
            s_Registries = new List<LevelObjectKindRegistry>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(LevelObjectKindRegistry)))
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (registry != null)
                    s_Registries.Add(registry);
            }
            return s_Registries;
        }

        /// <summary>The kind row by id, or by name when the id is empty; null when neither
        /// names one in any kind registry of the project.</summary>
        internal static LevelObjectKindDef Find(string id, string name)
        {
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name))
                return null;
            IReadOnlyList<LevelObjectKindRegistry> registries = Registries();
            for (int i = 0; i < registries.Count; i++)
            {
                LevelObjectKindRegistry registry = registries[i];
                if (registry == null)
                {
                    // A registry deleted since the listing — forget and look again.
                    Forget();
                    return Find(id, name);
                }
                var row = (string.IsNullOrEmpty(id)
                    ? registry.FindByName(name)
                    : registry.FindById(id)) as LevelObjectKindDef;
                if (row != null)
                    return row;
            }
            return null;
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>Making one — a document with the tick marker already on it, because a graph with
    /// no start is a graph that bakes to nothing and says so.</summary>
    internal static class TaskGraphAssetMenu
    {
        [MenuItem("Assets/Create/Draw To Play/Task Graph", false, 40)]
        internal static void Create()
        {
            var document = ScriptableObject.CreateInstance<TaskGraphDocument>();
            document.nodes.Add(new TaskGraphDocNode
            {
                id = "tick-marker", entry = TaskGraphEntry.Tick, position = new Vector2(40f, 60f)
            });
            ProjectWindowUtil.CreateAsset(document, "New Task Graph.asset");
        }
    }
}

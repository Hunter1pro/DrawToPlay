using System;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>A manifest row being dragged out of the Level Manifest's list — onto a mission tree, where it
    /// becomes a step about it.</summary>
    internal sealed class LevelObjectDrag
    {
        /// <summary>The DragAndDrop generic-data key the drag travels under.</summary>
        public const string Key = "PowerOfFire.DrawToPlay.LevelObjectRow";

        public LevelObjectRegistry manifest;
        public string id;

        public LevelObjectDef row => manifest != null ? manifest.FindById(id) as LevelObjectDef : null;

        public static void Start(LevelObjectRegistry manifest, LevelObjectDef row)
        {
            if (manifest == null || row == null)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.paths = Array.Empty<string>();
            DragAndDrop.SetGenericData(Key, new LevelObjectDrag { manifest = manifest, id = row.id });
            DragAndDrop.StartDrag(string.IsNullOrEmpty(row.name) ? row.id : row.name);
        }

        /// <summary>The row being dragged, or null.</summary>
        public static LevelObjectDrag Dragged() => DragAndDrop.GetGenericData(Key) as LevelObjectDrag;
    }
}

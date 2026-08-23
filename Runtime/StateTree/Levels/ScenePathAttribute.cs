using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A STRING THAT NAMES A SCENE — authored by dropping the scene asset, stored as the path
    /// the runtime loads by. The editor shows a scene field and writes
    /// "Assets/…/Level.unity" underneath; play mode and players read the string and never
    /// touch the asset. The drawer also says whether the scene is listed in Build Settings,
    /// because a scene that is not listed never arrives.
    /// </summary>
    public sealed class ScenePathAttribute : PropertyAttribute
    {
    }
}

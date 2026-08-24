using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// PRESS PLAY ON A LEVEL AND IT WORKS — brings the session in behind you when it is not
    /// already there.
    ///
    /// A level scene holds the place; the ROOT scene holds the session — the world registry,
    /// the inventory, the save, the HUD. Open a level to work on it, press play, and none of
    /// that exists: every character reports "no WorldService reachable", trees will not run,
    /// no HUD appears. So the level asks for the session. If a root is already loaded — the
    /// normal path, where the root opened this level — it does nothing and gets out of the way.
    ///
    /// It does NOT load a level afterwards: the game's root checks for an already-open level
    /// itself, so the two never argue about who is in charge.
    /// </summary>
    [AddComponentMenu("Draw To Play/Levels/Level Bootstrap")]
    public sealed class LevelBootstrap : MonoBehaviour
    {
        [Tooltip("The session scene to bring in when there is not one — picked as the scene, "
            + "kept as its path (a name still works for a scene listed in the build settings).")]
        [ScenePath]
        public string rootScene = "";

        [Tooltip("Off to let a level be played bare — for a test that WANTS no session.")]
        public bool bringSessionIn = true;

        private void Awake()
        {
            if (!bringSessionIn || string.IsNullOrEmpty(rootScene))
                return;

            // ALREADY HERE? A session is a ROOT-SCOPED HOST — whichever game's: the toolset's
            // scope, not a class of any game — checked by component, not by scene name.
            foreach (StateTreeContextHost host in FindObjectsByType<StateTreeContextHost>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (host != null && host.kind == StateTreeContextKind.Root)
                    return;
            }

            // ADDITIVE, and synchronous: the characters in this scene wake up this frame.
            SceneManager.LoadScene(rootScene, LoadSceneMode.Additive);
        }
    }
}

using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The level, put back, joins the episode: the episode tree's node for this level runs from
    /// where it last stood, on this level's board, until the level goes. Placed after the level's
    /// restore, so the run resumes against the facts the save put back. Instant. A project with
    /// no episode scope joins nothing.
    /// </summary>
    [StateTreeCategory("Tasks/Levels", "This level's part of the episode tree runs from here")]
    public sealed class JoinEpisodeTask : StateTreeTaskAsset
    {
        [Tooltip("The level this tree is — its node in the episode tree.")]
        [RowName(typeof(LevelDef))] public string level = "";

        [InjectHost(StateTreeContextKind.Level)] private StateTreeContextHost m_Level;

        [InjectHost(StateTreeContextKind.Episode, optional: true)] private StateTreeContextHost m_Episode;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Episode != null)
                m_Episode.JoinEpisode(level, m_Level);
            return StateTreeStatus.Success;
        }
    }
}

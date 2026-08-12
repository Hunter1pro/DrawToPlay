namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHICH TWO AXES A MANIFEST'S POSITIONS MEAN.
    ///
    /// A placement's position is a <c>Vector2</c> on purpose — a level is a ground plan, and the
    /// third number is either always zero or a consequence of what is being placed (a pickup
    /// floats, a person stands). But WHICH two axes that plan is drawn on is a property of the
    /// game, not of the row: the raider areas are a 2D game in XY, the outpost is a 3D one whose
    /// ground is XZ, and both are levels in the same project with the same manifests.
    ///
    /// Left implicit, every spawner picks its own mapping and every editor tool has to guess.
    /// That is what happened: two spawners disagreed, and the manifest overlay drew its handles
    /// and its ghosts standing up in a wall in one of the two levels — the placements were
    /// correct and the picture of them was not, which is the worst way for a tool to be wrong.
    /// Named here once, the spawner and the overlay read the same answer.
    /// </summary>
    public enum LevelGroundPlane
    {
        /// <summary>X right, Y up, positions on the screen plane — a 2D game. The default,
        /// because it is what every manifest written before this existed meant.</summary>
        XY = 0,

        /// <summary>X right, Z forward, Y the height a spawner supplies — a 3D game seen from
        /// somewhere above.</summary>
        XZ = 1
    }
}

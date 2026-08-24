using System.Runtime.CompilerServices;

// The example verifies exercise the editor's internal surface (the window, the ops layer) the
// way the EditMode tests do — examples are the library's first consumer, not a stranger.
[assembly: InternalsVisibleTo("PowerOfFire.DrawToPlay.Examples.Editor")]
[assembly: InternalsVisibleTo("PowerOfFire.DrawToPlay.Tests.Editor")]
[assembly: InternalsVisibleTo("PowerOfFire.DrawToPlay.Examples.Tests.Editor")]
[assembly: InternalsVisibleTo("PowerOfFire.DrawToPlay.Examples.GraphEditor")]
[assembly: InternalsVisibleTo("CyberBot.Editor")]
[assembly: InternalsVisibleTo("CyberBot.GraphEditor")]
[assembly: InternalsVisibleTo("CyberBot.Tests.Editor")]

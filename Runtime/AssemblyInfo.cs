using System.Runtime.CompilerServices;

// THE ENCODING IS THE RUNTIME'S (M32): an item's count is a blackboard key with a shape, and
// the inventory service is the only thing that may WRITE it. The tests are let in because they
// seed a bag to check the encoding itself — the one caller that legitimately speaks it directly.
[assembly: InternalsVisibleTo("PowerOfFire.DrawToPlay.Tests.Editor")]
[assembly: InternalsVisibleTo("PowerOfFire.DrawToPlay.Examples.Tests.Editor")]
[assembly: InternalsVisibleTo("CyberBot.Tests.Editor")]

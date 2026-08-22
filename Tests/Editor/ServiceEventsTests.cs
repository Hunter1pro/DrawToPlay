using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// META-RULE 1, AS A TEST (M40.4): no subsystem raises a C# event for another subsystem or a
    /// screen to hear. Every <see cref="StateTreeService"/> in the runtime and the waystation,
    /// and the waystation's plain-class services, declare zero public events. The next one
    /// fails here, with its name.
    /// </summary>
    [TestFixture]
    public sealed class ServiceEventsTests
    {
        [Test]
        public void NoService_DeclaresAPublicEvent()
        {
            var offenders = new List<string>();
            foreach (Type type in ServiceTypes())
            {
                EventInfo[] events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                for (int i = 0; i < events.Length; i++)
                    offenders.Add(type.Name + "." + events[i].Name);
            }
            Assert.That(offenders, Is.Empty,
                "a service raised an event for someone to hear — call them instead (meta-rule 1):\n"
                + string.Join("\n", offenders));
        }

        private static IEnumerable<Type> ServiceTypes()
        {
            Assembly runtime = typeof(StateTreeService).Assembly;
            Assembly game = typeof(OutpostSaveService).Assembly;
            foreach (Assembly assembly in new[] { runtime, game })
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(StateTreeService).IsAssignableFrom(type) && !type.IsAbstract)
                        yield return type;
                }
            }
            // The waystation's plain services — built by its root and level, not by a def.
            yield return typeof(OutpostSaveService);
            yield return typeof(OutpostProgressService);
            yield return typeof(OutpostDialogService);
            yield return typeof(OutpostCombatService);
            yield return typeof(OutpostLocomotionService);
        }
    }
}

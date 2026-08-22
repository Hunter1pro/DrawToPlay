using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>Generated with ClockService: the def and the class agree, and the
    /// class builds from its def. Passes the day it is written; fails the day they drift.</summary>
    [TestFixture]
    public sealed class ClockServiceTests
    {
        private const string k_DefPath = "Assets/DrawToPlayExamples/Demo/M21/Subsystems/ClockService.asset";

        [Test]
        public void TheDefAndTheClassAgree_AndItBuilds()
        {
            var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(k_DefPath);
            Assert.That(def, Is.Not.Null, "the def this class was sketched with");
            Assert.That(def.serviceType, Is.EqualTo(typeof(PowerOfFire.DrawToPlay.Examples.ClockService)));

            // Every request the def serves is an action the class declares.
            var declared = new System.Collections.Generic.HashSet<string>();
            foreach (ServiceActionContractAttribute contract in typeof(PowerOfFire.DrawToPlay.Examples.ClockService)
                .GetCustomAttributes(typeof(ServiceActionContractAttribute), true))
                declared.Add(contract.action);
            for (int i = 0; i < def.requests.Count; i++)
                Assert.That(declared, Does.Contain(def.requests[i].action), def.requests[i].key);

            // And it builds from its def on a bare scope, with the class defaults in place.
            var go = new GameObject("Scope") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var host = go.AddComponent<StateTreeContextHost>();
                host.kind = def.scope;
                host.autoStart = false;
                var service = new PowerOfFire.DrawToPlay.Examples.ClockService(host, def);
                Assert.That(service.secondsPerDay, Is.EqualTo(120.0f));
                Assert.That(service.startHour, Is.EqualTo(6));
                service.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

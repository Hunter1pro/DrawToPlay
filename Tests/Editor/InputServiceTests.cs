using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>The dash press carries a direction and both are spent together — a touch
    /// flick's way reaches the mover, a button's press carries none.</summary>
    [TestFixture]
    public sealed class InputServiceTests
    {
        [Test]
        public void ADashPress_CarriesItsDirection_AndBothAreSpentTogether()
        {
            var input = new InputService();
            Assert.That(input.ConsumeDash(out Vector2 none), Is.False);
            Assert.That(none, Is.EqualTo(Vector2.zero));

            input.dashDirection = Vector2.left;
            input.dashPressed = true;
            Assert.That(input.ConsumeDash(out Vector2 asked), Is.True, "the press is consumed");
            Assert.That(asked, Is.EqualTo(Vector2.left), "with the way it asked for");
            Assert.That(input.dashPressed, Is.False);
            Assert.That(input.dashDirection, Is.EqualTo(Vector2.zero), "the direction is spent with it");

            input.dashPressed = true;   // a button: a press with no way
            Assert.That(input.ConsumeDash(out Vector2 button), Is.True);
            Assert.That(button, Is.EqualTo(Vector2.zero), "the mover falls back to stick, then aim");
        }
    }
}

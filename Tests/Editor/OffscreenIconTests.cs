using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>The screen-edge arrow's math: onscreen shows nothing, offscreen clamps to
    /// the margin box aimed from centre, and a target BEHIND the camera flips its mirrored
    /// projection so the arrow points where to turn.</summary>
    [TestFixture]
    public sealed class OffscreenIconTests
    {
        [Test]
        public void OnScreen_ShowsNothing()
        {
            Assert.IsFalse(OffscreenIcon.Resolve(new Vector3(0.4f, 0.6f, 5f), 0.06f,
                out _, out _));
        }

        [Test]
        public void OffToTheRight_ClampsToTheRightEdge_AimedRight()
        {
            Assert.IsTrue(OffscreenIcon.Resolve(new Vector3(1.5f, 0.5f, 5f), 0.06f,
                out Vector2 anchor, out float angle));
            Assert.AreEqual(1f - 0.06f, anchor.x, 0.001f, "clamped to the margin box");
            Assert.AreEqual(0.5f, anchor.y, 0.001f);
            Assert.AreEqual(0f, angle, 0.001f, "aimed along +x");
        }

        [Test]
        public void AboveTheTop_AimsUp()
        {
            Assert.IsTrue(OffscreenIcon.Resolve(new Vector3(0.5f, 1.8f, 5f), 0.1f,
                out Vector2 anchor, out float angle));
            Assert.AreEqual(0.5f, anchor.x, 0.001f);
            Assert.AreEqual(0.9f, anchor.y, 0.001f);
            Assert.AreEqual(90f, angle, 0.001f);
        }

        [Test]
        public void BehindTheCamera_FlipsTheMirroredProjection()
        {
            // A target behind projects mirrored: it reads as up-right while the real
            // direction is down-left — the flip aims the arrow where to turn.
            Assert.IsTrue(OffscreenIcon.Resolve(new Vector3(0.8f, 0.8f, -2f), 0.06f,
                out Vector2 anchor, out float angle));
            Assert.Less(anchor.x, 0.5f);
            Assert.Less(anchor.y, 0.5f);
            Assert.AreEqual(-135f, angle, 0.001f);
        }
    }
}

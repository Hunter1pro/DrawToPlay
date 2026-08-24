using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.10 — the arcade jump (deepnight/gamefeel): one double jump in the air and the well
    /// is dry until the ground; a jump a beat after the ledge still counts (coyote 0.15s); a
    /// press a beat before touch is kept and spent on landing (buffer 0.12s); letting go while
    /// climbing cuts the climb (tap short, hold high) — but a pad's launch is never cut; and a
    /// landing is a FACT with its fall speed on it, for the feel skin to squash by.
    /// </summary>
    [TestFixture]
    public sealed class ArenaFeelTests
    {
        private const float k_Step = 1f / 60f;
        private PhysicsBody m_Ground;
        private GameObject m_Go;
        private ArenaFighter m_Fighter;

        [SetUp]
        public void SetUp()
        {
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            m_Ground = world.CreateBody(new PhysicsBodyDefinition
            {
                type = PhysicsBody.BodyType.Static, position = new Vector2(0f, -1f)
            });
            m_Ground.CreateShape(PolygonGeometry.CreateBox(new Vector2(40f, 2f)), new PhysicsShapeDefinition
            {
                contactFilter = new PhysicsShape.ContactFilter { categories = ArenaLayers.Static, contacts = PhysicsMask.All }
            });
            m_Go = new GameObject("Fighter") { hideFlags = HideFlags.HideAndDontSave };
            m_Go.SetActive(false);
            m_Fighter = m_Go.AddComponent<ArenaFighter>();
            m_Fighter.Wake();
            m_Fighter.Place(new Vector2(0f, 3f));
        }

        [TearDown]
        public void TearDown()
        {
            m_Fighter.Sleep();
            if (m_Ground.isValid)
                m_Ground.Destroy();
            Object.DestroyImmediate(m_Go);
        }

        private void Steps(int count)
        {
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            for (int i = 0; i < count; i++)
                m_Fighter.Step(world, k_Step);
        }

        private void Settle()
        {
            Steps(120);
            Assert.That(m_Fighter.onGround, Is.True, "settled first");
        }

        private void Press()
        {
            m_Fighter.Intent(0f, true);
            Steps(1);
        }

        private void Release()
        {
            m_Fighter.Intent(0f, false);
            Steps(1);
        }

        [Test]
        public void DoubleJump_OneInTheAir_ThenTheWellIsDry()
        {
            Settle();
            Press();
            Assert.That(m_Fighter.jumpedThisStep, Is.True, "the ground jump");
            Release();
            Steps(20);
            Assert.That(m_Fighter.onGround, Is.False);

            Press();
            Assert.That(m_Fighter.airJumpedThisStep, Is.True, "the double jump");
            Assert.That(m_Fighter.velocity.y, Is.EqualTo(m_Fighter.jumpSpeed).Within(0.6f), "a full second climb");
            // And mid-launch it never SLOWS the ride — checked in the pad test below.
            Release();
            Steps(10);

            float before = m_Fighter.velocity.y;
            Press();
            Assert.That(m_Fighter.airJumpedThisStep, Is.False, "the well is dry");
            Assert.That(m_Fighter.velocity.y, Is.LessThan(before), "still falling");
            m_Fighter.Intent(0f, false);

            Steps(240);
            Assert.That(m_Fighter.onGround, Is.True, "the ground refills it");
            Press();
            Release();
            Steps(20);
            Press();
            Assert.That(m_Fighter.airJumpedThisStep, Is.True, "and it works again");
        }

        [Test]
        public void CoyoteTime_AJumpABeatAfterTheLedge_StillCounts()
        {
            m_Fighter.airJumps = 0;   // isolate the coyote path
            Settle();
            m_Fighter.Place(new Vector2(0f, 8f));   // the ledge vanished under our feet
            Steps(4);                               // 0.066s — inside the 0.15s window
            Assert.That(m_Fighter.onGround, Is.False);
            Press();
            Assert.That(m_Fighter.jumpedThisStep, Is.True, "the just-in-time jump");
        }

        [Test]
        public void CoyoteTime_TooLate_IsJustFalling()
        {
            m_Fighter.airJumps = 0;
            Settle();
            m_Fighter.Place(new Vector2(0f, 12f));
            Steps(12);                              // 0.2s — past the window
            Press();
            Assert.That(m_Fighter.jumpedThisStep, Is.False, "the ledge is gone");
            Assert.That(m_Fighter.velocity.y, Is.LessThan(0f), "falling");
        }

        [Test]
        public void JumpBuffer_APressJustBeforeTouch_IsSpentOnLanding()
        {
            m_Fighter.airJumps = 0;   // the early press must not air-jump; it queues
            Settle();
            m_Fighter.Place(new Vector2(0f, 2.6f));
            Steps(12);                // 0.2s: the coyote window from settling is closed
            int guard = 0;
            while (m_Fighter.position.y > 1.25f && guard++ < 300)
                Steps(1);             // fall until the touch is a breath away
            Press();                  // pressed early, still in the air
            bool jumped = m_Fighter.jumpedThisStep;
            m_Fighter.Intent(0f, false);
            for (int i = 0; i < 8 && !jumped; i++)
            {
                Steps(1);
                jumped = m_Fighter.jumpedThisStep;
            }
            Assert.That(jumped, Is.True, "the press was kept and spent on touch");
            Assert.That(m_Fighter.velocity.y, Is.EqualTo(m_Fighter.jumpSpeed).Within(0.6f));
        }

        [Test]
        public void TapShort_HoldHigh_AndAPadLaunchIsNeverCut()
        {
            Settle();
            Press();
            Steps(3);
            float rising = m_Fighter.velocity.y;
            Release();
            Assert.That(m_Fighter.velocity.y, Is.LessThan(rising * 0.6f), "letting go cut the climb");

            Steps(240);
            Assert.That(m_Fighter.onGround, Is.True);
            m_Fighter.airJumps = 0;   // isolate the pad: the press must do nothing at all
            m_Fighter.Launch(20f);
            Steps(2);
            float launched = m_Fighter.velocity.y;
            Press();
            Release();    // the release must not cut the PAD's speed — it was never a jump
            Assert.That(m_Fighter.velocity.y, Is.GreaterThan(launched - 1.5f),
                "the launch survived the release");
        }

        [Test]
        public void ALanding_IsAFact_WithItsFallSpeedOnIt()
        {
            m_Fighter.Place(new Vector2(0f, 10f));
            bool landed = false;
            float speed = 0f;
            for (int i = 0; i < 300 && !landed; i++)
            {
                Steps(1);
                landed = m_Fighter.landedThisStep;
                speed = m_Fighter.landedWithSpeed;
            }
            Assert.That(landed, Is.True, "the touch is a fact");
            Assert.That(speed, Is.GreaterThan(8f), "with the fall's weight on it");
            Steps(1);
            Assert.That(m_Fighter.landedThisStep, Is.False, "facts last one step");
        }

        [Test]
        public void AVolley_IsAFact_ForOneStepOnly()
        {
            Settle();
            m_Fighter.Recoil(Vector2.right, 1f);
            Assert.That(m_Fighter.firedThisStep, Is.True, "the shot is a fact");
            Steps(1);
            Assert.That(m_Fighter.firedThisStep, Is.False,
                "and it lasts ONE step — a stuck flag shook the camera every frame forever");
        }

        [Test]
        public void Dash_IsAFlatOutBurst_AndGravityWaitsItsTurn()
        {
            Settle();
            m_Fighter.Place(new Vector2(0f, 8f));
            Steps(2);
            m_Fighter.Dash(1f);
            Assert.That(m_Fighter.dashedThisStep, Is.True, "the burst is a fact");
            Steps(4);   // 0.066s — inside the 0.14s dash
            Assert.That(m_Fighter.velocity.x, Is.EqualTo(m_Fighter.dashSpeed).Within(0.01f), "flat out");
            Assert.That(m_Fighter.velocity.y, Is.EqualTo(0f).Within(0.01f), "gravity waits");
            Steps(12);  // past the end
            Assert.That(m_Fighter.velocity.x, Is.LessThanOrEqualTo(m_Fighter.maxSpeed * 1.3f + 0.01f),
                "the end keeps a little of it, not all");
            Assert.That(m_Fighter.velocity.y, Is.LessThan(0f), "and gravity is back");
        }

        [Test]
        public void Dash_CoolsDown_ThenServesAgain()
        {
            Settle();
            m_Fighter.Dash(1f);
            Steps(1);
            m_Fighter.Dash(1f);
            Assert.That(m_Fighter.dashedThisStep, Is.False, "still cooling");
            Steps(55);  // dash 0.14 + cooldown 0.7 = 0.84s = 51 steps
            m_Fighter.Dash(1f);
            Assert.That(m_Fighter.dashedThisStep, Is.True, "served again");
        }

        [Test]
        public void Dash_WithAnIdleStick_GoesWhereTheAimPoints()
        {
            Settle();
            m_Fighter.aim = Vector2.left;
            m_Fighter.Dash(0f);
            Steps(2);
            Assert.That(m_Fighter.velocity.x, Is.EqualTo(-m_Fighter.dashSpeed).Within(0.01f),
                "no stick: the dash follows the aim");
        }

        [Test]
        public void AStaggeredFighter_SteersNothing_UntilTheSteppedClockClears()
        {
            // M43.13 (review M5): one clock — the stagger now runs on the same stepped time
            // as the jump, so this test can DRIVE it, which Time.time never allowed.
            Settle();
            m_Fighter.Shove(new Vector2(-6f, 2f), staggerSeconds: 0.25f);
            m_Fighter.Intent(1f, false);
            Steps(5);
            Assert.That(m_Fighter.velocity.x, Is.LessThan(0f), "staggered: intent buys nothing");
            Steps(40);   // 0.75s total — well past the stagger
            Assert.That(m_Fighter.velocity.x, Is.GreaterThan(1f), "the clock cleared and the intent works");
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M26: the AND, and the two things that make composing conditions safe rather than a trap.
    ///
    /// SHORT-CIRCUIT is not an optimisation here — some conditions CONSUME what they read (a
    /// button press is an event with exactly one taker), so an and that evaluated every child
    /// before answering would eat presses on edges it then declined. The rule that falls out is
    /// "the side-effecting test goes last", and it is only true if the walk stops at the first
    /// refusal.
    ///
    /// THE COPY has to follow the composition. A tree's deep copy exists so runners never share
    /// task or condition state; a composite whose children stayed the authored assets shared
    /// them across every runner and every level, and injected fields (filled only when empty)
    /// kept the first level's services for ever.
    /// </summary>
    [TestFixture]
    public sealed class CompositeConditionTests
    {
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            m_Assets.Clear();
        }

        [Test]
        public void Empty_IsTrue()
        {
            Assert.IsTrue(Make().Evaluate(null),
                "all of nothing holds — a half-authored edge behaves like the unconditioned "
                + "edge it currently is, rather than a dead one whose silence must be debugged");
        }

        [Test]
        public void EveryChildMustHold()
        {
            Assert.IsTrue(Make(Answer(true), Answer(true)).Evaluate(null));
            Assert.IsFalse(Make(Answer(true), Answer(false)).Evaluate(null));
            Assert.IsFalse(Make(Answer(false), Answer(true)).Evaluate(null));
        }

        [Test]
        public void NullChildrenAreSkipped()
        {
            AllCondition all = Make(Answer(true));
            all.conditions.Add(null);
            Assert.IsTrue(all.Evaluate(null),
                "an empty slot is authoring state, not a refusal");
        }

        [Test]
        public void RefusalStopsTheWalk_SoASideEffectingTestIsNeverReached()
        {
            CountingCondition refuses = Answer(false);
            CountingCondition after = Answer(true);
            Assert.IsFalse(Make(refuses, after).Evaluate(null));
            Assert.AreEqual(1, refuses.asked);
            Assert.AreEqual(0, after.asked,
                "the press-consuming condition must never be asked by an edge that has already "
                + "decided it does not want it");
        }

        [Test]
        public void Invert_FlipsTheAnswer()
        {
            AllCondition all = Make(Answer(true), Answer(false));
            all.invert = true;
            Assert.IsTrue(all.Evaluate(null), "'not all of these' needs no second class");
        }

        [Test]
        public void SelfReference_DoesNotRecurse()
        {
            AllCondition all = Make(Answer(true));
            all.conditions.Add(all);
            Assert.IsTrue(all.Evaluate(null));
        }

        [Test]
        public void DeepCopy_GivesEveryRunnerItsOwnNestedConditions()
        {
            CountingCondition child = Answer(true);
            AllCondition composite = Make(child);

            var root = Track(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            root.nodeId = "root";
            root.transitions.Add(new StateTreeTransition
            {
                targetNodeId = "elsewhere", condition = composite
            });
            var tree = Track(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = root;

            StateTreeAsset first = tree.DeepCopy();
            StateTreeAsset second = tree.DeepCopy();
            try
            {
                var firstAll = first.root.transitions[0].condition as AllCondition;
                var secondAll = second.root.transitions[0].condition as AllCondition;
                Assert.IsNotNull(firstAll);
                Assert.IsNotNull(secondAll);
                Assert.AreNotSame(composite, firstAll, "the composite itself was already copied");
                Assert.AreNotSame(child, firstAll.conditions[0],
                    "a composite's CHILD is state too — shared, it keeps whichever run "
                    + "injected it first");
                Assert.AreNotSame(firstAll.conditions[0], secondAll.conditions[0],
                    "two runners of one tree must not share a nested condition");
            }
            finally
            {
                StateTreeAsset.DestroyCopy(first);
                StateTreeAsset.DestroyCopy(second);
            }
        }

        private AllCondition Make(params StateTreeConditionAsset[] children)
        {
            AllCondition all = Track(ScriptableObject.CreateInstance<AllCondition>());
            for (int i = 0; i < children.Length; i++)
                all.conditions.Add(children[i]);
            return all;
        }

        private CountingCondition Answer(bool answer)
        {
            CountingCondition condition = Track(ScriptableObject.CreateInstance<CountingCondition>());
            condition.answer = answer;
            return condition;
        }

        private T Track<T>(T asset) where T : ScriptableObject
        {
            m_Assets.Add(asset);
            return asset;
        }
    }

    /// <summary>A condition that answers what it is told and counts how often it was asked —
    /// the stand-in for anything with a side effect.</summary>
    internal sealed class CountingCondition : StateTreeConditionAsset
    {
        public bool answer;

        public int asked;

        public override bool Evaluate(StateTreeContext context)
        {
            asked++;
            return answer;
        }
    }
}

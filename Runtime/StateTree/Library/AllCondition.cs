using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// TRUE WHEN EVERY LISTED CONDITION IS — the and, which a transition could not say before.
    ///
    /// A transition carries ONE condition, and that was enough while every edge tested one
    /// fact. The first edge that needed two — press the action button AND stand next to
    /// something choppable, so one button means chop here and shove there — had only bad
    /// answers: a bespoke condition class per pair (a combinatorial explosion of one-use
    /// assets), or an intermediate state that exists to ask a second question (a state the
    /// player can be caught in). This is the third answer, and it composes what is already
    /// declared instead of adding vocabulary.
    ///
    /// EMPTY IS TRUE, deliberately: "all of nothing" holding is what makes a half-authored
    /// edge behave like the unconditioned edge it currently is, rather than a dead one whose
    /// silence you have to debug. A null entry in the list is skipped for the same reason.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/All Of",
        fileName = "AllOf")]
    [StateTreeCategory("Conditions/Logic", "Every listed condition holds")]
    public sealed class AllCondition : StateTreeConditionAsset
    {
        [Tooltip("Each one must hold. Evaluated in order and stopped at the first refusal, so "
            + "the cheap test goes first — and a condition that CONSUMES what it reads (a "
            + "button press) must go last, or this edge eats it while deciding it did not "
            + "want it and the edge behind finds nothing left.")]
        public List<StateTreeConditionAsset> conditions = new List<StateTreeConditionAsset>();

        [Tooltip("Flip the answer — 'not all of these', which is the honest way to say a "
            + "refusal without a second class.")]
        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool all = true;
            for (int i = 0; i < conditions.Count; i++)
            {
                StateTreeConditionAsset condition = conditions[i];
                if (condition == null || condition == this)
                    continue;
                if (condition.Evaluate(context))
                    continue;
                all = false;
                break;
            }
            return invert ? !all : all;
        }
    }
}

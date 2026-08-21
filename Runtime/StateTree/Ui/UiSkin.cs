using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE ONE WAY A TASK REACHES A SKIN (M35.7) — through the UI service's ledger, by the row's
    /// name, or not at all.
    ///
    /// Three tasks had written this walk out longhand (find the service, ask for the shown view,
    /// dig out the component); a fourth would have written it again slightly differently. It is
    /// the hub's whole job — hold the references, forward the reach — so it is said once here.
    ///
    /// A row that is not on screen answers null, which is a task's cue to succeed with nothing
    /// to do rather than to fail: a bag nobody is looking at is not an error.
    /// </summary>
    public static class UiSkin
    {
        /// <summary>The shown row's skin, or null when it is not up.</summary>
        public static UiViewBehaviour Shown(StateTreeContext context, string rowName)
        {
            return Shown<UiViewBehaviour>(context, rowName);
        }

        /// <summary>
        /// SAY A VERB TO A SHOWN ROW, and be told when nobody heard it.
        ///
        /// A row that is not up answers false quietly — a beat with nobody to hear it is not an
        /// error, and that half was always right. What was wrong is the other half: a row that IS
        /// up whose skins speak none of the verb is a typo or a missing handler, and it used to
        /// look exactly like success. Now it says so, with the vocabulary those skins DO declare
        /// (<see cref="UiVerbContractAttribute"/>) in the message — the same "refused at the door
        /// with the name in it" the typed request rows do.
        ///
        /// Returns whether any skin answered.
        /// </summary>
        public static bool Say(StateTreeContext context, string rowName, string verb,
            string argument, object payload)
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(verb))
                return false;

            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            GameObject view = service != null ? service.ShownView(rowName) : null;
            if (view == null)
                return false;   // not on screen: nobody to hear it

            var skins = view.GetComponentsInChildren<UiViewBehaviour>(true);
            var answered = false;
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i].Call(verb, argument, payload))
                    answered = true;
            }
            if (!answered)
            {
                Debug.LogError("[Ui] row '" + rowName + "' is on screen but nothing answered '"
                    + verb + "' — its skins speak " + Vocabulary(skins) + ".", view);
            }
            return answered;
        }

        /// <summary>What the skins on a view declare they answer to, for the message above.</summary>
        private static string Vocabulary(UiViewBehaviour[] skins)
        {
            var verbs = new List<string>();
            for (int i = 0; skins != null && i < skins.Length; i++)
            {
                if (skins[i] == null)
                    continue;
                object[] declared = skins[i].GetType()
                    .GetCustomAttributes(typeof(UiVerbContractAttribute), true);
                for (int d = 0; d < declared.Length; d++)
                {
                    string spoken = ((UiVerbContractAttribute)declared[d]).verb;
                    if (!string.IsNullOrEmpty(spoken) && !verbs.Contains(spoken))
                        verbs.Add(spoken);
                }
            }
            return verbs.Count > 0 ? "'" + string.Join("', '", verbs) + "'" : "nothing at all";
        }

        /// <summary>The shown row's skin of a particular kind — how a task that drives ONE
        /// view (the bag's, the craft panel's) asks for it.</summary>
        public static T Shown<T>(StateTreeContext context, string rowName) where T : Component
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(rowName))
                return null;

            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            GameObject view = service != null ? service.ShownView(rowName) : null;
            return view != null ? view.GetComponentInChildren<T>(true) : null;
        }
    }
}

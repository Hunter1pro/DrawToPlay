using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>A view's VERB, declared (§4g) — the TaskOutputContract twin for skins:
    /// what <see cref="UiViewBehaviour.Call"/> answers to, readable by tools (the def
    /// inspector's screen surface, reaction pickers) without running the view.</summary>
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true,
        Inherited = true)]
    public sealed class UiVerbContractAttribute : System.Attribute
    {
        public readonly string verb;

        /// <summary>What the argument or payload means — "item name", "ItemUseResult".</summary>
        public readonly string argumentHint;

        public UiVerbContractAttribute(string verb, string argumentHint = "")
        {
            this.verb = verb;
            this.argumentHint = argumentHint;
        }
    }

    /// <summary>
    /// A view that reads its row's ARGUMENTS — the receiving end of the UI parameter
    /// channel. When the service shows a row it merges the row's declared parameters with
    /// the show-site's overrides and hands the effective list to every one of these on the
    /// spawned prefab. A view with nothing to tune simply never overrides this.
    /// </summary>
    public abstract class UiViewBehaviour : MonoBehaviour
    {
        /// <summary>The effective arguments for this showing — the row's declared defaults
        /// with the show-site's enabled overrides applied, in declaration order. Called
        /// after instantiation, and again if the same row is re-shown with new arguments.</summary>
        public virtual void Bind(IReadOnlyList<GraphTaskParameter> arguments)
        {
        }

        /// <summary>
        /// The generic VERB surface (§4c) — what one UiCallTask drives on any skin: the
        /// bag answers "toggle"/"flash", a HUD answers "pulse", the next view answers
        /// whatever it declares. Return true when the verb was yours; false lets a call
        /// pass over views that do not speak it. Base speaks nothing.
        /// </summary>
        public virtual bool Call(string verb, string argument)
        {
            return false;
        }

        /// <summary>The payload flavor (§4e): a verb handed a whole CONTRACT OBJECT — an
        /// ItemUseResult a flow routed onto the board — beside the scalar argument. The
        /// base forwards to the scalar form so a skin overrides whichever it speaks.</summary>
        public virtual bool Call(string verb, string argument, object payload)
        {
            return Call(verb, argument);
        }

        /// <summary>
        /// WHICH SCOPE A PRESS LANDS ON. The root by default, because that is where a session
        /// watches: one player, one screen, one board. A skin shown by a PER-PLAYER service
        /// overrides this to <see cref="StateTreeContextKind.Player"/> — its press is that
        /// player's, and on a split screen the other one must not hear it.
        /// </summary>
        protected virtual StateTreeContextKind requestScope => StateTreeContextKind.Root;

        /// <summary>
        /// The view's ONE output edge (the UI wiring brief): a press becomes a REQUEST on
        /// the <see cref="requestScope"/> blackboard — the GotoKey shape travel proved —
        /// served by whatever flow state watches the key. Views request; trees decide what
        /// happens, visibly.
        /// </summary>
        protected void Request(string key, string value = "1")
        {
            if (string.IsNullOrEmpty(key))
                return;
            StateTreeContextHost host = StateTreeContextHost.Resolve(gameObject, requestScope);
            if (host == null || host.Context == null)
            {
                Debug.LogWarning("[Ui] '" + name + "' has no " + requestScope
                    + " scope to request '" + key + "' on — the press went nowhere.", this);
                return;
            }
            host.Context.blackboard[key] = value ?? "";
        }

        /// <summary>Convenience reads for implementations.</summary>
        protected static string StringArg(IReadOnlyList<GraphTaskParameter> arguments,
            string name, string fallback = "")
        {
            for (int i = 0; arguments != null && i < arguments.Count; i++)
            {
                if (arguments[i] != null && arguments[i].name == name)
                    return arguments[i].stringValue ?? fallback;
            }
            return fallback;
        }

        protected static float FloatArg(IReadOnlyList<GraphTaskParameter> arguments,
            string name, float fallback = 0f)
        {
            for (int i = 0; arguments != null && i < arguments.Count; i++)
            {
                if (arguments[i] != null && arguments[i].name == name)
                    return arguments[i].floatValue;
            }
            return fallback;
        }
    }
}

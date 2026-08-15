using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
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

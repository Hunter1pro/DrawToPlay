using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THIS FIELD IS A KNOB (M36) — declared by the class, tuned on the def.
    ///
    /// A service's public fields were serialized on a component once; since M33 they are
    /// compiled-in defaults nothing can set, still carrying tooltips for an inspector that
    /// never draws them. Marked, a field is offered on the def's panel with the class's own
    /// initializer as its default, and the def — or the install — stores only what differs.
    ///
    /// THE FIELD'S TYPE IS THE SETTING'S TYPE: float, int, bool, string, an enum — and a string
    /// also marked <see cref="WorldTagAttribute"/> is a tag, PICKED from what the def declares.
    /// THE ATTRIBUTE CARRIES THE DEFAULT, not a field initializer: the panel has to show what a
    /// knob is worth when nobody overrides it, and an initializer can only be read by
    /// constructing the service — which needs a scope, and which every real service does work
    /// in. One place, readable by reflection, written onto the field by the base constructor
    /// before any layer's overrides. The class stays the source of truth for what exists and
    /// what it defaults to, exactly as <see cref="ServiceActionContractAttribute"/> is for what
    /// a service can be asked.
    ///
    /// A setting is read in the constructor body and nowhere else: it never changes while the
    /// subsystem runs, is never read by a tree task, and is never on the blackboard. The moment
    /// one wants to vary at runtime it is an attribute; the moment a flow wants it as an
    /// argument it is a parameter. Move it, do not blur it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ServiceSettingAttribute : Attribute
    {
        /// <summary>What the knob is worth when no layer overrides it — a constant of the
        /// field's type (2.4f, 256, false, "", an enum member).</summary>
        public readonly object defaultValue;

        /// <summary>What it means — the sentence the panel shows beside the number.</summary>
        public readonly string description;

        public ServiceSettingAttribute(object defaultValue, string description = "")
        {
            this.defaultValue = defaultValue;
            this.description = description ?? "";
        }
    }
}

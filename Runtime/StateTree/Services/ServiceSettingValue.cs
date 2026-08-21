using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE OVERRIDE (M36) — a setting's name and the value this layer gives it.
    ///
    /// The name is the class field's, which is the stable key: the class declares, this row
    /// only says how much. The value travels as a number and a string both, and the declared
    /// field's TYPE decides which is read — a float, int or bool from the number, a string or an
    /// enum from the text, a tag from the text with the row it was picked from beside it so a
    /// vocabulary can be renamed without every def that names it breaking.
    ///
    /// Rows for settings nobody touched are not stored: absent means "follow the layer below".
    /// </summary>
    [Serializable]
    public sealed class ServiceSettingValue
    {
        [Tooltip("Which setting — a field the service class declares with [ServiceSetting].")]
        public string name = "";

        public float floatValue;

        public string stringValue = "";

        /// <summary>The registry row a picked value came from, when it was picked. Hidden: it
        /// is the wire, and the text is the thing to read.</summary>
        [HideInInspector]
        public string entryId = "";
    }
}

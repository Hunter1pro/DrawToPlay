using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The ability service's rulebook, applied — validation of a row's authored parts against
    /// the <see cref="ServiceDef.nestingRules"/>. One place, because two consumers implement
    /// refusal (the part drawer's kind picker and this walk) and they must not disagree about
    /// what "legal" means. Code-built data goes through here too: the editor refusing a pick
    /// does nothing about a builder script writing the wrong shape.
    /// </summary>
    public static class AbilityRules
    {
        /// <summary>Validate one ability row's parts. Problems are appended, one line each,
        /// naming the row and the offending part — a report nobody can trace is not worth
        /// writing.</summary>
        public static void Validate(ServiceDef service, AbilityDef row, List<string> problems)
        {
            if (service == null || row == null || problems == null)
                return;
            ValidateChildren(service, AbilityDef.RootKind, row.parts,
                "ability '" + row.name + "'", problems);
        }

        private static void ValidateChildren(ServiceDef service, string parentKind,
            List<AbilityPartDef> children, string path, List<string> problems)
        {
            if (children == null)
                return;
            for (int i = 0; i < children.Count; i++)
            {
                AbilityPartDef part = children[i];
                if (part == null)
                    continue;

                string label = path + " → " + (string.IsNullOrEmpty(part.name)
                    ? part.kind + "[" + i + "]"
                    : "'" + part.name + "'");

                if (string.IsNullOrEmpty(part.kind))
                {
                    problems.Add(label + ": part has no kind.");
                    continue;
                }
                if (!service.Allows(parentKind, part.kind))
                {
                    problems.Add(label + ": a '" + part.kind + "' cannot sit under '"
                        + parentKind + "' — the service's nesting rules allow ["
                        + string.Join(", ", service.AllowedUnder(parentKind)) + "].");
                    // The subtree beneath an illegal part is judged against ITS kind anyway:
                    // one wrong level must not silence everything below it.
                }

                ValidateChildren(service, part.kind, part.children, label, problems);
            }
        }
    }
}

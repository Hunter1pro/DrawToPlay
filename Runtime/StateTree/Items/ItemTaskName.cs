namespace PowerOfFire.DrawToPlay
{
    /// <summary>The item tasks' shared name resolution: a blackboard key's string value
    /// when it resolves (the bag's request), else the authored fallback — the
    /// LoadLevelTask.levelNameKey rule, stated once.</summary>
    internal static class ItemTaskName
    {
        public static string Resolve(StateTreeContext context, StateTreeKeyField keyField,
            string fallback)
        {
            string key = keyField;
            if (context != null && !string.IsNullOrEmpty(key)
                && context.blackboard.TryGetValue(key, out object held)
                && held is string name && !string.IsNullOrEmpty(name))
                return name;
            return fallback;
        }
    }
}

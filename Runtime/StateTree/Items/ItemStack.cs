namespace PowerOfFire.DrawToPlay
{
    /// <summary>One row of what is carried: the definition and how many — what a grid
    /// draws and what a dialog checks, in the one shape both understand.</summary>
    public readonly struct ItemStack
    {
        public readonly ItemDef definition;
        public readonly int count;

        public ItemStack(ItemDef definition, int count)
        {
            this.definition = definition;
            this.count = count;
        }
    }
}

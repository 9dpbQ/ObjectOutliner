namespace ObjectManager.Outliner
{
    internal enum OutlinerNameOwnership
    {
        ManagedAlias = 1,
        UserDerivedNative = 2
    }

    internal enum OutlinerResolvedNameKind
    {
        None,
        Ephemeral,
        ManagedAlias,
        UserNative,
        UserDerivedNative
    }

    internal static class OutlinerNameOwnershipPolicy
    {
        public static OutlinerNameOwnership ResolveAutomaticName(
            string lineageRoot)
        {
            return string.IsNullOrWhiteSpace(lineageRoot)
                ? OutlinerNameOwnership.ManagedAlias
                : OutlinerNameOwnership.UserDerivedNative;
        }
    }
}

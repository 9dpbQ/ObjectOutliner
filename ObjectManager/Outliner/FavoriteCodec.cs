namespace ObjectManager.Outliner
{
    internal sealed class FavoriteValue
    {
        public FavoriteValue(bool isFavorite, bool canEdit)
        {
            IsFavorite = isFavorite;
            CanEdit = canEdit;
        }

        public bool IsFavorite { get; }

        public bool CanEdit { get; }
    }

    internal static class FavoriteCodec
    {
        public const string UserStringKey =
            "ba8474ea-4eb6-43e4-adcd-8f685386c163:favorite";

        public static FavoriteValue Read(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new FavoriteValue(false, true);

            var separator = value.IndexOf(':');
            if (separator <= 0 || separator == value.Length - 1)
                return new FavoriteValue(false, false);

            var version = value.Substring(0, separator);
            if (!bool.TryParse(value.Substring(separator + 1), out var favorite))
                return new FavoriteValue(false, false);

            return new FavoriteValue(
                favorite,
                string.Equals(version, "1", System.StringComparison.Ordinal));
        }

        public static string Write(bool favorite)
        {
            return favorite ? "1:true" : null;
        }
    }
}

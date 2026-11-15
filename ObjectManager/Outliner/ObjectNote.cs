using System;

namespace ObjectManager.Outliner
{
    internal static class ObjectNote
    {
        public const string Key = "ObjectOutliner.Note.v1";

        public static string Normalize(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty :
                text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        public static string Preview(string text)
        {
            var value = Normalize(text);
            return value.Length <= 300 ? value : value.Substring(0, 300) + "...";
        }
    }
}

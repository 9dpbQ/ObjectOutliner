using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal enum BatchRenameMode
    {
        Format,
        Replace,
        Add
    }

    internal enum BatchRenamePlacement
    {
        Before,
        After
    }

    internal sealed class BatchRenameOptions
    {
        public BatchRenameMode Mode { get; set; }

        public string FormatText { get; set; } = string.Empty;

        public int StartNumber { get; set; } = 1;

        public int DigitCount { get; set; } = 3;

        public string Separator { get; set; } = ".";

        public BatchRenamePlacement NumberPlacement { get; set; } =
            BatchRenamePlacement.After;

        public string FindText { get; set; } = string.Empty;

        public string ReplacementText { get; set; } = string.Empty;

        public bool MatchCase { get; set; }

        public string AddedText { get; set; } = string.Empty;

        public BatchRenamePlacement TextPlacement { get; set; } =
            BatchRenamePlacement.After;
    }

    internal sealed class BatchRenamePreview
    {
        public BatchRenamePreview(string originalName, string proposedName)
        {
            OriginalName = originalName ?? string.Empty;
            ProposedName = proposedName ?? string.Empty;
        }

        public string OriginalName { get; }

        public string ProposedName { get; }

        public bool Changed => !string.Equals(
            OriginalName,
            ProposedName,
            StringComparison.Ordinal);
    }

    internal static class BatchRenamePlanner
    {
        public static IList<BatchRenamePreview> CreatePreview(
            IEnumerable<string> names,
            BatchRenameOptions options)
        {
            var source = (names ?? Enumerable.Empty<string>())
                .Select(name => name ?? string.Empty)
                .ToList();
            options = options ?? new BatchRenameOptions();
            var result = new List<BatchRenamePreview>(source.Count);
            for (var index = 0; index < source.Count; index++)
            {
                result.Add(new BatchRenamePreview(
                    source[index],
                    CreateName(source[index], index, options)));
            }
            return result;
        }

        public static bool IsValid(
            IEnumerable<BatchRenamePreview> preview,
            BatchRenameOptions options)
        {
            options = options ?? new BatchRenameOptions();
            if (options.Mode == BatchRenameMode.Format &&
                string.IsNullOrWhiteSpace(options.FormatText))
            {
                return false;
            }
            if (options.Mode == BatchRenameMode.Replace &&
                string.IsNullOrEmpty(options.FindText))
            {
                return false;
            }
            if (options.Mode == BatchRenameMode.Add &&
                string.IsNullOrEmpty(options.AddedText))
            {
                return false;
            }

            var rows = (preview ?? Enumerable.Empty<BatchRenamePreview>())
                .ToList();
            return rows.Count > 0 &&
                rows.Any(item => item.Changed) &&
                rows.All(item => !string.IsNullOrWhiteSpace(item.ProposedName));
        }

        private static string CreateName(
            string originalName,
            int index,
            BatchRenameOptions options)
        {
            switch (options.Mode)
            {
                case BatchRenameMode.Replace:
                    return Replace(
                        originalName,
                        options.FindText,
                        options.ReplacementText,
                        options.MatchCase);
                case BatchRenameMode.Add:
                    return options.TextPlacement == BatchRenamePlacement.Before
                        ? (options.AddedText ?? string.Empty) + originalName
                        : originalName + (options.AddedText ?? string.Empty);
                default:
                    var number = Math.Max(0, options.StartNumber + index)
                        .ToString(
                            new string('0', Math.Max(1, options.DigitCount)),
                            CultureInfo.InvariantCulture);
                    var separator = options.Separator ?? string.Empty;
                    var formatText = options.FormatText?.Trim() ?? string.Empty;
                    return options.NumberPlacement == BatchRenamePlacement.Before
                        ? number + separator + formatText
                        : formatText + separator + number;
            }
        }

        private static string Replace(
            string source,
            string find,
            string replacement,
            bool matchCase)
        {
            source = source ?? string.Empty;
            find = find ?? string.Empty;
            replacement = replacement ?? string.Empty;
            if (find.Length == 0)
                return source;
            if (matchCase)
                return source.Replace(find, replacement);

            var result = new System.Text.StringBuilder(source.Length);
            var start = 0;
            while (start < source.Length)
            {
                var match = source.IndexOf(
                    find,
                    start,
                    StringComparison.CurrentCultureIgnoreCase);
                if (match < 0)
                {
                    result.Append(source, start, source.Length - start);
                    break;
                }

                result.Append(source, start, match - start);
                result.Append(replacement);
                start = match + find.Length;
            }
            return result.ToString();
        }
    }
}

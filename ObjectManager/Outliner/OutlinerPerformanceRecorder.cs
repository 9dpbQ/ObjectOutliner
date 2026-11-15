using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerPerformanceSample
    {
        public long TraceId { get; set; }
        public uint DocumentSerial { get; set; }
        public IDictionary<string, long> Details { get; set; }
        public DateTime TimestampUtc { get; set; }

        public string Reason { get; set; }

        public long TotalMilliseconds { get; set; }

        public long[] StageMilliseconds { get; set; }

        public int ObjectCount { get; set; }

        public int LoadedObjectCount { get; set; }

        public int LayerCount { get; set; }

        public int GroupCount { get; set; }

        public int GroupMembershipCount { get; set; }

        public int ProjectedRowCount { get; set; }

        public int HistoryQueryCount { get; set; }

        public int DirtyLayerCount { get; set; }

        public int ChangedObjectCount { get; set; }

        public int RenderedSelectionRowCount { get; set; }

        public int SelectionRowsAdded { get; set; }

        public int SelectionRowsRemoved { get; set; }

        public int ProgrammaticSelectionEventCount { get; set; }

        public string SelectionWriteMode { get; set; }

        public string Cause { get; set; }

        public string FallbackReason { get; set; }

        public int RequestCount { get; set; }

        public bool Deferred { get; set; }
    }

    internal static class OutlinerPerformanceRecorder
    {
        private const int Capacity = 512;
        private const long MaximumLogBytes = 2 * 1024 * 1024;
        internal static bool VerboseDiagnosticsEnabled =>
            OutlinerDiagnosticMode.IsEnabled;
        private static readonly object SyncRoot = new object();
        private static readonly Queue<OutlinerPerformanceSample> Samples =
            new Queue<OutlinerPerformanceSample>();
        private static readonly Queue<string> PendingLogLines =
            new Queue<string>();
        private static bool _flushScheduled;

        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ObjectManager",
            "performance.jsonl");

        public static void Record(OutlinerPerformanceSample sample)
        {
            if (sample == null)
                return;

            lock (SyncRoot)
            {
                Samples.Enqueue(sample);
                while (Samples.Count > Capacity)
                    Samples.Dequeue();

                if (!VerboseDiagnosticsEnabled)
                    return;

                while (PendingLogLines.Count >= 4096)
                    PendingLogLines.Dequeue();
                PendingLogLines.Enqueue(ToJson(sample));
                if (_flushScheduled)
                    return;

                _flushScheduled = true;
                ThreadPool.QueueUserWorkItem(_ => FlushPendingLines());
            }
        }

        public static IList<string> CreateSummaryLines()
        {
            List<OutlinerPerformanceSample> snapshot;
            lock (SyncRoot)
                snapshot = Samples.ToList();

            var lines = new List<string>
            {
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Object Outliner performance: {0} samples (latest {1} retained)",
                    snapshot.Count,
                    Capacity)
            };
            foreach (var group in snapshot
                .GroupBy(item => item.Reason ?? "refresh")
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var totals = group
                    .Select(item => item.TotalMilliseconds)
                    .OrderBy(value => value)
                    .ToList();
                var averageStages = new long[(int)OutlinerRefreshStage.Count];
                foreach (var sample in group)
                {
                    var stages = sample.StageMilliseconds ?? Array.Empty<long>();
                    for (var index = 0;
                         index < averageStages.Length && index < stages.Length;
                         index++)
                    {
                        averageStages[index] += stages[index];
                    }
                }
                for (var index = 0; index < averageStages.Length; index++)
                    averageStages[index] /= Math.Max(1, totals.Count);

                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: count={1}, p50={2} ms, p95={3} ms, max={4} ms, avg customcells={5} ms, selectedrows={6} ms, datastore={7} ms, project={8} ms",
                    group.Key,
                    totals.Count,
                    Percentile(totals, 0.50),
                    Percentile(totals, 0.95),
                    totals[totals.Count - 1],
                    averageStages[(int)OutlinerRefreshStage.CustomCells],
                    averageStages[(int)OutlinerRefreshStage.SelectedRows],
                    averageStages[(int)OutlinerRefreshStage.DataStore],
                    averageStages[(int)OutlinerRefreshStage.Projection]));
            }
            foreach (var group in snapshot
                .Where(item => !string.IsNullOrWhiteSpace(item.Cause))
                .GroupBy(item => item.Cause)
                .OrderByDescending(item => item.Count())
                .Take(8))
            {
                var totals = group
                    .Select(item => item.TotalMilliseconds)
                    .OrderBy(value => value)
                    .ToList();
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "cause {0}: count={1}, p50={2} ms, p95={3} ms, full={4}, partial={5}",
                    group.Key,
                    totals.Count,
                    Percentile(totals, 0.50),
                    Percentile(totals, 0.95),
                    group.Count(item => string.Equals(
                        item.Reason,
                        "full",
                        StringComparison.OrdinalIgnoreCase)),
                    group.Count(item => string.Equals(
                        item.Reason,
                        "structure",
                        StringComparison.OrdinalIgnoreCase))));
            }
            var fallbacks = snapshot
                .Where(item => !string.IsNullOrWhiteSpace(item.FallbackReason))
                .GroupBy(item => item.FallbackReason)
                .OrderByDescending(item => item.Count())
                .Take(8)
                .Select(item => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}={1}",
                    item.Key,
                    item.Count()))
                .ToList();
            if (fallbacks.Count > 0)
                lines.Add("fallbacks: " + string.Join(", ", fallbacks));
            lines.Add(VerboseDiagnosticsEnabled
                ? "Live and structured slow-operation logging is enabled: " +
                    LogPath
                : "Slow-operation logging is disabled. Run " +
                    "ObjectOutlinerDebugMode to enable it.");
            return lines;
        }

        private static long Percentile(IList<long> values, double percentile)
        {
            if (values == null || values.Count == 0)
                return 0;

            var index = (int)Math.Ceiling(values.Count * percentile) - 1;
            return values[Math.Max(0, Math.Min(values.Count - 1, index))];
        }

        private static void FlushPendingLines()
        {
            while (true)
            {
                string[] lines;
                lock (SyncRoot)
                {
                    if (PendingLogLines.Count == 0)
                    {
                        _flushScheduled = false;
                        return;
                    }

                    lines = PendingLogLines.ToArray();
                    PendingLogLines.Clear();
                }

                try
                {
                    var directory = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);
                    RotateLogIfNeeded();
                    File.AppendAllLines(LogPath, lines, Encoding.UTF8);
                }
                catch
                {
                    // Performance diagnostics must never affect Rhino work.
                }
            }
        }

        private static void RotateLogIfNeeded()
        {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogBytes)
                return;

            var previousPath = LogPath + ".1";
            if (File.Exists(previousPath))
                File.Delete(previousPath);
            File.Move(LogPath, previousPath);
        }

        internal static string ToJson(OutlinerPerformanceSample sample)
        {
            var stages = sample.StageMilliseconds ?? Array.Empty<long>();
            var builder = new StringBuilder();
            builder.Append('{');
            var timestamp = sample.TimestampUtc == default(DateTime)
                ? DateTime.UtcNow
                : sample.TimestampUtc;
            AppendJson(builder, "utc", timestamp.ToString("O", CultureInfo.InvariantCulture));
            AppendJson(builder, "reason", sample.Reason ?? "refresh");
            AppendJson(builder, "traceId", sample.TraceId);
            AppendJson(builder, "documentSerial", sample.DocumentSerial);
            if (sample.Details != null)
                foreach (var detail in sample.Details)
                    AppendJson(builder, "detail_" + detail.Key, detail.Value);
            AppendJson(builder, "totalMs", sample.TotalMilliseconds);
            AppendJson(builder, "objects", sample.ObjectCount);
            AppendJson(builder, "loadedObjects", sample.LoadedObjectCount);
            AppendJson(builder, "layers", sample.LayerCount);
            AppendJson(builder, "groups", sample.GroupCount);
            AppendJson(builder, "memberships", sample.GroupMembershipCount);
            AppendJson(builder, "rows", sample.ProjectedRowCount);
            AppendJson(builder, "historyQueries", sample.HistoryQueryCount);
            AppendJson(builder, "dirtyLayers", sample.DirtyLayerCount);
            AppendJson(builder, "changedObjects", sample.ChangedObjectCount);
            AppendJson(
                builder,
                "selected",
                sample.RenderedSelectionRowCount);
            AppendJson(builder, "selectionAdd", sample.SelectionRowsAdded);
            AppendJson(
                builder,
                "selectionRemove",
                sample.SelectionRowsRemoved);
            AppendJson(
                builder,
                "selectionWrite",
                sample.SelectionWriteMode ?? string.Empty);
            AppendJson(
                builder,
                "selectionEvents",
                sample.ProgrammaticSelectionEventCount);
            AppendJson(builder, "cause", sample.Cause ?? string.Empty);
            AppendJson(
                builder,
                "fallback",
                sample.FallbackReason ?? string.Empty);
            AppendJson(builder, "requests", sample.RequestCount);
            AppendJson(builder, "deferred", sample.Deferred);
            foreach (OutlinerRefreshStage stage in Enum.GetValues(
                typeof(OutlinerRefreshStage)))
            {
                if (stage == OutlinerRefreshStage.Count)
                    continue;
                var index = (int)stage;
                AppendJson(
                    builder,
                    stage.ToString() + "Ms",
                    index < stages.Length ? stages[index] : 0);
            }
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendJson(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendSeparator(builder);
            builder.Append('"').Append(Escape(name)).Append("\":\"")
                .Append(Escape(value)).Append('"');
        }

        private static void AppendJson(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendSeparator(builder);
            builder.Append('"').Append(Escape(name)).Append("\":")
                .Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendJson(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendSeparator(builder);
            builder.Append('"').Append(Escape(name)).Append("\":")
                .Append(value ? "true" : "false");
        }

        private static void AppendSeparator(StringBuilder builder)
        {
            if (builder.Length > 1)
                builder.Append(',');
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}

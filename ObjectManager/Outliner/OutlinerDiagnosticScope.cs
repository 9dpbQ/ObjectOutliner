using System;
using System.Diagnostics;
using System.Threading;

namespace ObjectManager.Outliner
{
    // Scope IDs group synchronous work in one dispatch, not an entire Rhino command.
    internal sealed class OutlinerDiagnosticScope : IDisposable
    {
        private static long _nextId;
        [ThreadStatic] private static long _currentId;
        [ThreadStatic] private static uint _currentDocument;
        private readonly long _previousId;
        private readonly uint _previousDocument;
        public static long CurrentId => _currentId;
        public static uint CurrentDocument => _currentDocument;
        public OutlinerRefreshMetrics Metrics { get; }
        private long _checkpoint = Stopwatch.GetTimestamp();

        public void Checkpoint(string name)
        {
            var now = Stopwatch.GetTimestamp();
            Metrics.Detail(name + "Us", (now - _checkpoint) * 1000000L / Stopwatch.Frequency);
            _checkpoint = now;
        }

        private OutlinerDiagnosticScope(string reason, uint document, long queuedAt, int requests)
        {
            _previousId = _currentId;
            _previousDocument = _currentDocument;
            if (_currentId == 0)
                _currentId = Interlocked.Increment(ref _nextId);
            _currentDocument = document;
            Metrics = new OutlinerRefreshMetrics(reason, Math.Max(1, requests), long.MaxValue);
            if (queuedAt != 0)
                Metrics.Detail("queueWaitUs", (Stopwatch.GetTimestamp() - queuedAt) * 1000000L / Stopwatch.Frequency);
        }

        public static OutlinerDiagnosticScope Start(string reason, uint document, long queuedAt = 0, int requests = 1)
        {
            return OutlinerDiagnosticMode.IsEnabled
                ? new OutlinerDiagnosticScope(reason, document, queuedAt, requests) : null;
        }

        public void Dispose()
        {
            try { Metrics.ReportIfSlow(); }
            finally
            {
                _currentId = _previousId;
                _currentDocument = _previousDocument;
            }
        }
    }
}

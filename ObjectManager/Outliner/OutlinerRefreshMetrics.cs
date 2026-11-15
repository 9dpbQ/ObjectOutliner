using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rhino;

namespace ObjectManager.Outliner
{
    internal enum OutlinerRefreshStage
    {
        ObjectCount,
        Layers,
        ObjectEnumeration,
        ObjectPresentation,
        Groups,
        History,
        Maintenance,
        Projection,
        DataStore,
        Selection,
        CustomCells,
        SelectedRows,
        Invalidate,
        Count
    }

    internal sealed class OutlinerRefreshMetrics
    {
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly long[] _elapsedTicks =
            new long[(int)OutlinerRefreshStage.Count];
        private readonly long _slowRefreshThresholdMilliseconds;
        private bool _reported;
        private readonly Dictionary<string, long> _details = new Dictionary<string, long>();
        public long TraceId { get; } = OutlinerDiagnosticScope.CurrentId;
        public uint DocumentSerial { get; } = OutlinerDiagnosticScope.CurrentDocument;

        public void Detail(string name, long value)
        {
            if (OutlinerDiagnosticMode.IsEnabled)
                _details[name] = value;
        }

        public T MeasureDetail<T>(string name, Func<T> action)
        {
            if (!OutlinerDiagnosticMode.IsEnabled)
                return action();
            var watch = Stopwatch.StartNew();
            try { return action(); }
            finally { Detail(name + "Us", watch.ElapsedTicks * 1000000L / Stopwatch.Frequency); }
        }

        public void MeasureDetail(string name, Action action)
        {
            MeasureDetail(name, () => { action(); return true; });
        }

        public OutlinerRefreshMetrics(
            string reason,
            int requestCount,
            long slowRefreshThresholdMilliseconds = 250)
        {
            Reason = string.IsNullOrWhiteSpace(reason) ? "refresh" : reason;
            RequestCount = Math.Max(1, requestCount);
            _slowRefreshThresholdMilliseconds = Math.Max(
                0,
                slowRefreshThresholdMilliseconds);
        }

        public string Reason { get; }

        public int RequestCount { get; }

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

        public bool Deferred { get; set; }

        public T Measure<T>(OutlinerRefreshStage stage, Func<T> action)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                watch.Stop();
                _elapsedTicks[(int)stage] += watch.ElapsedTicks;
            }
        }

        public void Measure(OutlinerRefreshStage stage, Action action)
        {
            Measure(stage, () =>
            {
                action();
                return true;
            });
        }

        public void ReportIfSlow()
        {
            if (_reported)
                return;

            _reported = true;
            _total.Stop();
            var stageMilliseconds = new long[(int)OutlinerRefreshStage.Count];
            for (var index = 0; index < stageMilliseconds.Length; index++)
            {
                stageMilliseconds[index] = _elapsedTicks[index] * 1000L /
                    Stopwatch.Frequency;
            }
            OutlinerPerformanceRecorder.Record(new OutlinerPerformanceSample
            {
                TimestampUtc = DateTime.UtcNow,
                TraceId = TraceId,
                DocumentSerial = DocumentSerial,
                Details = new Dictionary<string, long>(_details),
                Reason = Reason,
                TotalMilliseconds = _total.ElapsedMilliseconds,
                StageMilliseconds = stageMilliseconds,
                ObjectCount = ObjectCount,
                LoadedObjectCount = LoadedObjectCount,
                LayerCount = LayerCount,
                GroupCount = GroupCount,
                GroupMembershipCount = GroupMembershipCount,
                ProjectedRowCount = ProjectedRowCount,
                HistoryQueryCount = HistoryQueryCount,
                DirtyLayerCount = DirtyLayerCount,
                ChangedObjectCount = ChangedObjectCount,
                RenderedSelectionRowCount = RenderedSelectionRowCount,
                SelectionRowsAdded = SelectionRowsAdded,
                SelectionRowsRemoved = SelectionRowsRemoved,
                ProgrammaticSelectionEventCount =
                    ProgrammaticSelectionEventCount,
                SelectionWriteMode = SelectionWriteMode,
                Cause = Cause,
                FallbackReason = FallbackReason,
                RequestCount = RequestCount,
                Deferred = Deferred
            });
            if (!OutlinerPerformanceRecorder.VerboseDiagnosticsEnabled ||
                _total.ElapsedMilliseconds < _slowRefreshThresholdMilliseconds)
                return;

            var measuredMilliseconds = _elapsedTicks.Sum() * 1000L /
                Stopwatch.Frequency;
            var otherMilliseconds = Math.Max(
                0,
                _total.ElapsedMilliseconds - measuredMilliseconds);
            RhinoApp.WriteLine(string.Format(
                "Object Outliner perf [{0}]: total={1} ms, count={2}, layers={3}, enumerate={4}, present={5}, groups={6}, history={7}, maintain={8}, project={9}, datastore={10}, selection={11}, customcells={12}, selectedrows={13}, invalidate={14}, other={15}; objects={16:N0}, loadedObjects={17:N0}, layerCount={18:N0}, groups={19:N0}, memberships={20:N0}, rows={21:N0}, selected={22:N0}, add={23:N0}, remove={24:N0}, selectionWrite={25}, selectionEvents={26:N0}, historyQueries={27:N0}, dirtyLayers={28:N0}, changedObjects={29:N0}, requests={30}{31}, cause={32}, fallback={33}",
                Reason,
                _total.ElapsedMilliseconds,
                GetMilliseconds(OutlinerRefreshStage.ObjectCount),
                GetMilliseconds(OutlinerRefreshStage.Layers),
                GetMilliseconds(OutlinerRefreshStage.ObjectEnumeration),
                GetMilliseconds(OutlinerRefreshStage.ObjectPresentation),
                GetMilliseconds(OutlinerRefreshStage.Groups),
                GetMilliseconds(OutlinerRefreshStage.History),
                GetMilliseconds(OutlinerRefreshStage.Maintenance),
                GetMilliseconds(OutlinerRefreshStage.Projection),
                GetMilliseconds(OutlinerRefreshStage.DataStore),
                GetMilliseconds(OutlinerRefreshStage.Selection),
                GetMilliseconds(OutlinerRefreshStage.CustomCells),
                GetMilliseconds(OutlinerRefreshStage.SelectedRows),
                GetMilliseconds(OutlinerRefreshStage.Invalidate),
                otherMilliseconds,
                ObjectCount,
                LoadedObjectCount,
                LayerCount,
                GroupCount,
                GroupMembershipCount,
                ProjectedRowCount,
                RenderedSelectionRowCount,
                SelectionRowsAdded,
                SelectionRowsRemoved,
                string.IsNullOrWhiteSpace(SelectionWriteMode)
                    ? "none"
                    : SelectionWriteMode,
                ProgrammaticSelectionEventCount,
                HistoryQueryCount,
                DirtyLayerCount,
                ChangedObjectCount,
                RequestCount,
                Deferred ? ", deferred" : string.Empty,
                string.IsNullOrWhiteSpace(Cause) ? "none" : Cause,
                string.IsNullOrWhiteSpace(FallbackReason)
                    ? "none"
                    : FallbackReason));
        }

        private long GetMilliseconds(OutlinerRefreshStage stage)
        {
            return _elapsedTicks[(int)stage] * 1000L / Stopwatch.Frequency;
        }
    }
}

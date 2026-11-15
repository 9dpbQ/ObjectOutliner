using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;

namespace ObjectManager.Outliner
{
    internal sealed class DocumentLoadStateEventArgs : EventArgs
    {
        public DocumentLoadStateEventArgs(uint documentSerialNumber)
        {
            DocumentSerialNumber = documentSerialNumber;
        }

        public uint DocumentSerialNumber { get; }
    }

    internal sealed class DocumentLoadMonitor : IDisposable
    {
        private readonly object _sync = new object();
        private readonly HashSet<uint> _readyDocuments = new HashSet<uint>();
        private bool _disposed;

        public DocumentLoadMonitor()
        {
            foreach (var doc in RhinoDoc.OpenDocuments(false)
                .Where(candidate => candidate != null && candidate.Views.Count() > 0))
            {
                _readyDocuments.Add(doc.RuntimeSerialNumber);
            }

            RhinoDoc.BeginOpenDocument += OnBeginOpenDocument;
            RhinoDoc.EndOpenDocumentInitialViewUpdate += OnEndOpenDocument;
            RhinoDoc.NewDocument += OnNewDocument;
            RhinoDoc.CloseDocument += OnCloseDocument;
        }

        public event EventHandler<DocumentLoadStateEventArgs> DocumentOpening;

        public event EventHandler<DocumentLoadStateEventArgs> DocumentReady;

        public bool IsReady(uint documentSerialNumber)
        {
            lock (_sync)
                return !_disposed && _readyDocuments.Contains(documentSerialNumber);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            RhinoDoc.BeginOpenDocument -= OnBeginOpenDocument;
            RhinoDoc.EndOpenDocumentInitialViewUpdate -= OnEndOpenDocument;
            RhinoDoc.NewDocument -= OnNewDocument;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            lock (_sync)
                _readyDocuments.Clear();
        }

        private void OnBeginOpenDocument(object sender, DocumentOpenEventArgs e)
        {
            // Paste and import are merge opens into an already ready document.
            if (e.Merge)
                return;

            var serialNumber = e.DocumentSerialNumber;
            lock (_sync)
                _readyDocuments.Remove(serialNumber);
            DocumentOpening?.Invoke(
                this,
                new DocumentLoadStateEventArgs(serialNumber));
        }

        private void OnEndOpenDocument(object sender, DocumentOpenEventArgs e)
        {
            if (e.Merge)
                return;

            MarkReady(e.DocumentSerialNumber);
        }

        private void OnNewDocument(object sender, DocumentEventArgs e)
        {
            MarkReady(e.DocumentSerialNumber);
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            lock (_sync)
                _readyDocuments.Remove(e.DocumentSerialNumber);
        }

        private void MarkReady(uint documentSerialNumber)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _readyDocuments.Add(documentSerialNumber);
            }
            DocumentReady?.Invoke(
                this,
                new DocumentLoadStateEventArgs(documentSerialNumber));
        }
    }
}

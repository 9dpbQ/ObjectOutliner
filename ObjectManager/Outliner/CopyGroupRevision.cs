namespace ObjectManager.Outliner
{
    internal sealed class CopyGroupRevision
    {
        public long Current { get; private set; } = 1;
        private long _reconciled;
        public bool NeedsReconciliation => Current != _reconciled;
        public void Changed() { Current++; }
        public void Reconciled(long revision) { _reconciled = revision; }
    }
}

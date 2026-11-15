namespace ObjectManager.Outliner
{
    internal sealed class OutlinerSelectionEventGate
    {
        private int _treeWriteGeneration;
        private int _expansionGeneration;

        public bool TreeWritePending { get; private set; }

        public bool ExpansionPending { get; private set; }

        public bool SuppressTreeSelection =>
            TreeWritePending || ExpansionPending;

        public int BeginTreeWrite()
        {
            TreeWritePending = true;
            return ++_treeWriteGeneration;
        }

        public bool CompleteTreeWrite(int generation)
        {
            if (!TreeWritePending || generation != _treeWriteGeneration)
                return false;

            TreeWritePending = false;
            return true;
        }

        public void BeginUserInput()
        {
            CancelTreeWrite();
            CancelExpansion();
        }

        public int BeginExpansion()
        {
            ExpansionPending = true;
            return ++_expansionGeneration;
        }

        public bool CompleteExpansion(int generation)
        {
            if (!ExpansionPending || generation != _expansionGeneration)
                return false;

            ExpansionPending = false;
            return true;
        }

        public void CancelTreeWrite()
        {
            _treeWriteGeneration++;
            TreeWritePending = false;
        }

        public void CancelExpansion()
        {
            _expansionGeneration++;
            ExpansionPending = false;
        }

        public void CancelAll()
        {
            CancelTreeWrite();
            CancelExpansion();
        }
    }
}

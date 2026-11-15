namespace ObjectManager.Outliner
{
    internal enum OutlinerDropPlacement
    {
        Over,
        Before,
        After,
        Append
    }

    internal sealed class OutlinerDropTarget
    {
        public OutlinerDropTarget(
            OutlinerNodeReference targetNode,
            OutlinerDropPlacement placement)
        {
            TargetNode = targetNode;
            Placement = placement;
        }

        public OutlinerNodeReference TargetNode { get; }

        public OutlinerDropPlacement Placement { get; }
    }
}

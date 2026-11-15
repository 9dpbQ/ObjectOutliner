using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerHistoryStackPlan
    {
        private readonly IDictionary<Guid, Guid> _resultByInputId;
        private readonly IDictionary<Guid, IList<Guid>> _inputsByResultId;

        public OutlinerHistoryStackPlan(IDictionary<Guid, Guid> resultByInputId)
        {
            _resultByInputId = new Dictionary<Guid, Guid>(
                resultByInputId ?? new Dictionary<Guid, Guid>());
            _inputsByResultId = _resultByInputId
                .GroupBy(entry => entry.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IList<Guid>)group.Select(entry => entry.Key).ToList());
        }

        public bool TryGetResult(Guid inputId, out Guid resultId)
        {
            return _resultByInputId.TryGetValue(inputId, out resultId);
        }

        public IList<Guid> GetInputs(Guid resultId)
        {
            return _inputsByResultId.TryGetValue(resultId, out var inputs)
                ? inputs
                : Array.Empty<Guid>();
        }
    }

    internal static class OutlinerHistoryStackPlanner
    {
        public static OutlinerHistoryStackPlan Create(
            OutlinerDocumentModel model)
        {
            var resultByInputId = new Dictionary<Guid, Guid>();
            if (model == null || model.HistoryByObjectId.Count == 0)
                return new OutlinerHistoryStackPlan(resultByInputId);

            var childrenByParentId = BuildChildrenByParentId(model);
            foreach (var history in model.HistoryByObjectId.Values
                .Where(item => item != null && item.ObjectId != Guid.Empty))
            {
                if (!model.ObjectsById.TryGetValue(
                        history.ObjectId,
                        out var result))
                {
                    continue;
                }

                foreach (var inputId in history.ParentIds
                    .Where(id => id != Guid.Empty)
                    .Distinct())
                {
                    if (!CanStack(
                            model,
                            inputId,
                            result,
                            childrenByParentId,
                            resultByInputId))
                    {
                        continue;
                    }

                    resultByInputId[inputId] = result.Id;
                }
            }

            return new OutlinerHistoryStackPlan(resultByInputId);
        }

        private static IDictionary<Guid, ISet<Guid>> BuildChildrenByParentId(
            OutlinerDocumentModel model)
        {
            var children = new Dictionary<Guid, ISet<Guid>>();
            foreach (var history in model.HistoryByObjectId.Values
                .Where(item => item != null && item.ObjectId != Guid.Empty))
            {
                foreach (var parentId in history.ParentIds
                    .Where(id => id != Guid.Empty))
                {
                    AddChild(children, parentId, history.ObjectId);
                }

                foreach (var childId in history.ChildIds
                    .Where(id => id != Guid.Empty))
                {
                    AddChild(children, history.ObjectId, childId);
                }
            }
            return children;
        }

        private static void AddChild(
            IDictionary<Guid, ISet<Guid>> children,
            Guid parentId,
            Guid childId)
        {
            if (!children.TryGetValue(parentId, out var childIds))
            {
                childIds = new HashSet<Guid>();
                children[parentId] = childIds;
            }
            childIds.Add(childId);
        }

        private static bool CanStack(
            OutlinerDocumentModel model,
            Guid inputId,
            OutlinerObjectModel result,
            IDictionary<Guid, ISet<Guid>> childrenByParentId,
            IDictionary<Guid, Guid> resultByInputId)
        {
            if (inputId == result.Id ||
                resultByInputId.ContainsKey(inputId) ||
                !model.ObjectsById.TryGetValue(inputId, out var input) ||
                input.IsFavorite ||
                input.LayerId != result.LayerId ||
                model.GroupHierarchy.GetCanonicalContainerId(input.Id) !=
                    model.GroupHierarchy.GetCanonicalContainerId(result.Id) ||
                !childrenByParentId.TryGetValue(inputId, out var childIds) ||
                childIds.Count != 1 ||
                !childIds.Contains(result.Id))
            {
                return false;
            }

            var currentId = result.Id;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (currentId == inputId)
                    return false;
                currentId = resultByInputId.TryGetValue(
                    currentId,
                    out var nextId)
                        ? nextId
                        : Guid.Empty;
            }
            return true;
        }
    }
}

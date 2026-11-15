using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerSelectionResult
    {
        public bool Changed { get; set; }

        public bool FullyApplied { get; set; }

        public ISet<Guid> ActualObjectIds { get; set; } = new HashSet<Guid>();
    }

    internal sealed class OutlinerSelectionController
    {
        public ISet<Guid> Read(RhinoDoc doc)
        {
            if (doc == null)
                return new HashSet<Guid>();

            return new HashSet<Guid>(doc.Objects
                .GetSelectedObjects(false, false)
                .Where(item => item != null && !item.IsDeleted)
                .Select(item => item.Id));
        }

        public IList<Guid> GetSelectableIds(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            if (doc == null)
                return new List<Guid>();

            return (objectIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Where(id => IsSelectable(doc.Objects.FindId(id)))
                .ToList();
        }

        public OutlinerSelectionResult Apply(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            using (var diagnostic = OutlinerDiagnosticScope.Start("selection-controller", doc?.RuntimeSerialNumber ?? 0))
            {
                var desired = new HashSet<Guid>((objectIds ??
                        Enumerable.Empty<Guid>())
                    .Where(id => id != Guid.Empty));
                var before = Read(doc);
                diagnostic?.Checkpoint("prepareAndRead");
                diagnostic?.Metrics.Detail("beforeCount", before.Count);
                diagnostic?.Metrics.Detail("desiredCount", desired.Count);
                if (doc == null ||
                    desired.Any(id => !IsSelectable(doc.Objects.FindId(id))))
                {
                    return new OutlinerSelectionResult
                    {
                        Changed = false,
                        FullyApplied = false,
                        ActualObjectIds = before
                    };
                }

                if (before.SetEquals(desired))
                {
                    return new OutlinerSelectionResult
                    {
                        Changed = false,
                        FullyApplied = true,
                        ActualObjectIds = before
                    };
                }

                diagnostic?.Checkpoint("validateAndCompare");
                var removed = 0;
                var added = 0;
                foreach (var objectId in before.Where(id => !desired.Contains(id)))
                {
                    removed++;
                    var rhinoObject = doc.Objects.FindId(objectId);
                    rhinoObject?.Select(false);
                }
                diagnostic?.Checkpoint("nativeDeselect");
                foreach (var objectId in desired.Where(id => !before.Contains(id)))
                {
                    added++;
                    var rhinoObject = doc.Objects.FindId(objectId);
                    rhinoObject?.Select(true);
                }

                diagnostic?.Checkpoint("nativeSelect");
                var actual = Read(doc);
                diagnostic?.Checkpoint("readBack");
                diagnostic?.Metrics.Detail("removed", removed);
                diagnostic?.Metrics.Detail("added", added);
                return new OutlinerSelectionResult
                {
                    Changed = !before.SetEquals(actual),
                    FullyApplied = actual.SetEquals(desired),
                    ActualObjectIds = actual
                };
            }
        }

        public static bool IsSelectable(RhinoObject rhinoObject)
        {
            return rhinoObject != null &&
                !rhinoObject.IsDeleted &&
                !rhinoObject.IsReference &&
                rhinoObject.IsSelectable(true, false, false, false);
        }
    }
}

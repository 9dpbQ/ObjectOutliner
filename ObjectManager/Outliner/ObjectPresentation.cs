using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ObjectManager.Outliner
{
    internal enum OutlinerIconKind
    {
        Point,
        PointCloud,
        Curve,
        Surface,
        Polysurface,
        Extrusion,
        Mesh,
        SubD,
        Block,
        Annotation,
        Light,
        Hatch,
        Other
    }

    internal sealed class ObjectPresentation
    {
        private sealed class CachedPresentation
        {
            public uint Serial;
            public double Tolerance;
            public double AngleTolerance;
            public ObjectPresentation Value;
        }

        private static ConditionalWeakTable<RhinoDoc, Dictionary<Guid, CachedPresentation>> Cache =
            new ConditionalWeakTable<RhinoDoc, Dictionary<Guid, CachedPresentation>>();

        public static void ClearAllCaches()
        {
            Cache = new ConditionalWeakTable<RhinoDoc, Dictionary<Guid, CachedPresentation>>();
        }

        public static void Invalidate(RhinoObject obj)
        {
            if (obj?.Document != null && Cache.TryGetValue(obj.Document, out var entries))
                entries.Remove(obj.Id);
        }

        public static void ClearCache(RhinoDoc doc)
        {
            if (doc != null)
                Cache.Remove(doc);
        }

        public ObjectPresentation(string baseName, OutlinerIconKind iconKind)
        {
            BaseName = baseName;
            IconKind = iconKind;
        }

        public string BaseName { get; }

        public OutlinerIconKind IconKind { get; }

        public static ObjectPresentation FromObject(
            RhinoObject rhinoObject,
            double modelTolerance,
            double angleToleranceRadians)
        {
            var doc = rhinoObject.Document;
            if (doc == null || rhinoObject.ObjectType == ObjectType.InstanceReference)
                return Compute(rhinoObject, modelTolerance, angleToleranceRadians);

            var entries = Cache.GetOrCreateValue(doc);
            if (entries.TryGetValue(rhinoObject.Id, out var cached) &&
                cached.Serial == rhinoObject.RuntimeSerialNumber &&
                cached.Tolerance == modelTolerance &&
                cached.AngleTolerance == angleToleranceRadians)
                return cached.Value;

            var result = Compute(rhinoObject, modelTolerance, angleToleranceRadians);
            // Bound retention even when an external plug-in bypasses our deletion notifications.
            if (entries.Count >= 100000)
                entries.Clear();
            entries[rhinoObject.Id] = new CachedPresentation
            {
                Serial = rhinoObject.RuntimeSerialNumber,
                Tolerance = modelTolerance,
                AngleTolerance = angleToleranceRadians,
                Value = result
            };
            return result;
        }

        private static ObjectPresentation Compute(
            RhinoObject rhinoObject,
            double modelTolerance,
            double angleToleranceRadians)
        {
            switch (rhinoObject.ObjectType)
            {
                case ObjectType.Point:
                    return new ObjectPresentation("Point", OutlinerIconKind.Point);
                case ObjectType.PointSet:
                    return new ObjectPresentation("PointCloud", OutlinerIconKind.PointCloud);
                case ObjectType.Curve:
                    return new ObjectPresentation(
                        GetCurveBaseName(rhinoObject.Geometry as Curve, modelTolerance, angleToleranceRadians),
                        OutlinerIconKind.Curve);
                case ObjectType.Surface:
                    return new ObjectPresentation("Surface", OutlinerIconKind.Surface);
                case ObjectType.Brep:
                    return GetBrepPresentation(rhinoObject.Geometry as Brep);
                case ObjectType.Extrusion:
                    return new ObjectPresentation("Extrusion", OutlinerIconKind.Extrusion);
                case ObjectType.Mesh:
                    return new ObjectPresentation("Mesh", OutlinerIconKind.Mesh);
                case ObjectType.SubD:
                    return new ObjectPresentation("SubD", OutlinerIconKind.SubD);
                case ObjectType.InstanceReference:
                    return new ObjectPresentation(
                        GetBlockInstanceBaseName(rhinoObject as InstanceObject),
                        OutlinerIconKind.Block);
                case ObjectType.Annotation:
                case ObjectType.TextDot:
                    return new ObjectPresentation("Annotation", OutlinerIconKind.Annotation);
                case ObjectType.Light:
                    return new ObjectPresentation("Light", OutlinerIconKind.Light);
                case ObjectType.Hatch:
                    return new ObjectPresentation("Hatch", OutlinerIconKind.Hatch);
                default:
                    return new ObjectPresentation("Object", OutlinerIconKind.Other);
            }
        }

        private static ObjectPresentation GetBrepPresentation(Brep brep)
        {
            if (brep != null && brep.Faces.Count == 1)
                return new ObjectPresentation("Surface", OutlinerIconKind.Surface);

            return new ObjectPresentation("Polysurface", OutlinerIconKind.Polysurface);
        }

        private static string GetBlockInstanceBaseName(InstanceObject instanceObject)
        {
            var definitionName = instanceObject?.InstanceDefinition?.Name;
            return string.IsNullOrWhiteSpace(definitionName)
                ? "BlockInstance"
                : definitionName.Trim();
        }

        private static string GetCurveBaseName(
            Curve curve,
            double modelTolerance,
            double angleToleranceRadians)
        {
            if (curve == null)
                return "Curve";

            Circle circle;
            if (curve.TryGetCircle(out circle, modelTolerance))
                return "Circle";

            Ellipse ellipse;
            if (curve.TryGetEllipse(out ellipse, modelTolerance))
                return "Ellipse";

            Arc arc;
            if (curve.TryGetArc(out arc, modelTolerance))
                return "Arc";

            if (curve.IsLinear(modelTolerance))
                return "Line";

            Polyline polyline;
            if (curve.TryGetPolyline(out polyline))
            {
                if (IsRectangle(polyline, modelTolerance, angleToleranceRadians))
                    return "Rectangle";

                return "Polyline";
            }

            return "Curve";
        }

        private static bool IsRectangle(
            Polyline polyline,
            double modelTolerance,
            double angleToleranceRadians)
        {
            if (!polyline.IsClosed || polyline.SegmentCount != 4)
                return false;

            var first = polyline[1] - polyline[0];
            var second = polyline[2] - polyline[1];
            var third = polyline[3] - polyline[2];
            var fourth = polyline[4] - polyline[3];

            if (first.Length <= modelTolerance ||
                second.Length <= modelTolerance ||
                third.Length <= modelTolerance ||
                fourth.Length <= modelTolerance)
                return false;

            return first.IsPerpendicularTo(second, angleToleranceRadians) &&
                   second.IsPerpendicularTo(third, angleToleranceRadians) &&
                   third.IsPerpendicularTo(fourth, angleToleranceRadians) &&
                   first.IsParallelTo(third, angleToleranceRadians) != 0 &&
                   second.IsParallelTo(fourth, angleToleranceRadians) != 0;
        }
    }
}

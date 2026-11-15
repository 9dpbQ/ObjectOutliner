using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{

    internal sealed class ManagedObjectName
    {
        public string Name { get; set; }

        public string Command { get; set; }

        public int OperationNumber { get; set; }

        public string Role { get; set; }

        public string LineageRoot { get; set; }
    }

    internal struct OutlinerResolvedName
    {
        public string Name { get; set; }

        public OutlinerResolvedNameKind Kind { get; set; }

        public string Command { get; set; }

        public string Role { get; set; }

        public string LineageRoot { get; set; }

        public bool IsUserNamed =>
            Kind == OutlinerResolvedNameKind.UserNative ||
            Kind == OutlinerResolvedNameKind.UserDerivedNative;
    }

    internal static class ObjectNamingService
    {
        private const int SchemaVersion = 1;
        private const string SchemaKey = "ObjectManager.Naming.Schema";
        private const string ManagedNameKey = "ObjectManager.Naming.ManagedName";
        private const string CommandKey = "ObjectManager.Naming.Command";
        private const string OperationKey = "ObjectManager.Naming.Operation";
        private const string RoleKey = "ObjectManager.Naming.Role";
        private const string LineageRootKey = "ObjectManager.Naming.LineageRoot";
        public const string GenericCopyRole = "GenericShape";

        public static bool TryGetManagedName(
            RhinoObject rhinoObject,
            out ManagedObjectName managed)
        {
            managed = null;
            if (rhinoObject == null)
                return false;

            var dictionary = rhinoObject.Attributes.UserDictionary;
            if (dictionary.GetInteger(SchemaKey, 0) != SchemaVersion)
                return false;

            var managedName = dictionary.GetString(ManagedNameKey, string.Empty);
            if (string.IsNullOrWhiteSpace(managedName) ||
                !string.Equals(
                    rhinoObject.Attributes.Name,
                    managedName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            managed = new ManagedObjectName
            {
                Name = managedName,
                Command = dictionary.GetString(CommandKey, string.Empty),
                OperationNumber = dictionary.GetInteger(OperationKey, 0),
                Role = dictionary.GetString(RoleKey, string.Empty),
                LineageRoot = dictionary.GetString(
                    LineageRootKey,
                    string.Empty)
            };
            return true;
        }

        public static void ClearManagedName(ObjectAttributes attributes)
        {
            if (attributes == null)
                return;

            var dictionary = attributes.UserDictionary;
            dictionary.Remove(SchemaKey);
            dictionary.Remove(ManagedNameKey);
            dictionary.Remove(CommandKey);
            dictionary.Remove(OperationKey);
            dictionary.Remove(RoleKey);
            dictionary.Remove(LineageRootKey);
        }

        public static bool TryResolveName(
            RhinoObject rhinoObject,
            out OutlinerResolvedName resolved)
        {
            OutlinerNameRecord stored = null;
            ObjectManagerPlugin.Instance?.NameStore?.TryGetObject(
                rhinoObject?.Document?.RuntimeSerialNumber ?? 0,
                rhinoObject?.Id ?? Guid.Empty,
                out stored);
            return TryResolveName(rhinoObject, stored, out resolved);
        }

        public static bool TryResolveName(
            RhinoObject rhinoObject,
            IReadOnlyDictionary<Guid, OutlinerNameRecord> records,
            out OutlinerResolvedName resolved)
        {
            OutlinerNameRecord stored = null;
            records?.TryGetValue(rhinoObject?.Id ?? Guid.Empty, out stored);
            return TryResolveName(rhinoObject, stored, out resolved);
        }

        private static bool TryResolveName(
            RhinoObject rhinoObject,
            OutlinerNameRecord stored,
            out OutlinerResolvedName resolved)
        {
            resolved = default(OutlinerResolvedName);
            if (rhinoObject == null)
                return false;

            var nativeName = (rhinoObject.Attributes.Name ?? string.Empty).Trim();

            if (TryGetManagedName(rhinoObject, out var legacy))
            {
                if (stored != null &&
                    stored.Ownership == OutlinerNameOwnership.ManagedAlias)
                {
                    resolved = FromRecord(
                        stored,
                        OutlinerResolvedNameKind.ManagedAlias);
                    return true;
                }

                resolved = new OutlinerResolvedName
                {
                    Name = legacy.Name,
                    Kind = string.IsNullOrWhiteSpace(legacy.LineageRoot)
                        ? OutlinerResolvedNameKind.ManagedAlias
                        : OutlinerResolvedNameKind.UserDerivedNative,
                    Command = legacy.Command,
                    Role = legacy.Role,
                    LineageRoot = legacy.LineageRoot
                };
                return true;
            }

            if (!string.IsNullOrWhiteSpace(nativeName))
            {
                var userDerived = stored != null &&
                    stored.Ownership ==
                        OutlinerNameOwnership.UserDerivedNative &&
                    string.Equals(
                        stored.Name,
                        nativeName,
                        StringComparison.Ordinal);
                resolved = userDerived
                    ? FromRecord(
                        stored,
                        OutlinerResolvedNameKind.UserDerivedNative)
                    : new OutlinerResolvedName
                    {
                        Name = nativeName,
                        Kind = OutlinerResolvedNameKind.UserNative
                    };
                return true;
            }

            if (stored == null || string.IsNullOrWhiteSpace(stored.Name))
                return false;

            resolved = FromRecord(
                stored,
                stored.Ownership == OutlinerNameOwnership.UserDerivedNative
                    ? OutlinerResolvedNameKind.UserDerivedNative
                    : OutlinerResolvedNameKind.ManagedAlias);
            return true;
        }

        public static string ResolveGroupDisplayName(
            RhinoDoc doc,
            Group group,
            string fallbackName)
        {
            if (doc == null || group == null)
                return fallbackName ?? string.Empty;

            var nativeName = doc.Groups.GroupName(group.Index);
            if (!string.IsNullOrWhiteSpace(nativeName))
                return nativeName.Trim();

            if (ObjectManagerPlugin.Instance?.NameStore?.TryGetGroup(
                    doc.RuntimeSerialNumber,
                    group.Id,
                    out var stored) == true &&
                !string.IsNullOrWhiteSpace(stored.Name))
            {
                return stored.Name.Trim();
            }

            return fallbackName ?? string.Empty;
        }

        public static ISet<string> GetEffectiveGroupNames(RhinoDoc doc)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null)
                return names;

            for (var index = 0; index < doc.Groups.Count; index++)
            {
                var group = doc.Groups.FindIndex(index);
                if (group == null || group.IsDeleted)
                    continue;

                var name = ResolveGroupDisplayName(doc, group, string.Empty);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
            return names;
        }

        public static bool TryApplyManagedName(
            RhinoDoc doc,
            Guid objectId,
            string name,
            string command,
            int operationNumber,
            string role,
            bool allowRoleUpgrade,
            bool allowExistingName,
            string lineageRoot = null)
        {
            var rhinoObject = doc?.Objects.FindId(objectId);
            if (rhinoObject == null || rhinoObject.IsDeleted || rhinoObject.IsReference)
                return false;

            ManagedObjectName existing;
            var isManaged = TryGetManagedName(rhinoObject, out existing);
            var hasStoredName = !string.IsNullOrWhiteSpace(rhinoObject.Attributes.Name);
            var nameStore = ObjectManagerPlugin.Instance?.NameStore;
            OutlinerNameRecord existingAlias = null;
            nameStore?.TryGetObject(
                doc.RuntimeSerialNumber,
                objectId,
                out existingAlias);
            if (hasStoredName && !isManaged &&
                string.IsNullOrWhiteSpace(lineageRoot))
                return false;
            if ((isManaged || existingAlias != null) &&
                allowRoleUpgrade &&
                !string.IsNullOrWhiteSpace(
                    isManaged ? existing.Role : existingAlias.Role))
            {
                return false;
            }

            if (nameStore == null)
                return false;

            var ownership = OutlinerNameOwnershipPolicy.ResolveAutomaticName(
                lineageRoot);
            var userDerived = ownership ==
                OutlinerNameOwnership.UserDerivedNative;
            var nativeChanged = false;
            if (!userDerived && isManaged && allowExistingName)
            {
                var attributes = rhinoObject.Attributes.Duplicate();
                attributes.Name = string.Empty;
                ClearManagedName(attributes);
                nativeChanged = doc.Objects.ModifyAttributes(
                    objectId,
                    attributes,
                    true);
                if (!nativeChanged)
                    return false;
            }
            else if (userDerived &&
                !string.Equals(
                    rhinoObject.Attributes.Name,
                    name,
                    StringComparison.Ordinal))
            {
                var attributes = rhinoObject.Attributes.Duplicate();
                attributes.Name = name;
                ClearManagedName(attributes);
                nativeChanged = doc.Objects.ModifyAttributes(
                    objectId,
                    attributes,
                    true);
                if (!nativeChanged)
                    return false;
            }

            var storeChanged = nameStore.SetObject(
                doc,
                objectId,
                name,
                ownership,
                command,
                operationNumber,
                role,
                lineageRoot);
            return nativeChanged || storeChanged;
        }

        public static bool TryGetInheritableRoot(
            RhinoObject rhinoObject,
            out string rootName)
        {
            rootName = string.Empty;
            if (!TryResolveName(rhinoObject, out var resolved) ||
                !resolved.IsUserNamed)
                return false;

            rootName = string.IsNullOrWhiteSpace(resolved.LineageRoot)
                ? resolved.Name.Trim()
                : resolved.LineageRoot.Trim();
            return rootName.Length > 0;
        }

        public static int NextOperationNumber(
            RhinoDoc doc,
            string command,
            ISet<string> reservedNames = null)
        {
            var usedNames = GetUsedNames(doc, null, reservedNames);
            for (var number = 1; ; number++)
            {
                var prefix = FormatOperationPrefix(command, number);
                if (!usedNames.Any(name =>
                    string.Equals(name, prefix, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)))
                {
                    return number;
                }
            }
        }

        public static string NextNumberedName(
            RhinoDoc doc,
            string baseName,
            IEnumerable<Guid> excludedObjectIds = null,
            ISet<string> reservedNames = null)
        {
            var excluded = excludedObjectIds == null
                ? null
                : new HashSet<Guid>(excludedObjectIds);
            var usedNames = GetUsedNames(doc, excluded, reservedNames);
            for (var number = 1; ; number++)
            {
                var candidate = string.Format("{0}.{1:000}", baseName, number);
                if (!usedNames.Contains(candidate))
                    return candidate;
            }
        }

        public static string NextCopyName(
            RhinoDoc doc,
            string sourceName,
            Guid copiedObjectId,
            ISet<string> reservedNames = null)
        {
            return NextNumberedName(
                doc,
                OutlinerStrings.AppendCopySuffix(sourceName),
                new[] { copiedObjectId },
                reservedNames);
        }

        public static GenericObjectNameAllocator CreateGenericNameAllocator(
            RhinoDoc doc,
            IEnumerable<Guid> excludedObjectIds = null,
            ISet<string> reservedNames = null)
        {
            var excluded = excludedObjectIds == null
                ? new HashSet<Guid>()
                : new HashSet<Guid>(excludedObjectIds);
            var usedNames = new HashSet<string>(
                reservedNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var unnamed = new List<RhinoObject>();
            if (doc != null)
            {
                var records = ObjectManagerPlugin.Instance?.NameStore
                    ?.GetObjectSnapshot(doc.RuntimeSerialNumber);
                foreach (var obj in doc.Objects)
                {
                    if (obj == null || obj.IsDeleted || excluded.Contains(obj.Id))
                        continue;
                    if (TryResolveName(obj, records, out var resolved))
                        usedNames.Add(resolved.Name);
                    else
                        unnamed.Add(obj);
                }
            }
            var allocator = new GenericObjectNameAllocator(usedNames);
            foreach (var obj in unnamed.OrderBy(item => item.RuntimeSerialNumber))
                allocator.Next(GetGenericNameBase(doc, obj));
            return allocator;
        }

        public static bool UsesGenericCopyNaming(RhinoObject rhinoObject)
        {
            if (rhinoObject == null)
                return false;
            if (!TryResolveName(rhinoObject, out var resolved))
                return true;

            return string.Equals(
                    resolved.Command,
                    "Copy",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    resolved.Role,
                    GenericCopyRole,
                    StringComparison.Ordinal);
        }

        public static string GetGenericNameBase(
            RhinoDoc doc,
            RhinoObject rhinoObject)
        {
            if (rhinoObject == null)
                return "Object";

            return ObjectPresentation.FromObject(
                rhinoObject,
                doc?.ModelAbsoluteTolerance ?? RhinoMath.ZeroTolerance,
                doc?.ModelAngleToleranceRadians ?? RhinoMath.ToRadians(1.0))
                .BaseName;
        }

        public static string GetSourceDisplayBase(RhinoDoc doc, RhinoObject rhinoObject)
        {
            if (rhinoObject == null)
                return "Object";
            if (TryResolveName(rhinoObject, out var resolved))
                return resolved.Name.Trim();

            return ObjectPresentation.FromObject(
                rhinoObject,
                doc.ModelAbsoluteTolerance,
                doc.ModelAngleToleranceRadians).BaseName;
        }

        public static string GetCopySourceBase(RhinoDoc doc, RhinoObject rhinoObject)
        {
            var sourceName = GetSourceDisplayBase(doc, rhinoObject);
            if (!TryResolveName(rhinoObject, out var resolved) ||
                !string.Equals(
                    resolved.Command,
                    "Copy",
                    StringComparison.OrdinalIgnoreCase))
            {
                return sourceName;
            }

            return GetCopySeriesBase(sourceName);
        }

        public static string GetCopySeriesBase(string sourceName)
        {
            var value = (sourceName ?? string.Empty).Trim();
            foreach (var separator in new[] { "のコピー.", " Copy." })
            {
                var index = value.LastIndexOf(
                    separator,
                    StringComparison.OrdinalIgnoreCase);
                if (index < 0 ||
                    index + separator.Length + 3 != value.Length)
                {
                    continue;
                }

                var suffix = value.Substring(index + separator.Length);
                if (suffix.Length == 3 && suffix.All(char.IsDigit))
                    return value.Substring(0, index);
            }
            return value;
        }

        public static string FormatOperationPrefix(string command, int operationNumber)
        {
            return string.Format("{0}.{1:000}", command, operationNumber);
        }

        private static HashSet<string> GetUsedNames(
            RhinoDoc doc,
            ISet<Guid> excludedObjectIds,
            ISet<string> reservedNames)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc != null)
            {
                var nameRecords = ObjectManagerPlugin.Instance?.NameStore
                    ?.GetObjectSnapshot(doc.RuntimeSerialNumber);
                foreach (var rhinoObject in doc.Objects)
                {
                    if (rhinoObject == null ||
                        rhinoObject.IsDeleted ||
                        (excludedObjectIds != null &&
                         excludedObjectIds.Contains(rhinoObject.Id)) ||
                        !TryResolveName(
                            rhinoObject,
                            nameRecords,
                            out var resolvedName))
                    {
                        continue;
                    }
                    used.Add(resolvedName.Name);
                }
            }

            if (reservedNames != null)
            {
                foreach (var name in reservedNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                    used.Add(name);
            }
            return used;
        }

        private static OutlinerResolvedName FromRecord(
            OutlinerNameRecord record,
            OutlinerResolvedNameKind kind)
        {
            return new OutlinerResolvedName
            {
                Name = record?.Name ?? string.Empty,
                Kind = kind,
                Command = record?.Command ?? string.Empty,
                Role = record?.Role ?? string.Empty,
                LineageRoot = record?.LineageRoot ?? string.Empty
            };
        }

    }
}

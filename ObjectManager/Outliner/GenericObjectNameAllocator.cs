using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class GenericObjectNameAllocator
    {
        private readonly ISet<string> _usedNames;
        private readonly IDictionary<string, int> _nextNumberByBaseName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public GenericObjectNameAllocator(IEnumerable<string> usedNames)
        {
            _usedNames = new HashSet<string>(
                usedNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public string Next(string baseName)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(baseName)
                ? "Object"
                : baseName.Trim();
            if (!_nextNumberByBaseName.TryGetValue(
                normalizedBase,
                out var nextNumber))
            {
                nextNumber = 1;
            }

            string candidate;
            do
            {
                candidate = string.Format(
                    "{0}.{1:000}",
                    normalizedBase,
                    nextNumber);
                nextNumber++;
            }
            while (_usedNames.Contains(candidate));

            _nextNumberByBaseName[normalizedBase] = nextNumber;
            _usedNames.Add(candidate);
            return candidate;
        }
    }
}

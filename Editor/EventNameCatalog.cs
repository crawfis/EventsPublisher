using System;
using System.Collections.Generic;

using UnityEditor;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// The set of event names offered by the Inspector dropdowns, projected from every enum marked
    /// <see cref="EventEnumAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para>The names must match what <see cref="EventsPublisherEnums{T}"/> projects at runtime, since
    /// the chosen string is what the publisher is keyed on. Both call
    /// <see cref="EventsRegistry.GetPrefix"/>, so there is one definition of the projection rather than
    /// two that could drift; <see cref="EventNameCatalogTests"/> pins them together regardless.</para>
    /// <para>Marking an enum <see cref="EventEnumAttribute"/> is the opt-in. An enum with no attribute
    /// contributes nothing, and a serialized value naming one of its members is treated as unknown —
    /// shown as missing, never silently cleared.</para>
    /// </remarks>
    internal static class EventNameCatalog
    {
        private static string[] _cached;

        /// <summary>All known event names, sorted, in <c>"TypeName/MemberName"</c> form.</summary>
        internal static string[] Names
        {
            get
            {
                if (_cached == null)
                {
                    // TypeCache is maintained by the editor and is far cheaper than walking assemblies.
                    var enumTypes = new List<Type>(TypeCache.GetTypesWithAttribute<EventEnumAttribute>());
                    _cached = BuildNames(enumTypes);
                }
                return _cached;
            }
        }

        /// <summary>Drops the cache so the next access rebuilds it, after a domain reload or recompile.</summary>
        [InitializeOnLoadMethod]
        internal static void Invalidate()
        {
            _cached = null;
        }

        /// <summary>
        /// Projects a set of enum types onto sorted event names. Kept free of editor APIs so it can be
        /// tested directly.
        /// </summary>
        internal static string[] BuildNames(IEnumerable<Type> enumTypes)
        {
            var names = new List<string>();
            if (enumTypes != null)
            {
                foreach (Type enumType in enumTypes)
                {
                    if (enumType == null || !enumType.IsEnum) continue;
                    string prefix = EventsRegistry.GetPrefix(enumType);
                    foreach (object value in Enum.GetValues(enumType))
                    {
                        string candidate = prefix + "/" + value;
                        // Enum members sharing a value collapse to one name; do not offer duplicates.
                        if (!names.Contains(candidate)) names.Add(candidate);
                    }
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }
    }
}

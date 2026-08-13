using System;
using System.Collections.Generic;
using System.Reflection;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>How much attention a finding needs.</summary>
    public enum UpgradeSeverity
    {
        /// <summary>Nothing to do.</summary>
        Ok = 0,

        /// <summary>Worth looking at, but needs a judgement this tool cannot make.</summary>
        Suggestion = 1,

        /// <summary>A concrete, mechanical step that has not been taken.</summary>
        Action = 2,
    }

    /// <summary>One thing the audit noticed.</summary>
    public readonly struct UpgradeFinding
    {
        /// <summary>Which migration step this belongs to.</summary>
        public readonly string Step;

        public readonly UpgradeSeverity Severity;

        /// <summary>One line, suitable for a list.</summary>
        public readonly string Summary;

        /// <summary>The specifics — type names, field names, counts. May be empty.</summary>
        public readonly IReadOnlyList<string> Details;

        public UpgradeFinding(string step, UpgradeSeverity severity, string summary, IReadOnlyList<string> details = null)
        {
            Step = step;
            Severity = severity;
            Summary = summary;
            Details = details ?? Array.Empty<string>();
        }
    }

    /// <summary>A string found in a scene, prefab or asset whose value is a known event name.</summary>
    public readonly struct SerializedEventName
    {
        /// <summary>Asset path the value was found in.</summary>
        public readonly string AssetPath;

        /// <summary>The serialized field key, as it appears in the YAML.</summary>
        public readonly string FieldName;

        /// <summary>The event name held in that field.</summary>
        public readonly string Value;

        public SerializedEventName(string assetPath, string fieldName, string value)
        {
            AssetPath = assetPath;
            FieldName = fieldName;
            Value = value;
        }
    }

    /// <summary>
    /// Everything the audit reasons about, gathered separately so the analysis can be tested without an
    /// editor, a project, or an asset database.
    /// </summary>
    public sealed class UpgradeAuditInput
    {
        /// <summary>Enum types reachable as event families, however they were discovered.</summary>
        public IReadOnlyList<Type> EventEnums = Array.Empty<Type>();

        /// <summary>Concrete subclasses of <c>EventsPublisherEnumsSingleton&lt;T&gt;</c>.</summary>
        public IReadOnlyList<Type> SingletonSubclasses = Array.Empty<Type>();

        /// <summary>Serialized <c>string</c> fields on components, with their declaring type.</summary>
        public IReadOnlyList<FieldInfo> SerializedStringFields = Array.Empty<FieldInfo>();

        /// <summary>Event names found inside scene, prefab and asset files.</summary>
        public IReadOnlyList<SerializedEventName> SerializedEventNames = Array.Empty<SerializedEventName>();

        /// <summary>Whether the package is listed in the project's <c>testables</c>.</summary>
        public bool TestablesConfigured;

        /// <summary>What the runtime diagnostic recorded, if a play session has run.</summary>
        public IReadOnlyList<EventsDiagnostics.Observation> LateDeliveries = Array.Empty<EventsDiagnostics.Observation>();

        /// <summary>False when no play session has run since the last reset, so silence means nothing.</summary>
        public bool DiagnosticsHaveData;
    }

    /// <summary>
    /// Reports which upgrade steps a project has and has not taken.
    /// </summary>
    /// <remarks>
    /// <para>Written for the several projects consuming this package, none of which this tool knows
    /// anything about. Everything here is discovered by reflection and by reading the project's own
    /// assets, so the same audit works unchanged in a project it has never seen.</para>
    /// <para>The analysis is deliberately separate from both the gathering and the window. Gathering
    /// needs <c>TypeCache</c> and the asset database; the window needs IMGUI; the reasoning needs
    /// neither, and is the only part worth testing.</para>
    /// </remarks>
    public static class EventsUpgradeAudit
    {
        /// <summary>Names that read as a level rather than an edge, for the delivery-policy suggestion.</summary>
        private static readonly string[] LevelSuffixes =
        {
            "Ready", "Initialized", "Authenticated", "Updated", "Applied", "Loaded", "Configured",
            "Selected", "Changed", "Available", "Complete", "Completed", "Enabled", "Connected",
        };

        /// <summary>
        /// Whether a <c>manifest.json</c> lists a package in its <c>testables</c> array.
        /// </summary>
        /// <remarks>Scoped to the <c>testables</c> array specifically. A plain substring search over the
        /// whole manifest matches the package's own <c>dependencies</c> entry, which is always present —
        /// so it would report every project as already configured and never once be right.</remarks>
        public static bool IsListedInTestables(string manifestJson, string packageName)
        {
            if (string.IsNullOrEmpty(manifestJson) || string.IsNullOrEmpty(packageName)) return false;

            int key = manifestJson.IndexOf("\"testables\"", StringComparison.Ordinal);
            if (key < 0) return false;
            int open = manifestJson.IndexOf('[', key);
            if (open < 0) return false;
            int close = manifestJson.IndexOf(']', open);
            if (close < 0) return false;

            return manifestJson.Substring(open, close - open)
                               .IndexOf("\"" + packageName + "\"", StringComparison.Ordinal) >= 0;
        }

        /// <summary>Runs every check and returns the findings, most urgent first.</summary>
        public static List<UpgradeFinding> Analyze(UpgradeAuditInput input)
        {
            var findings = new List<UpgradeFinding>();
            if (input == null) return findings;

            CheckTestables(input, findings);
            CheckEventEnumAttribute(input, findings);
            CheckExecutionOrder(input, findings);
            CheckInspectorStrings(input, findings);
            CheckDeliveryPolicies(input, findings);
            CheckPayloadTypes(input, findings);
            CheckLateDeliveries(input, findings);

            findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return findings;
        }

        private static void CheckTestables(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            findings.Add(input.TestablesConfigured
                ? new UpgradeFinding("1. Tests", UpgradeSeverity.Ok, "The package is listed in testables.")
                : new UpgradeFinding("1. Tests", UpgradeSeverity.Action,
                    "The package is not in Packages/manifest.json \"testables\", so its tests do not build.",
                    new[] { "Add: \"testables\": [ \"com.crawfissoftware.eventspublisher\" ]" }));
        }

        private static void CheckEventEnumAttribute(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            var unmarked = new List<string>();
            foreach (Type enumType in input.EventEnums)
            {
                if (enumType != null && !enumType.IsDefined(typeof(EventEnumAttribute), false))
                    unmarked.Add(enumType.FullName);
            }

            if (unmarked.Count == 0)
            {
                findings.Add(new UpgradeFinding("2. [EventEnum]", UpgradeSeverity.Ok,
                    $"All {input.EventEnums.Count} event enums are marked."));
                return;
            }

            findings.Add(new UpgradeFinding("2. [EventEnum]", UpgradeSeverity.Action,
                $"{unmarked.Count} event enum(s) are not marked [EventEnum], so they are not registered " +
                "before scenes load and do not appear in the Inspector dropdowns.", unmarked));
        }

        private static void CheckExecutionOrder(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            if (input.SingletonSubclasses.Count == 0)
            {
                findings.Add(new UpgradeFinding("4. EventsFor<T>", UpgradeSeverity.Ok,
                    "No EventsPublisherEnumsSingleton<T> subclasses remain."));
                return;
            }

            var details = new List<string>();
            foreach (Type type in input.SingletonSubclasses)
            {
                if (type == null) continue;
                bool ordered = type.IsDefined(typeof(UnityEngine.DefaultExecutionOrderAttribute), false);
                details.Add(type.FullName + (ordered ? "  [DefaultExecutionOrder]" : ""));
            }

            findings.Add(new UpgradeFinding("4. EventsFor<T>", UpgradeSeverity.Action,
                $"{details.Count} scene-object publisher singleton(s) remain. EventsFor<T> is static and " +
                "lazily initialized, so it needs no GameObject and no execution order. Any " +
                "[DefaultExecutionOrder] marked below is load-bearing today and only orders Awake within " +
                "one scene load batch, which is why additive scenes still race.", details));
        }

        private static void CheckInspectorStrings(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            // Fields whose serialized value is a known event name: exact, no naming heuristic involved.
            var fieldsHoldingNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SerializedEventName found in input.SerializedEventNames)
                fieldsHoldingNames.Add(found.FieldName);

            var unmarked = new List<string>();
            var suspected = new List<string>();
            foreach (FieldInfo field in input.SerializedStringFields)
            {
                if (field == null || field.IsDefined(typeof(EventNameAttribute), false)) continue;

                string label = field.DeclaringType?.Name + "." + field.Name;
                if (fieldsHoldingNames.Contains(field.Name)) unmarked.Add(label + "  (holds an event name in an asset)");
                else if (field.Name.IndexOf("event", StringComparison.OrdinalIgnoreCase) >= 0) suspected.Add(label);
            }

            if (unmarked.Count == 0 && suspected.Count == 0)
            {
                findings.Add(new UpgradeFinding("3. [EventName]", UpgradeSeverity.Ok,
                    "No unmarked serialized string fields look like event names."));
                return;
            }

            unmarked.AddRange(suspected);
            findings.Add(new UpgradeFinding("3. [EventName]", unmarked.Count > suspected.Count ? UpgradeSeverity.Action : UpgradeSeverity.Suggestion,
                $"{unmarked.Count} serialized string field(s) hold or look like event names and lack " +
                "[EventName]. Add the attribute; do NOT change the field type to EventRef, which would " +
                "change the serialized shape and lose every name already baked into a scene or prefab. " +
                "Entries not annotated \"(holds an event name in an asset)\" are name-based guesses.",
                unmarked));
        }

        private static void CheckDeliveryPolicies(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            int total = 0, declared = 0;
            var levelCandidates = new List<string>();

            foreach (Type enumType in input.EventEnums)
            {
                if (enumType == null || !enumType.IsEnum) continue;
                foreach (FieldInfo member in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    total++;
                    if (member.IsDefined(typeof(EventDeliveryAttribute), false)) { declared++; continue; }
                    if (LooksLikeALevel(member.Name)) levelCandidates.Add(enumType.Name + "/" + member.Name);
                }
            }

            if (total == 0) return;
            if (declared == total)
            {
                findings.Add(new UpgradeFinding("6. Delivery", UpgradeSeverity.Ok,
                    $"All {total} event members declare a delivery policy."));
                return;
            }

            findings.Add(new UpgradeFinding("6. Delivery", UpgradeSeverity.Suggestion,
                $"{total - declared} of {total} event members have no [EventDelivery], so they are " +
                "Transient and a late subscriber hears nothing. Decide edge or level per event: an edge " +
                "is a transition and is only meaningful in sequence; a level is a state and is " +
                "self-describing, so replaying it is safe. Only levels should be Sticky. The names " +
                "below read as levels, which is a guess — the runtime diagnostic below is evidence.",
                levelCandidates));
        }

        private static bool LooksLikeALevel(string memberName)
        {
            for (int i = 0; i < LevelSuffixes.Length; i++)
                if (memberName.EndsWith(LevelSuffixes[i], StringComparison.Ordinal)) return true;
            return memberName.StartsWith("Is", StringComparison.Ordinal)
                || memberName.StartsWith("Has", StringComparison.Ordinal);
        }

        private static void CheckPayloadTypes(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            int total = 0, declared = 0;
            foreach (Type enumType in input.EventEnums)
            {
                if (enumType == null || !enumType.IsEnum) continue;
                foreach (FieldInfo member in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    total++;
                    if (member.IsDefined(typeof(EventPayloadAttribute), false)) declared++;
                }
            }

            if (total == 0) return;
            findings.Add(declared == 0
                ? new UpgradeFinding("7. Payloads", UpgradeSeverity.Suggestion,
                    $"None of the {total} event members declare [EventPayload], so every handler still " +
                    "casts an object and a wrong cast fails inside the handler. Declaring a type makes " +
                    "publishes and handlers compiler-checked, and lets the publisher report a mismatched " +
                    "payload from a raw-string publish. Start with events that already carry data.")
                : new UpgradeFinding("7. Payloads", UpgradeSeverity.Suggestion,
                    $"{declared} of {total} event members declare [EventPayload]. Events with no " +
                    "declaration stay unchecked, which is the intended default — migrate the ones that " +
                    "carry data."));
        }

        private static void CheckLateDeliveries(UpgradeAuditInput input, List<UpgradeFinding> findings)
        {
            if (!input.DiagnosticsHaveData)
            {
                findings.Add(new UpgradeFinding("6. Delivery (evidence)", UpgradeSeverity.Suggestion,
                    "No runtime data yet. Enter play mode and run the boot sequence, then re-run this " +
                    "audit: every event whose subscribers arrived too late to hear it will be listed, " +
                    "which turns the edge-or-level decision from a guess into a measurement."));
                return;
            }

            if (input.LateDeliveries.Count == 0)
            {
                findings.Add(new UpgradeFinding("6. Delivery (evidence)", UpgradeSeverity.Ok,
                    "No event was subscribed to after it had already been published during the recorded run."));
                return;
            }

            var details = new List<string>();
            foreach (EventsDiagnostics.Observation observation in input.LateDeliveries)
            {
                details.Add($"{observation.Name}  —  {observation.LateSubscribes} late subscriber(s), " +
                            $"{observation.Publishes} publish(es)");
            }

            findings.Add(new UpgradeFinding("6. Delivery (evidence)", UpgradeSeverity.Action,
                $"{details.Count} event(s) had a subscriber arrive after they were published, and it was " +
                "delivered nothing. These are the Sticky candidates, and the events any hand-maintained " +
                "bool mirror is compensating for. A late subscribe is evidence, not proof — an event may " +
                "be genuinely transient and its late subscriber genuinely uninterested.", details));
        }
    }
}

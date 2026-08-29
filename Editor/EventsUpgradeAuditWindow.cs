using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// Gathers what <see cref="EventsUpgradeAudit"/> reasons about, and shows the result.
    /// </summary>
    /// <remarks>
    /// <para>Everything here touches an editor API — <c>TypeCache</c>, the asset database, the file
    /// system, IMGUI — and none of it makes a decision. The decisions are in
    /// <see cref="EventsUpgradeAudit.Analyze"/>, which is why they are testable and this is not.</para>
    /// <para><b>Copy report</b> puts the findings on the clipboard as plain text. That is the intended
    /// route into an AI-assisted migration: run the audit in the project being upgraded, paste the
    /// report alongside <c>Documentation~/upgrade-prompt.md</c>, and the assistant works from what this
    /// project actually contains rather than from a guess about it.</para>
    /// </remarks>
    public class EventsUpgradeAuditWindow : EditorWindow
    {
        private const string PackageName = "com.crawfissoftware.eventspublisher";

        private List<UpgradeFinding> _findings;
        private Vector2 _scroll;

        [MenuItem("Window/Events/Upgrade Audit")]
        public static void Open()
        {
            GetWindow<EventsUpgradeAuditWindow>("Event Upgrade Audit").Refresh();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh")) Refresh();
                using (new EditorGUI.DisabledScope(_findings == null))
                {
                    if (GUILayout.Button("Copy report"))
                    {
                        EditorGUIUtility.systemCopyBuffer = BuildReport(_findings);
                        Debug.Log("Event upgrade audit copied to the clipboard.");
                    }
                }
            }

            if (_findings == null)
            {
                EditorGUILayout.HelpBox("Press Refresh to scan the project.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "Enter play mode and run the boot sequence before refreshing. The delivery-policy " +
                "evidence comes from what actually happened during a run.", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (UpgradeFinding finding in _findings)
            {
                EditorGUILayout.LabelField(finding.Step, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(finding.Summary, MessageTypeFor(finding.Severity));
                foreach (string detail in finding.Details)
                    EditorGUILayout.LabelField("    " + detail, EditorStyles.miniLabel);
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
        }

        private static MessageType MessageTypeFor(UpgradeSeverity severity)
        {
            switch (severity)
            {
                case UpgradeSeverity.Action: return MessageType.Warning;
                case UpgradeSeverity.Suggestion: return MessageType.Info;
                default: return MessageType.None;
            }
        }

        private void Refresh()
        {
            _findings = EventsUpgradeAudit.Analyze(Gather());
        }

        /// <summary>Collects the project's current state for the analysis.</summary>
        internal static UpgradeAuditInput Gather()
        {
            List<Type> singletons = CollectSingletonSubclasses();
            List<Type> eventEnums = CollectEventEnums(singletons);

            return new UpgradeAuditInput
            {
                EventEnums = eventEnums,
                SingletonSubclasses = singletons,
                SerializedStringFields = CollectSerializedStringFields(),
                SerializedEventNames = CollectSerializedEventNames(eventEnums),
                TestablesConfigured = IsPackageInTestables(),
                LateDeliveries = EventsDiagnostics.GetLateDeliveries(),
                DiagnosticsHaveData = EventsDiagnostics.GetAll().Count > 0,
            };
        }

        private static List<Type> CollectSingletonSubclasses()
        {
            var found = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom(typeof(EventsPublisherEnumsSingleton<>)))
                if (!type.IsAbstract && !EventsRegistry.IsTestAssembly(type.Assembly)) found.Add(type);
            return found;
        }

        /// <summary>
        /// Finds every enum acting as an event family, whether or not it is marked yet.
        /// </summary>
        /// <remarks>
        /// <para>Three sources, because a project part-way through the migration has families in
        /// each: already marked with the attribute, still behind a singleton subclass, and named in a
        /// <c>EventsFor&lt;T&gt;</c> type argument in source. The last needs a source scan — a type
        /// argument leaves no trace this tool can reflect over.</para>
        /// <para>Test assemblies are excluded from the two reflected sources, as they are from the
        /// runtime sweep. The audit advises a project on its own migration, and a fixture is not part
        /// of what there is to migrate: reporting one would be a finding no consumer could act on. The
        /// source scan needs no such filter — it reads <c>Assets</c> only.</para>
        /// </remarks>
        private static List<Type> CollectEventEnums(List<Type> singletons)
        {
            var found = new List<Type>();
            var seen = new HashSet<Type>();

            foreach (Type type in TypeCache.GetTypesWithAttribute<EventEnumAttribute>())
            {
                if (!EventsRegistry.IsTestAssembly(type.Assembly)) AddEnum(found, seen, type);
            }

            foreach (Type subclass in singletons)
            {
                for (Type baseType = subclass.BaseType; baseType != null; baseType = baseType.BaseType)
                {
                    if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(EventsPublisherEnumsSingleton<>))
                    {
                        AddEnum(found, seen, baseType.GetGenericArguments()[0]);
                        break;
                    }
                }
            }

            foreach (string name in ScanSourceForFacadeTypeArguments())
                AddEnum(found, seen, ResolveEnumByName(name));
            return found;
        }

        private static void AddEnum(List<Type> found, HashSet<Type> seen, Type type)
        {
            if (type != null && type.IsEnum && seen.Add(type)) found.Add(type);
        }

        private static readonly Regex FacadeUsage =
            new Regex(@"EventsFor\s*<\s*([\w\.]+)\s*>", RegexOptions.Compiled);

        private static HashSet<string> ScanSourceForFacadeTypeArguments()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in SafeEnumerateFiles("Assets", "*.cs"))
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }

                foreach (Match match in FacadeUsage.Matches(text)) names.Add(match.Groups[1].Value);
            }
            return names;
        }

        private static Type ResolveEnumByName(string name)
        {
            string simpleName = name.Substring(name.LastIndexOf('.') + 1);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }

                foreach (Type type in types)
                    if (type != null && type.IsEnum && type.Name == simpleName) return type;
            }
            return null;
        }

        private static List<FieldInfo> CollectSerializedStringFields()
        {
            var fields = new List<FieldInfo>();
            var types = new List<Type>();
            types.AddRange(TypeCache.GetTypesDerivedFrom<MonoBehaviour>());
            types.AddRange(TypeCache.GetTypesDerivedFrom<ScriptableObject>());

            foreach (Type type in types)
            {
                if (type == null || type.Assembly == typeof(EventsUpgradeAudit).Assembly) continue;
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType != typeof(string) && field.FieldType != typeof(List<string>)) continue;
                    bool serialized = field.IsPublic
                        ? !field.IsDefined(typeof(NonSerializedAttribute), false)
                        : field.IsDefined(typeof(SerializeField), false);
                    if (serialized) fields.Add(field);
                }
            }
            return fields;
        }

        // A serialized string in Unity YAML: two spaces or more, a key, a colon, then the value.
        private static readonly Regex YamlEntry = new Regex(@"^\s+(\w+):\s*(\S.*?)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Finds serialized fields whose value is a known event name, by reading the assets directly.
        /// </summary>
        /// <remarks>This is the one check with no heuristic in it. A field called <c>_trigger</c> holding
        /// "GameFlowEvents/GameStarting" is found here and would be missed by any amount of guessing from
        /// field names. Requires text-serialized assets; a project set to force-binary serialization gets
        /// nothing from this and falls back to the name-based guesses.</remarks>
        private static List<SerializedEventName> CollectSerializedEventNames(List<Type> eventEnums)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type enumType in eventEnums)
            {
                string prefix = EventsRegistry.GetPrefix(enumType);
                foreach (string member in Enum.GetNames(enumType)) known.Add(prefix + "/" + member);
            }

            var found = new List<SerializedEventName>();
            if (known.Count == 0) return found;

            foreach (string path in SafeEnumerateFiles("Assets", "*.unity", "*.prefab", "*.asset"))
            {
                string[] lines;
                try { lines = File.ReadAllLines(path); }
                catch (IOException) { continue; }

                foreach (string line in lines)
                {
                    Match match = YamlEntry.Match(line);
                    if (match.Success && known.Contains(match.Groups[2].Value))
                        found.Add(new SerializedEventName(path, match.Groups[1].Value, match.Groups[2].Value));
                }
            }
            return found;
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, params string[] patterns)
        {
            if (!Directory.Exists(root)) yield break;
            foreach (string pattern in patterns)
            {
                string[] paths;
                try { paths = Directory.GetFiles(root, pattern, SearchOption.AllDirectories); }
                catch (Exception) { continue; }
                foreach (string path in paths) yield return path;
            }
        }

        private static bool IsPackageInTestables()
        {
            try
            {
                string manifest = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Packages/manifest.json");
                return File.Exists(manifest)
                    && EventsUpgradeAudit.IsListedInTestables(File.ReadAllText(manifest), PackageName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Renders the findings as plain text, for pasting into an assistant or an issue.</summary>
        internal static string BuildReport(IReadOnlyList<UpgradeFinding> findings)
        {
            var report = new StringBuilder();
            report.AppendLine("EventsPublisher upgrade audit");
            report.AppendLine();
            foreach (UpgradeFinding finding in findings)
            {
                report.AppendLine($"[{finding.Severity}] {finding.Step}: {finding.Summary}");
                foreach (string detail in finding.Details) report.AppendLine("    - " + detail);
                report.AppendLine();
            }
            return report.ToString();
        }
    }
}

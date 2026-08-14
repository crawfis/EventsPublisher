using System;
using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// Renders an event-name string as a dropdown of known events.
    /// </summary>
    /// <remarks>Shared by <see cref="EventNameDrawer"/> and <see cref="EventRefDrawer"/>, which differ
    /// only in where the string lives.</remarks>
    internal static class EventNamePopup
    {
        private const string NoneLabel = "<none>";

        /// <summary>
        /// Draws the dropdown for a string property, writing back only when the user picks something.
        /// </summary>
        internal static void Draw(Rect position, SerializedProperty stringProperty, GUIContent label)
        {
            if (stringProperty == null || stringProperty.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use [EventName] on a string field.");
                return;
            }

            string current = stringProperty.stringValue;
            string[] options = BuildOptions(EventNameCatalog.Names, current, out int selected);

            var contents = new GUIContent[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                // Unity renders '/' in popup entries as nested submenus, which groups the list by enum
                // family without needing a second control.
                contents[i] = new GUIContent(options[i]);
            }

            EditorGUI.BeginProperty(position, label, stringProperty);
            int picked = EditorGUI.Popup(position, label, selected, contents);
            EditorGUI.EndProperty();

            // Only write on an actual change. Re-writing the current value would dirty every scene the
            // Inspector merely displayed, and would destroy an unrecognised value on first paint.
            if (picked != selected)
            {
                stringProperty.stringValue = picked == 0 ? string.Empty : options[picked];
            }
        }

        /// <summary>
        /// Builds the option list and resolves which entry the current value corresponds to.
        /// </summary>
        /// <remarks>An unrecognised non-empty value gets its own entry and is selected, so a name whose
        /// enum is not marked <see cref="EventEnumAttribute"/> — or has since been renamed — is surfaced
        /// rather than silently replaced with the first alphabetical event.</remarks>
        internal static string[] BuildOptions(string[] known, string current, out int selectedIndex)
        {
            var options = new List<string> { NoneLabel };
            options.AddRange(known ?? Array.Empty<string>());

            if (string.IsNullOrEmpty(current))
            {
                selectedIndex = 0;
                return options.ToArray();
            }

            int index = options.IndexOf(current);
            if (index >= 0)
            {
                selectedIndex = index;
                return options.ToArray();
            }

            options.Add("Missing/" + current);
            selectedIndex = options.Count - 1;
            return options.ToArray();
        }
    }

    /// <summary>Dropdown for a <see cref="string"/> field marked <see cref="EventNameAttribute"/>.</summary>
    [CustomPropertyDrawer(typeof(EventNameAttribute))]
    internal sealed class EventNameDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EventNamePopup.Draw(position, property, label);
        }
    }

    /// <summary>Dropdown for an <see cref="EventRef"/> field.</summary>
    [CustomPropertyDrawer(typeof(EventRef))]
    internal sealed class EventRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EventNamePopup.Draw(position, property.FindPropertyRelative("_eventName"), label);
        }
    }
}

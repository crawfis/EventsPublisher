using CrawfisSoftware.Events;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System;

namespace CrawfisSoftware.Events.Editor
{
    public class EventMenu : EditorWindow
    {
        private const string MENU_LOCATION = "CrawfisSoftware/Events/Event Publisher Menu";
        private static int eventToPublish = 0;

        [MenuItem(MENU_LOCATION, false, 0)]
        static void OpenWindow()
        {
            // Get existing open window or if none, make a new one:
            EventMenu window = (EventMenu)GetWindow(typeof(EventMenu), false, "Event Publisher", true);
            window.Show();
        }

        public void OnGUI()
        {
            var eventCollection = EventsPublisher.Instance.GetRegisteredEvents();
            string[] eventNameArray = eventCollection.ToArray();
            Array.Sort<string>(eventNameArray, StringComparer.OrdinalIgnoreCase);
            eventToPublish = EditorGUILayout.Popup(eventToPublish, eventNameArray);
            if (GUILayout.Button("Publish Event"))
            {
                EventsPublisher.Instance.PublishEvent(eventNameArray[eventToPublish], this, null);
            }
        }
    }
}

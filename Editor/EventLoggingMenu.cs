﻿using CrawfisSoftware.Events;

using UnityEditor;

using UnityEngine;

namespace CrawfisSoftware.Events.Editor
{
    public class EventLoggingMenu : EditorWindow
    {
        private const string MENU_LOCATION = "CrawfisSoftware/Events/Event Logging Enabled";
        private const string SettingName = "DoLogEvents";

        public static bool IsEnabled
        {
            get { return SessionState.GetBool(SettingName, false); }
            set { SessionState.SetBool(SettingName, value); ManageSubscription(); }
        }

        [MenuItem(MENU_LOCATION)]
        private static void ToggleAction()
        {
            IsEnabled = !IsEnabled;
        }

        [MenuItem(MENU_LOCATION, true)]
        private static bool ToggleActionValidate()
        {
            Menu.SetChecked(MENU_LOCATION, IsEnabled);
            return true;
        }

        [InitializeOnEnterPlayMode]
        private static void OnPlay()
        {
            if (IsEnabled)
            {
                EventsPublisher.Instance.SubscribeToAllEvents(OnEvent);
            }
        }

        private static void ManageSubscription()
        {
            if (Application.isPlaying)
            {
                if (IsEnabled)
                {
                    EventsPublisher.Instance.SubscribeToAllEvents(OnEvent);
                }
                else
                {
                    EventsPublisher.Instance.UnsubscribeToAllEvents(OnEvent);
                }
            }
        }

        private static void OnEvent(string eventName, object sender, object data)
        {
            Debug.Log($"<color=cyan>[{eventName}]</color> published by: {sender?.ToString()}\nData: {data}");
        }
    }
}

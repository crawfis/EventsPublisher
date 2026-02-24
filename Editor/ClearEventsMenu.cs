using UnityEditor;

namespace CrawfisSoftware.Events.Editor
{
    [InitializeOnLoad]
    public class ClearEventsMenu : EditorWindow
    {
        private const string CLEAR_NOW_MENU_LOCATION = "CrawfisSoftware/Events/Clear Now";
        private const string LIST_SUBSCRIBERS_MENU_LOCATION = "CrawfisSoftware/Events/List Current Subscribers";
        private const string TOGGLE_MENU_LOCATION = "CrawfisSoftware/Events/Clear Events on Exiting Play Mode";
        private const string SettingName = "DoClearEvents";

        public static bool IsEnabled
        {
            get { return SessionState.GetBool(SettingName, false); }
            set { SessionState.SetBool(SettingName, value); }
        }


        [MenuItem(CLEAR_NOW_MENU_LOCATION)]
        private static void Clear()
        {
            EventsPublisher.Instance.Clear();
        }

        [MenuItem(LIST_SUBSCRIBERS_MENU_LOCATION)]
        private static void ListSubscribers()
        {
            UnityEngine.Debug.Log("Listing all current subscribers ...");
            EventsPublisher publisher = (EventsPublisher)(EventsPublisher.Instance);
            int count = 0;
            foreach ((string eventName, string targetName) subscriberData in publisher.GetSubscribers())
            {
                UnityEngine.Debug.Log($"{subscriberData.targetName} is subscribed to {subscriberData.eventName}.");
                count++;
            }
            UnityEngine.Debug.Log($"   ....  {count} subscribers listed");
        }

        [MenuItem(TOGGLE_MENU_LOCATION, true)]
        private static bool ToggleActionValidate()
        {
            Menu.SetChecked(TOGGLE_MENU_LOCATION, IsEnabled);
            return true;
        }

        static void EventLoggingMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode && IsEnabled)
            {
                EventsPublisher.Instance.Clear();
            }
        }
    }
}
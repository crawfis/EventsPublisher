using System;

using UnityEngine;

namespace CrawfisSoftware.Events
{
    public class EventsPublisherEnumsSingleton<T> : MonoBehaviour where T : Enum
    {
        public static EventsPublisherEnumsSingleton<T> Instance { get; private set; }
        private EventsPublisherEnums<T> _eventsPublisher;
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _eventsPublisher = new EventsPublisherEnums<T>(EventsPublisher.Instance);
            RegisterKnownEvents();
        }
        public void PublishEvent(T eventEnum, object sender, object data)
        {
            _eventsPublisher.PublishEvent(eventEnum, sender, data);
        }

        public void SubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            _eventsPublisher.SubscribeToEvent(eventEnum, callback);
        }

        public void UnsubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            _eventsPublisher.UnsubscribeToEvent(eventEnum, callback);
        }
        private static void RegisterKnownEvents()
        {
            foreach (T eventEnum in Enum.GetValues(typeof(T)))
            {
                string eventName = eventEnum.ToString();
                EventsPublisher.Instance.RegisterEvent(eventName);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void ResetOnPlayMode()
        {
            //#if UNITY_EDITOR
            Instance = null;
            //#endif
        }
    }
}
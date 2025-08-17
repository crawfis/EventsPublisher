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

        /// <summary>
        /// Publishes an event to all registered subscribers.
        /// </summary>
        /// <param name="eventEnum">The event identifier of type <typeparamref name="T"/> that specifies the event to be published.</param>
        /// <param name="sender">The source of the event. This parameter can be used to identify the origin of the event.</param>
        /// <param name="data">The data associated with the event. This can be any object containing information relevant to the event.</param>
        public void PublishEvent(T eventEnum, object sender, object data)
        {
            _eventsPublisher.PublishEvent(eventEnum, sender, data);
        }

        /// <summary>
        /// Subscribes to the specified event and associates it with a callback to handle the event.
        /// </summary>
        /// <remarks>This method allows you to register a handler for a specific event. Ensure that the
        /// callback is thread-safe if the event may be triggered from multiple threads.</remarks>
        /// <param name="eventEnum">The event to subscribe to, represented as an enumeration value of type <typeparamref name="T"/>.</param>
        /// <param name="callback">The callback to invoke when the event is triggered. The callback receives three parameters: <list
        /// type="bullet"> <item><description>A <see cref="string"/> representing the event name.</description></item>
        /// <item><description>An <see cref="object"/> representing the event's primary data.</description></item>
        /// <item><description>An <see cref="object"/> representing additional context or metadata for the
        /// event.</description></item> </list></param>
        public void SubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            _eventsPublisher.SubscribeToEvent(eventEnum, callback);
        }

        /// <summary>
        /// Unsubscribes the specified callback from the event associated with the given event enumeration value.
        /// </summary>
        /// <remarks>If the specified callback is not currently subscribed to the event, this method has
        /// no effect.</remarks>
        /// <param name="eventEnum">The event enumeration value representing the event to unsubscribe from.</param>
        /// <param name="callback">The callback to be removed from the event's subscription list. This callback will no longer be invoked when
        /// the event is triggered.</param>
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
        private static void ResetOnPlayMode()
        {
            //#if UNITY_EDITOR
            Instance = null;
            //#endif
        }
    }
}
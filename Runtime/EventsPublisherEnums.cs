using System;
using System.Collections.Generic;

using UnityEngine.UIElements;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Provides a strongly-typed wrapper for publishing and subscribing to events using an enumeration as the event
    /// identifier.
    /// </summary>
    /// <remarks>This class simplifies event management by allowing events to be identified using enumeration
    /// values instead of strings. It delegates the actual event publishing and subscription logic to an underlying <see
    /// cref="IEventsPublisher{T}"/> implementation.</remarks>
    /// <typeparam name="T">The enumeration type used to identify events. Must be a type derived from <see cref="System.Enum"/>.</typeparam>
    public class EventsPublisherEnums<T> where T : Enum
    {
        private IEventsPublisher<string> _eventsPublisher;
        private Dictionary<T, string> _eventEnumToStringMap = new Dictionary<T, string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="EventsPublisherEnums"/> class with the specified events
        /// publisher.
        /// </summary>
        /// <remarks>This constructor allows dependency injection of an <see cref="IEventsPublisher{T}"/>
        /// implementation to enable event publishing functionality for the class.</remarks>
        /// <param name="eventsPublisher">The events publisher used to publish events. The publisher must handle events of type <see cref="string"/>.</param>
        public EventsPublisherEnums(IEventsPublisher<string> eventsPublisher)
        {
            _eventsPublisher = eventsPublisher;
            var enumType = typeof(T);
            string enumName = enumType.Name;
            foreach (T eventEnum in Enum.GetValues(typeof(T)))
            {
                _eventEnumToStringMap[eventEnum] = enumName + "/" + eventEnum.ToString();
            }
        }

        /// <summary>
        /// Publishes an event with the specified event type, sender, and associated data.
        /// </summary>
        /// <remarks>The event type is determined by the string representation of <paramref
        /// name="eventEnum"/>. This method delegates the event publishing to an internal event publisher.</remarks>
        /// <param name="eventEnum">The event type to publish. This is typically an enumeration value representing the event.</param>
        /// <param name="sender">The source of the event. This can be any object that provides context about the event's origin.</param>
        /// <param name="data">The data associated with the event. This can be any object containing information relevant to the event.</param>
        public void PublishEvent(T eventEnum, object sender, object data)
        {
            string eventName = _eventEnumToStringMap[eventEnum];
            _eventsPublisher.PublishEvent(eventName, sender, data);
        }

        /// <summary>
        /// Subscribes to the specified event and registers a callback to be invoked when the event is published.
        /// </summary>
        /// <remarks>The callback will be invoked whenever the specified event is published. Ensure that
        /// the <paramref name="callback"/> is thread-safe if the event may be triggered from multiple
        /// threads.</remarks>
        /// <param name="eventEnum">The event to subscribe to, represented as an enumeration value.</param>
        /// <param name="callback">The callback to execute when the event is triggered. The callback receives three parameters: the event name
        /// as a <see cref="string"/>, the event's primary data as an <see cref="object"/>, and additional event data as
        /// an <see cref="object"/>.</param>
        public void SubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            string eventName = _eventEnumToStringMap[eventEnum];
            _eventsPublisher.SubscribeToEvent(eventName, callback);
        }

        /// <summary>
        /// Unsubscribes the specified callback from the event associated with the given event enumeration value.
        /// </summary>
        /// <remarks>If the specified callback is not currently subscribed to the event, this method has
        /// no effect.</remarks>
        /// <param name="eventEnum">The enumeration value representing the event to unsubscribe from.</param>
        /// <param name="callback">The callback to be removed from the event's subscription list.  This callback will no longer be invoked when
        /// the event is triggered.</param>
        public void UnsubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            string eventName = _eventEnumToStringMap[eventEnum];
            _eventsPublisher.UnsubscribeToEvent(eventName, callback);
        }

        internal void RegisterKnownEvents()
        {
            foreach(string eventName in _eventEnumToStringMap.Values)
            {
                EventsPublisher.Instance.RegisterEvent(eventName);
            }
        }
    }
}
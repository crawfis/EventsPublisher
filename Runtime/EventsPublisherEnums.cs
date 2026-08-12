using System;
using System.Collections.Generic;

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
        private readonly IEventsPublisher<string> _eventsPublisher;
        private readonly Dictionary<T, string> _eventEnumToStringMap = new Dictionary<T, string>();
        private readonly Dictionary<string, T> _eventStringToEnumMap = new Dictionary<string, T>();

        // Which enum type has already claimed a given "TypeName/" prefix. Two enum types with the same
        // simple name (in different namespaces) project onto the same event names and would collide.
        private static readonly Dictionary<string, Type> _claimedPrefixes = new Dictionary<string, Type>();

        /// <summary>
        /// Initializes a new instance of the <see cref="EventsPublisherEnums"/> class with the specified events Enum/EnumName
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
            WarnOnPrefixCollision(enumName, enumType);
            foreach (T eventEnum in Enum.GetValues(typeof(T)))
            {
                string eventName = enumName + "/" + eventEnum.ToString();
                _eventEnumToStringMap[eventEnum] = eventName;
                _eventStringToEnumMap[eventName] = eventEnum;
            }
        }

        /// <summary>
        /// Reports two enum types whose simple names collide. Event names are projected from
        /// <see cref="Type.Name"/>, not <see cref="Type.FullName"/>, so two same-named enums in different
        /// namespaces would silently share every event.
        /// </summary>
        private static void WarnOnPrefixCollision(string enumName, Type enumType)
        {
            if (_claimedPrefixes.TryGetValue(enumName, out Type existing))
            {
                if (existing != enumType)
                {
                    UnityEngine.Debug.LogError(
                        $"EventsPublisherEnums: '{enumType.FullName}' and '{existing.FullName}' both project onto the " +
                        $"event name prefix '{enumName}/'. Their events will collide. Rename one of the enum types.");
                }
                return;
            }
            _claimedPrefixes[enumName] = enumType;
        }

        /// <summary>
        /// Gets the published event name for <paramref name="eventEnum"/>.
        /// </summary>
        public string GetEventName(T eventEnum)
        {
            return _eventEnumToStringMap[eventEnum];
        }

        /// <summary>
        /// Recovers the enum value for a published event name, without allocating.
        /// </summary>
        /// <remarks>Use this in "all events" handlers instead of slicing the name and calling
        /// <see cref="Enum.Parse{T}(string)"/>, which allocates a string and does a reflection-backed
        /// lookup on every published event.</remarks>
        /// <param name="eventName">The published event name, e.g. "GameFlowEvents/GameStarting".</param>
        /// <param name="eventEnum">The matching enum value, or the default when the name is not from this enum.</param>
        /// <returns><see langword="true"/> if the name belongs to <typeparamref name="T"/>.</returns>
        public bool TryGetEnum(string eventName, out T eventEnum)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                eventEnum = default;
                return false;
            }
            return _eventStringToEnumMap.TryGetValue(eventName, out eventEnum);
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
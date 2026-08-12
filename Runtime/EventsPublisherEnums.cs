using System;
using System.Collections.Generic;
using System.Reflection;

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
            EventsRegistry.ClaimPrefix(enumName, enumType);
            foreach (T eventEnum in Enum.GetValues(typeof(T)))
            {
                string eventName = enumName + "/" + eventEnum.ToString();
                _eventEnumToStringMap[eventEnum] = eventName;
                _eventStringToEnumMap[eventName] = eventEnum;
            }
            // Before any publish can happen through this facade, so the first publish is retained.
            DeclareDeliveryPolicies(enumType);
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
            // Registers with the publisher this instance was constructed against, rather than reaching
            // for EventsPublisher.Instance, so an injected publisher is honoured.
            foreach (string eventName in _eventEnumToStringMap.Values)
            {
                _eventsPublisher.RegisterEvent(eventName);
            }
        }

        /// <summary>
        /// Reads <see cref="EventDeliveryAttribute"/> off each member of <typeparamref name="T"/> and
        /// declares the policies.
        /// </summary>
        /// <remarks>Done once, here, rather than per publish: the reflection cost is paid at
        /// construction and the publisher then does a dictionary lookup per event.</remarks>
        private void DeclareDeliveryPolicies(Type enumType)
        {
            foreach (KeyValuePair<T, string> entry in _eventEnumToStringMap)
            {
                FieldInfo member = enumType.GetField(entry.Key.ToString(), BindingFlags.Public | BindingFlags.Static);
                if (member == null) continue;
                var attribute = (EventDeliveryAttribute)Attribute.GetCustomAttribute(member, typeof(EventDeliveryAttribute));
                if (attribute == null) continue;
                EventsRegistry.DeclarePolicy(entry.Value, attribute.Delivery);
            }
        }

        /// <summary>
        /// Returns the most recently retained value for <paramref name="eventEnum"/>, if there is one.
        /// </summary>
        public bool TryGetLast(T eventEnum, out object sender, out object data)
        {
            return _eventsPublisher.TryGetLast(_eventEnumToStringMap[eventEnum], out sender, out data);
        }
    }
}
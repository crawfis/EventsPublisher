using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    public interface IEventsPublisher<T>
    {
        /// <summary>
        /// Clears all items from the collection.
        /// </summary>
        /// <remarks>After calling this method, the collection will be empty.  This operation may affect
        /// any iterators or references to the collection's items.</remarks>
        void Clear();

        /// <summary>
        /// Retrieves a collection of registered events of type <typeparamref name="T"/>.
        /// </summary>
        /// <returns>An <see cref="IEnumerable{T}"/> containing the registered events.  The collection will be empty if no events
        /// are registered.</returns>
        IEnumerable<T> GetRegisteredEvents();

        /// <summary>
        /// Publishes an event to notify subscribers with the specified event name, sender, and associated data.
        /// </summary>
        /// <remarks>This method triggers all subscribers registered for the specified event name. Ensure
        /// that the <paramref name="eventName"/> is valid and that  <paramref name="sender"/> and <paramref
        /// name="data"/> are provided as needed by the subscribers.</remarks>
        /// <param name="eventName">The name of the event to publish. This identifies the event to which subscribers are listening.</param>
        /// <param name="sender">The source of the event. This typically represents the object that raised the event.</param>
        /// <param name="data">The data associated with the event. This can be used to pass additional information to subscribers.</param>
        void PublishEvent(T eventName, object sender, object data);

        /// <summary>
        /// Registers an event with the specified name.
        /// </summary>
        /// <param name="eventName">The name of the event to register. Cannot be null or empty.</param>
        void RegisterEvent(T eventName);

        /// <summary>
        /// Registers an event and declares its <see cref="EventDelivery"/> policy.
        /// </summary>
        /// <remarks>The escape hatch for names no enum can annotate — runtime-computed names and
        /// Inspector-authored strings. For an enum family, prefer <see cref="EventDeliveryAttribute"/>
        /// on the member. Declare from a static initializer rather than <c>Awake</c>: the policy must
        /// be in place before the event is first published, or that first publish is not retained.
        /// First declaration wins; a differing second one is reported and ignored.</remarks>
        void RegisterEvent(T eventName, EventDelivery delivery);

        /// <summary>
        /// Retrieves the most recently retained value for an event, without subscribing to it.
        /// </summary>
        /// <remarks>Only events declared <see cref="EventDelivery.Sticky"/> or
        /// <see cref="EventDelivery.Replay"/> retain anything; a
        /// <see cref="EventDelivery.Transient"/> event always reports <see langword="false"/>. Use this
        /// for code that wants the current value but has no reason to subscribe — a one-shot read, a
        /// non-<c>MonoBehaviour</c>, an editor tool. Subscribers do not need it: a subscription to a
        /// retaining event is delivered the retained value immediately.</remarks>
        /// <param name="eventName">The event to query.</param>
        /// <param name="sender">The sender recorded with the retained value, or <see langword="null"/>.</param>
        /// <param name="data">The data recorded with the retained value, or <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if a value was retained.</returns>
        bool TryGetLast(T eventName, out object sender, out object data);

        /// <summary>
        /// Subscribes to all events and invokes the specified callback when an event occurs.
        /// </summary>
        /// <remarks>The callback is invoked for every event without filtering. Ensure the callback
        /// implementation is efficient to avoid performance issues when handling a high volume of events.</remarks>
        /// <param name="callback">A delegate to be invoked when an event is triggered. The callback receives three parameters: the source of
        /// the event, the event data, and an additional context object.</param>
        void SubscribeToAllEvents(Action<T, object, object> callback);

        /// <summary>
        /// Subscribes a callback method to the specified event.
        /// </summary>
        /// <remarks>The callback will be executed whenever the specified event is raised. Ensure that the
        /// callback method is thread-safe if the event may be triggered from multiple threads.</remarks>
        /// <param name="eventName">The event to which the callback will be subscribed.</param>
        /// <param name="callback">The method to invoke when the event is triggered. The callback receives the event name, the sender of the
        /// event, and an additional event-specific argument.</param>
        void SubscribeToEvent(T eventName, Action<T, object, object> callback);

        /// <summary>
        /// Unsubscribes the specified callback from all events associated with the current instance.
        /// </summary>
        /// <remarks>If the specified callback is not currently subscribed to any events, this method has
        /// no effect.</remarks>
        /// <param name="callback">The callback to be unsubscribed. This delegate is invoked with the event source, the event arguments, and
        /// additional context.</param>
        void UnsubscribeToAllEvents(Action<T, object, object> callback);

        /// <summary>
        /// Unsubscribes a callback from the specified event.
        /// </summary>
        /// <remarks>If the specified callback is not currently subscribed to the event, this method has
        /// no effect.</remarks>
        /// <param name="eventName">The name of the event to unsubscribe from.</param>
        /// <param name="callback">The callback to be removed from the event's subscription list.  This must match the callback previously
        /// subscribed to the event.</param>
        void UnsubscribeToEvent(T eventName, Action<T, object, object> callback);
    }
}
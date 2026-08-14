using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// <see cref="EventRef"/> overloads for the publisher, so a component whose event is authored in the
    /// Inspector never has to reach for the underlying string.
    /// </summary>
    /// <remarks>
    /// <para>Extension methods rather than interface members, so no existing implementer of
    /// <see cref="IEventsPublisher{T}"/> has to change.</para>
    /// <para>These exist because the raw-string API cannot be closed off. A component that publishes a
    /// name chosen in the Inspector is holding that name as data, not as a literal, and must still hand
    /// a string to the publisher at runtime. The hazard was never the string API itself — it was the
    /// name being typed by hand with nothing to check it. <see cref="EventRef"/> and
    /// <see cref="EventNameAttribute"/> close that at the authoring step; these overloads keep the
    /// call site from having to unwrap the value again.</para>
    /// </remarks>
    public static class EventsPublisherExtensions
    {
        /// <summary>Publishes the event an <see cref="EventRef"/> points at. Ignored when unset.</summary>
        public static void PublishEvent(this IEventsPublisher<string> publisher, EventRef eventRef, object sender, object data)
        {
            if (publisher == null || eventRef.IsEmpty) return;
            publisher.PublishEvent(eventRef.Name, sender, data);
        }

        /// <summary>Subscribes to the event an <see cref="EventRef"/> points at. Ignored when unset.</summary>
        public static void SubscribeToEvent(this IEventsPublisher<string> publisher, EventRef eventRef, Action<string, object, object> callback)
        {
            if (publisher == null || eventRef.IsEmpty) return;
            publisher.SubscribeToEvent(eventRef.Name, callback);
        }

        /// <summary>Unsubscribes from the event an <see cref="EventRef"/> points at. Ignored when unset.</summary>
        /// <remarks>The unset check here is defence in depth rather than the thing that prevents a
        /// crash — the publisher already rejects null and empty names, so removing this guard changes
        /// no observable behaviour. It avoids the pointless call, and keeps every overload on this type
        /// reading the same way.</remarks>
        public static void UnsubscribeToEvent(this IEventsPublisher<string> publisher, EventRef eventRef, Action<string, object, object> callback)
        {
            if (publisher == null || eventRef.IsEmpty) return;
            publisher.UnsubscribeToEvent(eventRef.Name, callback);
        }

        /// <summary>Registers the event an <see cref="EventRef"/> points at, with a delivery policy.</summary>
        public static void RegisterEvent(this IEventsPublisher<string> publisher, EventRef eventRef, EventDelivery delivery)
        {
            if (publisher == null || eventRef.IsEmpty) return;
            publisher.RegisterEvent(eventRef.Name, delivery);
        }

        /// <summary>Reads the retained value for the event an <see cref="EventRef"/> points at.</summary>
        public static bool TryGetLast(this IEventsPublisher<string> publisher, EventRef eventRef, out object sender, out object data)
        {
            if (publisher == null || eventRef.IsEmpty)
            {
                sender = null;
                data = null;
                return false;
            }
            return publisher.TryGetLast(eventRef.Name, out sender, out data);
        }
    }
}

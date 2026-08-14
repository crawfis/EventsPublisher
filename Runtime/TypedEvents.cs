using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// The publish and subscribe operations for a typed identity.
    /// </summary>
    /// <remarks>
    /// <para>Extension methods rather than members of <see cref="EventId{TData}"/>, so that the struct
    /// stays a pure identity and does not carry a dependency on a particular publisher.</para>
    /// <para>Each operation has an overload taking an explicit <see cref="IEventsPublisher{T}"/>, for a
    /// pushed frame or an injected publisher; the short form uses
    /// <see cref="EventsPublisher.Instance"/>.</para>
    /// </remarks>
    public static class EventIdExtensions
    {
        /// <summary>Publishes a typed payload. Passing the wrong type is a compile error.</summary>
        public static void Publish<TData>(this EventId<TData> eventId, object sender, TData data)
        {
            eventId.Publish(EventsPublisher.Instance, sender, data);
        }

        /// <inheritdoc cref="Publish{TData}(EventId{TData}, object, TData)"/>
        public static void Publish<TData>(this EventId<TData> eventId, IEventsPublisher<string> publisher, object sender, TData data)
        {
            if (publisher == null || !eventId.IsValid) return;
            if (publisher is IEventIdPublisher idPublisher) idPublisher.PublishEvent(eventId.Untyped, sender, data);
            else publisher.PublishEvent(eventId.Name, sender, data);
        }

        /// <summary>
        /// Subscribes a handler whose <c>data</c> parameter is <typeparamref name="TData"/>, with no cast.
        /// </summary>
        /// <remarks>The handler is wrapped in an erased one for the publisher, and the wrapper is
        /// remembered so that <see cref="Unsubscribe{TData}(EventId{TData}, Action{string, object, TData})"/>
        /// removes the same delegate rather than a fresh one that would match nothing.</remarks>
        public static void Subscribe<TData>(this EventId<TData> eventId, Action<string, object, TData> callback)
        {
            eventId.Subscribe(EventsPublisher.Instance, callback);
        }

        /// <inheritdoc cref="Subscribe{TData}(EventId{TData}, Action{string, object, TData})"/>
        public static void Subscribe<TData>(this EventId<TData> eventId, IEventsPublisher<string> publisher, Action<string, object, TData> callback)
        {
            if (publisher == null || !eventId.IsValid || callback == null) return;

            Action<string, object, object> wrapper = TypedSubscriptions.Acquire(eventId.Untyped, callback);
            if (publisher is IEventIdPublisher idPublisher) idPublisher.SubscribeToEvent(eventId.Untyped, wrapper);
            else publisher.SubscribeToEvent(eventId.Name, wrapper);
        }

        /// <summary>Unsubscribes a handler subscribed through the typed API.</summary>
        public static void Unsubscribe<TData>(this EventId<TData> eventId, Action<string, object, TData> callback)
        {
            eventId.Unsubscribe(EventsPublisher.Instance, callback);
        }

        /// <inheritdoc cref="Unsubscribe{TData}(EventId{TData}, Action{string, object, TData})"/>
        public static void Unsubscribe<TData>(this EventId<TData> eventId, IEventsPublisher<string> publisher, Action<string, object, TData> callback)
        {
            if (publisher == null || !eventId.IsValid || callback == null) return;

            Action<string, object, object> wrapper = TypedSubscriptions.Release(eventId.Untyped, callback);
            if (wrapper == null) return; // Never subscribed through the typed API; a no-op, as untyped is.

            if (publisher is IEventIdPublisher idPublisher) idPublisher.UnsubscribeToEvent(eventId.Untyped, wrapper);
            else publisher.UnsubscribeToEvent(eventId.Name, wrapper);
        }

        /// <summary>Registers a typed event, with a delivery policy, without subscribing to it.</summary>
        /// <remarks>Needed only for an identity minted by <see cref="EventId{TData}.Of"/>; an identity
        /// from <see cref="EventsFor{T}.Id{TData}"/> is registered along with the rest of its family.</remarks>
        public static void Register<TData>(this EventId<TData> eventId, EventDelivery delivery = EventDelivery.Transient)
        {
            if (!eventId.IsValid) return;
            EventsPublisher.Instance.RegisterEvent(eventId.Name, delivery);
        }

        /// <summary>
        /// Reads the retained value for a typed event, without subscribing.
        /// </summary>
        /// <returns><see langword="false"/> when nothing is retained, and also when what is retained is
        /// not a <typeparamref name="TData"/> — which an untyped publish can leave behind.</returns>
        public static bool TryGetLast<TData>(this EventId<TData> eventId, out object sender, out TData data)
        {
            return eventId.TryGetLast(EventsPublisher.Instance, out sender, out data);
        }

        /// <inheritdoc cref="TryGetLast{TData}(EventId{TData}, out object, out TData)"/>
        public static bool TryGetLast<TData>(this EventId<TData> eventId, IEventsPublisher<string> publisher, out object sender, out TData data)
        {
            data = default(TData);
            sender = null;
            if (publisher == null || !eventId.IsValid) return false;

            object raw;
            bool found = publisher is IEventIdPublisher idPublisher
                ? idPublisher.TryGetLast(eventId.Untyped, out sender, out raw)
                : publisher.TryGetLast(eventId.Name, out sender, out raw);
            if (!found) return false;

            if (raw is TData typed)
            {
                data = typed;
                return true;
            }
            if (raw == null && TypedSubscriptions.AcceptsNull<TData>()) return true;

            TypedSubscriptions.ReportMismatch(eventId.Name, typeof(TData), raw, "reading the retained value for");
            sender = null;
            return false;
        }
    }

    /// <summary>
    /// Remembers the erased wrapper standing in for each typed handler, so that unsubscribing removes
    /// the delegate that was actually subscribed.
    /// </summary>
    /// <remarks>
    /// <para>Without this, wrapping a typed handler at subscribe time would be a silent leak: the
    /// wrapper is a new delegate each time, so a later <c>Unsubscribe</c> would build a second wrapper,
    /// hand it to <c>-=</c>, match nothing, and leave the original subscribed forever.</para>
    /// <para>Keyed on <c>(EventId, handler)</c> and reference-counted, so that subscribing the same
    /// handler twice fires it twice and takes two unsubscribes to remove — the same semantics the
    /// untyped <c>+=</c> and <c>-=</c> already have.</para>
    /// </remarks>
    internal static class TypedSubscriptions
    {
        private sealed class Entry
        {
            public Action<string, object, object> Wrapper;
            public int Count;
        }

        private static readonly Dictionary<(EventId, Delegate), Entry> _wrappers
            = new Dictionary<(EventId, Delegate), Entry>();

        /// <summary>Gets the wrapper for a handler, creating it on the first subscribe.</summary>
        internal static Action<string, object, object> Acquire<TData>(EventId eventId, Action<string, object, TData> callback)
        {
            var key = (eventId, (Delegate)callback);
            if (!_wrappers.TryGetValue(key, out Entry entry))
            {
                entry = new Entry { Wrapper = MakeWrapper(eventId, callback), Count = 0 };
                _wrappers[key] = entry;
            }
            entry.Count++;
            return entry.Wrapper;
        }

        /// <summary>
        /// Gets the wrapper to unsubscribe, or <see langword="null"/> when this handler is not subscribed.
        /// </summary>
        internal static Action<string, object, object> Release<TData>(EventId eventId, Action<string, object, TData> callback)
        {
            var key = (eventId, (Delegate)callback);
            if (!_wrappers.TryGetValue(key, out Entry entry)) return null;

            entry.Count--;
            if (entry.Count <= 0) _wrappers.Remove(key);
            return entry.Wrapper;
        }

        internal static void Clear()
        {
            _wrappers.Clear();
        }

        /// <summary>True when <typeparamref name="TData"/> can hold a null payload.</summary>
        internal static bool AcceptsNull<TData>()
        {
            return EventsRegistry.AcceptsNull(typeof(TData));
        }

        /// <summary>
        /// Builds the erased delegate the publisher holds, which narrows the payload back to
        /// <typeparamref name="TData"/> before calling the typed handler.
        /// </summary>
        /// <remarks>A payload that does not fit is reported and the handler is skipped, rather than
        /// throwing an <see cref="InvalidCastException"/> from inside it. Both end up in the console, but
        /// a cast exception names neither the event nor the type that was expected, which is most of what
        /// is needed to find the publisher at fault.</remarks>
        private static Action<string, object, object> MakeWrapper<TData>(EventId eventId, Action<string, object, TData> callback)
        {
            return (eventName, sender, data) =>
            {
                if (data is TData typed)
                {
                    callback(eventName, sender, typed);
                    return;
                }
                if (data == null && AcceptsNull<TData>())
                {
                    callback(eventName, sender, default(TData));
                    return;
                }
                ReportMismatch(eventName, typeof(TData), data, "delivering to a handler of");
            };
        }

        internal static void ReportMismatch(string eventName, Type expected, object data, string action)
        {
            string actual = data == null ? "null" : data.GetType().FullName;
            UnityEngine.Debug.LogError(
                $"EventsPublisher: {action} '{eventName}' expected a payload of {expected.FullName} but got {actual}. " +
                "The handler was skipped. Something published this event with the wrong payload — check the " +
                "untyped publishes of this name.");
        }
    }
}

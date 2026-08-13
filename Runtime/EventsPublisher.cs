using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// The EventsPublisher is a singleton that manages event publishing and subscription.
    /// It allows for nested publishers, enabling a stack-like behavior for event management.
    /// </summary>
    public class EventsPublisher : IStackEventsPublisher<string>, IEventIdPublisher
    {
        /// <summary>
        /// Gets the singleton instance of the <see cref="IStackEventsPublisher{T}"/> for publishing stack events.
        /// </summary>
        public static IStackEventsPublisher<string> Instance { get; private set; }

        /// <summary>
        /// When enabled, publishing an event name that no publisher in the stack has registered is
        /// reported as an error. Without this, a misspelled name silently reaches no subscriber while
        /// still notifying "all events" subscribers, so the logger prints it and the system looks healthy.
        /// Defaults to enabled in the editor and in development builds.
        /// </summary>
        public static bool StrictMode { get; set; } =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        static EventsPublisher()
        {
            Instance = new EventsPublisher();
            Instance.Push();
        }
        private EventsPublisher() { }

        private readonly Stack<IEventsPublisher<string>> _eventsPublishers = new Stack<IEventsPublisher<string>>();

        /// <inheritdoc/>
        public void Push()
        {
            IEventsPublisher<string> eventsPublisher = new EventsPublisherInternal();
            _eventsPublishers.Push(eventsPublisher);
        }

        /// <inheritdoc/>
        public IEventsPublisher<string> Pop()
        {
            return _eventsPublishers.Pop();
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetRegisteredEvents()
        {
            foreach (var publisher in _eventsPublishers)
            {
                foreach (string eventName in publisher.GetRegisteredEvents()) { yield return eventName; }
            }
        }

        /// <inheritdoc/>
        public void PublishEvent(string eventName, object sender, object data)
        {
            if (StrictMode) WarnIfUnregistered(eventName, sender);
            PublishResolved(EventsRegistry.Intern(eventName), eventName, sender, data);
        }

        /// <inheritdoc/>
        public void PublishEvent(EventId eventId, object sender, object data)
        {
            if (StrictMode) WarnIfUnregistered(eventId.Name, sender);
            PublishResolved(eventId, eventId.Name, sender, data);
        }

        private void PublishResolved(EventId eventId, string eventName, object sender, object data)
        {
            if (StrictMode) WarnIfPayloadMismatch(eventId, eventName, sender, data);
            // Here rather than in the frames below, so one publish counts once however deep the stack is.
            EventsDiagnostics.NotePublish(eventId);

            // Dispatch to every frame so subscribers underneath still hear it, but retain only on the
            // frame that is top at publish time. A Stack<T> enumerates top-down, so the first is Peek().
            bool isTopFrame = true;
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                if (publisher is EventsPublisherInternal internalPublisher)
                    internalPublisher.PublishEvent(eventId, sender, data, isTopFrame);
                else
                    publisher.PublishEvent(eventName, sender, data);
                isTopFrame = false;
            }
        }

        /// <inheritdoc/>
        public void RegisterEvent(EventId eventId)
        {
            if (_eventsPublishers.Peek() is IEventIdPublisher idPublisher) idPublisher.RegisterEvent(eventId);
        }

        /// <inheritdoc/>
        public void SubscribeToEvent(EventId eventId, Action<string, object, object> callback)
        {
            if (_eventsPublishers.Peek() is IEventIdPublisher idPublisher) idPublisher.SubscribeToEvent(eventId, callback);
        }

        /// <inheritdoc/>
        public void UnsubscribeToEvent(EventId eventId, Action<string, object, object> callback)
        {
            if (_eventsPublishers.Peek() is IEventIdPublisher idPublisher) idPublisher.UnsubscribeToEvent(eventId, callback);
        }

        /// <inheritdoc/>
        public bool TryGetLast(EventId eventId, out object sender, out object data)
        {
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                if (publisher is IEventIdPublisher idPublisher && idPublisher.TryGetLast(eventId, out sender, out data))
                    return true;
            }
            sender = null;
            data = null;
            return false;
        }

        /// <summary>
        /// Reports a publish of an event name that no frame in the stack has registered. Checked across
        /// the whole stack, since <see cref="RegisterEvent"/> only registers with the top frame.
        /// </summary>
        private void WarnIfUnregistered(string eventName, object sender)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                UnityEngine.Debug.LogError($"EventsPublisher: published a null or empty event name from {sender}.");
                return;
            }
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                if (publisher is EventsPublisherInternal internalPublisher && internalPublisher.IsEventRegistered(eventName))
                    return;
            }
            UnityEngine.Debug.LogError(
                $"EventsPublisher: '{eventName}' was published by {sender} but is not registered, so no subscriber will receive it. " +
                "Check for a misspelled event name. Set EventsPublisher.StrictMode = false to silence this.");
        }

        /// <summary>
        /// Reports a publish whose payload is not what the event declared it carries.
        /// </summary>
        /// <remarks>
        /// <para>A publish through <see cref="EventId{TData}"/> cannot be wrong — the compiler saw to
        /// that — so this exists for the publishes that are still untyped: a raw string, an
        /// Inspector-authored <see cref="EventRef"/>, or an enum published through the erased overload.
        /// Those are the ones that can hand a typed subscriber something it cannot use.</para>
        /// <para>Reported at the publisher, naming the sender, rather than once per subscriber. The
        /// wrapper around a typed handler reports the same mismatch, but by then the publisher is gone
        /// and the message can only say which handler was skipped, not who is at fault.</para>
        /// <para>Costs one dictionary lookup per publish, and only in <see cref="StrictMode"/>.</para>
        /// </remarks>
        private void WarnIfPayloadMismatch(EventId eventId, string eventName, object sender, object data)
        {
            if (EventsRegistry.IsPayloadAssignable(eventId, data, out Type declared)) return;

            string actual = data == null ? "null" : data.GetType().FullName;
            UnityEngine.Debug.LogError(
                $"EventsPublisher: '{eventName}' was published by {sender} with a payload of {actual}, but it is " +
                $"declared to carry {declared.FullName}. Subscribers typed to that payload will be skipped. " +
                "Set EventsPublisher.StrictMode = false to silence this.");
        }

        /// <inheritdoc/>
        public void RegisterEvent(string eventName)
        {
            _eventsPublishers.Peek().RegisterEvent(eventName);
        }

        /// <summary>
        /// Registers an event and declares its <see cref="EventDelivery"/> policy.
        /// </summary>
        /// <remarks>
        /// <para>The escape hatch for names that no enum can annotate — runtime-computed names, and
        /// Inspector-authored strings. For an enum family, prefer
        /// <see cref="EventDeliveryAttribute"/> on the member, which is read before any scene loads and
        /// keeps the policy next to the event.</para>
        /// <para>Call this from a static initializer rather than from <c>Awake</c>. The policy must be
        /// declared before the event is first published, or the first publish — often the one that
        /// matters most during boot — is not retained.</para>
        /// <para>First explicit declaration wins; a second, differing one is reported and ignored.</para>
        /// </remarks>
        public void RegisterEvent(string eventName, EventDelivery delivery)
        {
            EventsRegistry.DeclarePolicy(eventName, delivery);
            RegisterEvent(eventName);
        }

        /// <inheritdoc/>
        /// <remarks>Searches the stack from the top down and returns the first frame holding a
        /// retained value.</remarks>
        public bool TryGetLast(string eventName, out object sender, out object data)
        {
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                if (publisher.TryGetLast(eventName, out sender, out data)) return true;
            }
            sender = null;
            data = null;
            return false;
        }

        /// <inheritdoc/>
        public void SubscribeToAllEvents(Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().SubscribeToAllEvents(callback);
        }

        public void SubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().SubscribeToEvent(eventName, callback);
        }

        /// <inheritdoc/>
        public void UnsubscribeToAllEvents(Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().UnsubscribeToAllEvents(callback);
        }

        /// <inheritdoc/>
        public void UnsubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().UnsubscribeToEvent(eventName, callback);
        }

        /// <summary>
        /// Clears all events and subscriptions from all publishers in the stack.
        /// </summary>
        public void Clear()
        {
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                publisher.Clear();
            }
        }
        public IEnumerable<(string methodName, string targetName)> GetSubscribers()
        {
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                if (publisher is EventsPublisherInternal internalPublisher)
                {
                    foreach (var log in internalPublisher.GetSubscribers())
                    {
                        yield return log;
                    }
                }
            }
        }
    }
}
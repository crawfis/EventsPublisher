using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// The EventsPublisher is a singleton that manages event publishing and subscription.
    /// It allows for nested publishers, enabling a stack-like behavior for event management.
    /// </summary>
    public class EventsPublisher : IStackEventsPublisher<string>
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
            foreach (IEventsPublisher<string> publisher in _eventsPublishers) { publisher.PublishEvent(eventName, sender, data); }
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

        /// <inheritdoc/>
        public void RegisterEvent(string eventName)
        {
            _eventsPublishers.Peek().RegisterEvent(eventName);
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
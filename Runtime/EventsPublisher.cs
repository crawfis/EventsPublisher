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
        public static IStackEventsPublisher<string> Instance { get; private set; }

        static EventsPublisher()
        {
            Instance = new EventsPublisher();
            Instance.Push();
        }
        private EventsPublisher() { }

        private readonly Stack<IEventsPublisher<string>> _eventsPublishers = new Stack<IEventsPublisher<string>>();

        public void Push()
        {
            IEventsPublisher<string> eventsPublisher = new EventsPublisherInternal();
            _eventsPublishers.Push(eventsPublisher);
        }

        public IEventsPublisher<string> Pop()
        {
            return _eventsPublishers.Pop();
        }

        public IEnumerable<string> GetRegisteredEvents()
        {
            foreach (var publisher in _eventsPublishers)
            {
                foreach (string eventName in publisher.GetRegisteredEvents()) { yield return eventName; }
            }
        }

        public void PublishEvent(string eventName, object sender, object data)
        {
            foreach (IEventsPublisher<string> publisher in _eventsPublishers) { publisher.PublishEvent(eventName, sender, data); }
        }

        public void RegisterEvent(string eventName)
        {
            _eventsPublishers.Peek().RegisterEvent(eventName);
        }

        public void SubscribeToAllEvents(Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().SubscribeToAllEvents(callback);
        }

        public void SubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().SubscribeToEvent(eventName, callback);
        }

        public void UnsubscribeToAllEvents(Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().UnsubscribeToAllEvents(callback);
        }

        public void UnsubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            _eventsPublishers.Peek().UnsubscribeToEvent(eventName, callback);
        }

        public void Clear()
        {
            foreach (IEventsPublisher<string> publisher in _eventsPublishers)
            {
                publisher.Clear();
            }
        }
    }
}
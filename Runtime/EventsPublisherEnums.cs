using System;

namespace CrawfisSoftware.Events
{
    public class EventsPublisherEnums<T> where T : Enum
    {
        private IEventsPublisher<string> _eventsPublisher;
        public EventsPublisherEnums(IEventsPublisher<string> eventsPublisher)
        {
            _eventsPublisher = eventsPublisher;
        }
        public void PublishEvent(T eventEnum, object sender, object data)
        {
            _eventsPublisher.PublishEvent(eventEnum.ToString(), sender, data);
        }

        public void SubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            _eventsPublisher.SubscribeToEvent(eventEnum.ToString(), callback);
        }

        public void UnsubscribeToEvent(T eventEnum, Action<string, object, object> callback)
        {
            _eventsPublisher.UnsubscribeToEvent(eventEnum.ToString(), callback);
        }
    }
}
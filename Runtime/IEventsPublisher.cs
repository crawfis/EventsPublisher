using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    public interface IEventsPublisher<T>
    {
        void Clear();
        IEnumerable<T> GetRegisteredEvents();
        void PublishEvent(T eventName, object sender, object data);
        void RegisterEvent(T eventName);
        void SubscribeToAllEvents(Action<T, object, object> callback);
        void SubscribeToEvent(T eventName, Action<T, object, object> callback);
        void UnsubscribeToAllEvents(Action<T, object, object> callback);
        void UnsubscribeToEvent(T eventName, Action<T, object, object> callback);
    }
}
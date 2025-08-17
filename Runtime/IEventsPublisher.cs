using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    public interface IEventsPublisher
    {
        void Clear();
        IEnumerable<string> GetRegisteredEvents();
        void PublishEvent(string eventName, object sender, object data);
        void Push();
        IEventsPublisher Pop();
        void RegisterEvent(string eventName);
        void SubscribeToAllEvents(Action<string, object, object> callback);
        void SubscribeToEvent(string eventName, Action<string, object, object> callback);
        void UnsubscribeToAllEvents(Action<string, object, object> callback);
        void UnsubscribeToEvent(string eventName, Action<string, object, object> callback);
    }
}
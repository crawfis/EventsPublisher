using CrawfisSoftware.Events;

using System;
using System.Collections.Generic;

namespace CrawfisSoftware.Events
{
    internal class EventsPublisherInternal : IEventsPublisher<string>
    {
        // Define the events that occur in the game
        private readonly Dictionary<string, Action<string, object, object>> events = new Dictionary<string, Action<string, object, object>>();
        private readonly List<Action<string, object, object>> allSubscribers = new List<Action<string, object, object>>();
        private Queue<(string eventName, Delegate callback, object sender, object data)> _callbackQueue = new Queue<(string eventName, Delegate callback, object sender, object data)>();

        public void RegisterEvent(string eventName)
        {
            if (!events.ContainsKey(eventName))
            {
                events.Add(eventName, NullCallback);
            }
        }
        public void SubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            RegisterEvent(eventName);
            if (events.ContainsKey(eventName) && callback != null)
                events[eventName] += callback;
        }

        public void UnsubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            if (events.ContainsKey(eventName) && callback != null)
                events[eventName] -= callback;
        }

        public void SubscribeToAllEvents(Action<string, object, object> callback)
        {
            allSubscribers.Add(callback);
        }

        public void UnsubscribeToAllEvents(Action<string, object, object> callback)
        {
            allSubscribers.Remove(callback);
        }

        public void PublishEvent(string eventName, object sender, object data)
        {
            if (events.TryGetValue(eventName, out Action<string, object, object> eventDelegate))
            {
                //eventDelegate(sender, data);
                var callbacks = eventDelegate.GetInvocationList();

                // Queue up each callback. This ensures that if a callback publishes an event, that the
                // other callbacks for *this* event are called before the newly published event's callbacks.
                foreach (var callback in callbacks)
                    _callbackQueue.Enqueue((eventName, callback, sender, data));
            }
            foreach (var handler in allSubscribers)
                _callbackQueue.Enqueue((eventName, handler, sender, data));
            //handler(eventName, sender, data);

            while (_callbackQueue.Count > 0)
            {
                var message = _callbackQueue.Dequeue();
                eventName = message.eventName;
                var callback = message.callback;
                try
                {
                    callback.DynamicInvoke(eventName, message.sender, message.data);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Exception publishing {message.eventName}: {callback.Target} {e.InnerException.Message} {e.InnerException.StackTrace} {e.InnerException.Source}");
                }
            }
        }

        public IEnumerable<string> GetRegisteredEvents()
        {
            return events.Keys;
        }

        private void NullCallback(string eventName, object sender, object data)
        {
        }

        public void Clear()
        {
            events.Clear();
            allSubscribers.Clear();
        }
    }
}
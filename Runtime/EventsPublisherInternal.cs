using System;
using System.Collections.Generic;
using System.Linq;

namespace CrawfisSoftware.Events
{
    internal class EventsPublisherInternal : IEventsPublisher<string>
    {
        // Define the events that occur in the game
        private readonly Dictionary<string, Action<string, object, object>> events = new Dictionary<string, Action<string, object, object>>();
        private readonly List<Action<string, object, object>> allSubscribers = new List<Action<string, object, object>>();
        private Queue<(string eventName, Action<string, object, object> callback, object sender, object data)> _callbackQueue
            = new Queue<(string eventName, Action<string, object, object> callback, object sender, object data)>();

        // Guards against a callback that publishes an event starting a second, nested drain of the
        // shared queue. Nested publishes enqueue and return; the outermost drain processes them.
        private bool _isDraining;

        public void RegisterEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            if (!events.ContainsKey(eventName))
            {
                events.Add(eventName, NullCallback);
            }
        }
        public void SubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null) return;
            RegisterEvent(eventName);
            events[eventName] += callback;
        }

        public void UnsubscribeToEvent(string eventName, Action<string, object, object> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null) return;
            if (events.ContainsKey(eventName))
                events[eventName] -= callback;
        }

        /// <summary>
        /// Returns true if <paramref name="eventName"/> has been registered with this publisher.
        /// </summary>
        internal bool IsEventRegistered(string eventName)
        {
            return !string.IsNullOrEmpty(eventName) && events.ContainsKey(eventName);
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
            if (string.IsNullOrEmpty(eventName)) return;

            if (events.TryGetValue(eventName, out Action<string, object, object> eventDelegate))
            {
                var callbacks = eventDelegate.GetInvocationList();

                // Queue up each callback. This ensures that if a callback publishes an event, that the
                // other callbacks for *this* event are called before the newly published event's callbacks.
                foreach (var callback in callbacks)
                    _callbackQueue.Enqueue((eventName, (Action<string, object, object>)callback, sender, data));
            }
            foreach (var handler in allSubscribers)
                _callbackQueue.Enqueue((eventName, handler, sender, data));

            // A nested publish (a callback publishing an event) only enqueues. The outermost call owns
            // the drain, so the remaining callbacks for the *current* event run first, as intended.
            if (_isDraining) return;

            _isDraining = true;
            try
            {
                while (_callbackQueue.Count > 0)
                {
                    var message = _callbackQueue.Dequeue();
                    Action<string, object, object> callback = message.callback;
                    try
                    {
                        callback(message.eventName, message.sender, message.data);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(
                            $"Exception publishing {message.eventName} to {callback.Target}: {e}");
                    }
                }
            }
            finally
            {
                // Never leave stale callbacks queued; they would otherwise flush during an unrelated publish.
                _callbackQueue.Clear();
                _isDraining = false;
            }
        }

        public IEnumerable<string> GetRegisteredEvents()
        {
            return events.Keys;
        }

        public IEnumerable<(string eventName, string typeName)> GetSubscribers()
        {
            foreach (var eventName in events.Keys)
            {
                if (events.TryGetValue(eventName, out var eventDelegate) && eventDelegate != null)
                {
                    foreach (var handler in eventDelegate.GetInvocationList().Skip(1))
                    {
                        yield return (eventName, handler.Method.DeclaringType.ToString());
                    }
                }
            }
            foreach (var handler in allSubscribers)
                yield return ("all events", handler.Method.DeclaringType.ToString());
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
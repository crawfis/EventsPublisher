using System;

namespace CrawfisSoftware.Events
{
    /// <summary>
    /// Renders senders and handlers for the publisher's reports without trusting their
    /// <c>ToString</c>.
    /// </summary>
    /// <remarks>
    /// <para>Two things go wrong when a report calls <c>ToString</c> on whatever it was handed. A
    /// destroyed Unity object answers with <c>"null"</c>, so the most common exception report — a
    /// handler left subscribed on an object that no longer exists — read "Exception publishing X to
    /// null" and named nothing. And the StrictMode reports format the sender outside any guard, so a
    /// <c>ToString</c> that throws — an object caught mid-teardown, say — escaped
    /// <c>PublishEvent</c> into the caller's code.</para>
    /// <para>A handler is therefore named by its method, which is what the reader has to go and
    /// find, and a sender is rendered inside a guard with the type name as the fallback.</para>
    /// </remarks>
    internal static class Describe
    {
        /// <summary>
        /// Names a handler as <c>Namespace.Type.Method</c>, marking a target that is a destroyed
        /// Unity object.
        /// </summary>
        internal static string Handler(Delegate handler)
        {
            if (handler == null) return "null";

            Type declaringType = handler.Method.DeclaringType;
            string name = declaringType == null
                ? handler.Method.Name
                : declaringType.FullName + "." + handler.Method.Name;
            return IsDestroyed(handler.Target) ? name + " (destroyed)" : name;
        }

        /// <summary>
        /// Renders a sender, surviving a null, a destroyed Unity object, and a throwing
        /// <c>ToString</c>.
        /// </summary>
        internal static string Sender(object sender)
        {
            if (sender == null) return "null";
            if (IsDestroyed(sender)) return sender.GetType().FullName + " (destroyed)";
            try
            {
                string text = sender.ToString();
                return string.IsNullOrEmpty(text) ? sender.GetType().FullName : text;
            }
            catch (Exception e)
            {
                return sender.GetType().FullName + " (ToString threw " + e.GetType().Name + ")";
            }
        }

        // Unity's overloaded == is what tells a destroyed object from a live one. The C# reference is
        // never null for a destroyed object, which is exactly why its ToString misleads.
        private static bool IsDestroyed(object candidate)
        {
            return candidate is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}

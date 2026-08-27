using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// Lists the event-enum families the registry holds, one console line each.
    /// </summary>
    /// <remarks>
    /// <para>Nothing sweeps in edit mode — the
    /// <see cref="UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad"/> sweep runs before the first
    /// scene loads — so a listing taken straight from the registry would report that a project with a
    /// dozen event families has none. This runs that same sweep first rather than walking the
    /// assemblies its own way: the registration it performs in the editor is the registration play
    /// mode performs, it subscribes to nothing and publishes nothing, and entering play mode resets
    /// the static state and sweeps again — so nothing it registers here can outlive the editor session
    /// or reach a build.</para>
    /// <para>The sweep is idempotent, so the menu behaves the same in play mode, where it reports the
    /// lazily-registered families too: an enum with no <see cref="EventEnumAttribute"/> registers the
    /// first time its <see cref="EventsFor{T}"/> is touched, and is a domain from that moment on.</para>
    /// <para>Console rather than a window, matching <c>List Current Subscribers</c>. One line per
    /// domain is readable in the console list without opening anything, and the lines survive being
    /// copied out.</para>
    /// </remarks>
    internal static class EventDomainsMenu
    {
        private const string MENU_LOCATION = "CrawfisSoftware/Events/List Domains";

        [MenuItem(MENU_LOCATION)]
        private static void ListDomains()
        {
            EventsRegistry.RegisterAnnotatedEventEnums();

            List<EventDomain> domains = EventDomainCatalog.Describe(EventsRegistry.RegisteredEnumTypes);
            if (domains.Count == 0)
            {
                Debug.Log("No event domains are registered. Mark an event enum with [EventEnum] to " +
                          "register it before any scene loads.");
                return;
            }

            Debug.Log($"Listing {domains.Count} registered event domain(s) ...");
            foreach (EventDomain domain in domains)
            {
                Debug.Log(EventDomainCatalog.Format(domain));
            }
        }
    }
}

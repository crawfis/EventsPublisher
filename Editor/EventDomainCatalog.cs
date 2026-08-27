using System;
using System.Collections.Generic;
using System.Reflection;

namespace CrawfisSoftware.Events.Editor
{
    /// <summary>
    /// One registered event-enum family, summarised for display.
    /// </summary>
    /// <remarks>The counts are of declared members, which is what the enum's source shows. Members
    /// sharing a value are one event rather than two — the projection keys on the value, so an alias
    /// never becomes a second event name — so a family that uses aliases publishes fewer events than it
    /// declares members, and the attribute counts include an alias's <see cref="EventDeliveryAttribute"/>
    /// or <see cref="EventPayloadAttribute"/>, which the registry never reads: it resolves the value
    /// back to one member, the first one declared with it.</remarks>
    internal readonly struct EventDomain
    {
        /// <summary>The prefix every event in the family is named under, without its trailing slash.</summary>
        internal readonly string Prefix;

        /// <summary>The enum type itself.</summary>
        internal readonly Type EnumType;

        /// <summary>How many members the enum declares.</summary>
        internal readonly int MemberCount;

        /// <summary>How many of them declare an <see cref="EventPayloadAttribute"/>.</summary>
        internal readonly int PayloadCount;

        /// <summary>How many of them are declared <see cref="EventDelivery.Sticky"/>.</summary>
        internal readonly int StickyCount;

        /// <summary>How many of them are declared <see cref="EventDelivery.Replay"/>.</summary>
        internal readonly int ReplayCount;

        internal EventDomain(string prefix, Type enumType, int memberCount, int payloadCount,
                             int stickyCount, int replayCount)
        {
            Prefix = prefix;
            EnumType = enumType;
            MemberCount = memberCount;
            PayloadCount = payloadCount;
            StickyCount = stickyCount;
            ReplayCount = replayCount;
        }
    }

    /// <summary>
    /// Summarises the event-enum families the registry holds, for the <c>List Domains</c> menu.
    /// </summary>
    /// <remarks>
    /// <para>The families are handed in from <see cref="EventsRegistry.RegisteredEnumTypes"/> rather
    /// than discovered here, so what is listed is what the publisher is keyed on. A reflection walk of
    /// this tool's own would be a second definition of "an event family" and could disagree with the
    /// runtime one — the same drift <see cref="EventNameCatalog"/> exists to prevent on the projection,
    /// and a listing that quietly disagrees with the registry is worse than no listing.</para>
    /// <para>Free of editor APIs, so it can be tested directly.</para>
    /// </remarks>
    internal static class EventDomainCatalog
    {
        /// <summary>
        /// Summarises each enum family, ordered for display.
        /// </summary>
        /// <remarks>Ordered by prefix, then by full type name so that two families colliding on one
        /// prefix still list deterministically — and adjacently, which is what makes the collision
        /// legible here as well as in the error the registry logs for it.</remarks>
        internal static List<EventDomain> Describe(IEnumerable<Type> enumTypes)
        {
            var domains = new List<EventDomain>();
            if (enumTypes == null) return domains;

            foreach (Type enumType in enumTypes)
            {
                if (enumType == null || !enumType.IsEnum) continue;

                int members = 0, payloads = 0, sticky = 0, replay = 0;
                foreach (FieldInfo member in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    members++;
                    if (member.IsDefined(typeof(EventPayloadAttribute), false)) payloads++;

                    var delivery = (EventDeliveryAttribute)Attribute.GetCustomAttribute(
                        member, typeof(EventDeliveryAttribute));
                    if (delivery == null) continue;
                    if (delivery.Delivery == EventDelivery.Sticky) sticky++;
                    else if (delivery.Delivery == EventDelivery.Replay) replay++;
                }

                domains.Add(new EventDomain(
                    EventsRegistry.GetPrefix(enumType), enumType, members, payloads, sticky, replay));
            }

            domains.Sort(CompareForDisplay);
            return domains;
        }

        private static int CompareForDisplay(EventDomain a, EventDomain b)
        {
            int byPrefix = string.CompareOrdinal(a.Prefix, b.Prefix);
            return byPrefix != 0 ? byPrefix : string.CompareOrdinal(a.EnumType.FullName, b.EnumType.FullName);
        }

        /// <summary>Renders one domain as a single line.</summary>
        /// <remarks>The prefix is shown with its slash, because that is the shape of the names the
        /// family publishes; the type is named in full, because the prefix is projected from the simple
        /// name and two families in different namespaces can share it.</remarks>
        internal static string Format(EventDomain domain)
        {
            return $"{domain.Prefix}/  —  {domain.EnumType.FullName}  —  {domain.MemberCount} member(s), " +
                   $"{domain.PayloadCount} with [EventPayload], {domain.StickyCount} Sticky, " +
                   $"{domain.ReplayCount} Replay";
        }
    }
}

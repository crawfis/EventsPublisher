using System.Runtime.CompilerServices;

// The catalog and the popup's option builder are internal but carry real logic — the projection that
// must match the runtime one, and the rule that an unrecognised value is surfaced rather than cleared.
[assembly: InternalsVisibleTo("CrawfisSoftware.EventsPublisher.Tests")]

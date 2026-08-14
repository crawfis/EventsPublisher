using System.Runtime.CompilerServices;

// The test assembly exercises internals deliberately: EventsRegistry's reset, so each test starts
// from clean static state, and EventsPublisherInternal's registration probe. Neither belongs on the
// public API — a public "reset everything" would be a footgun in game code.
[assembly: InternalsVisibleTo("CrawfisSoftware.EventsPublisher.Tests")]

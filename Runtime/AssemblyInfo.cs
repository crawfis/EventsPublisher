using System.Runtime.CompilerServices;

// The test assembly exercises internals deliberately: EventsRegistry's reset, so each test starts
// from clean static state, and EventsPublisherInternal's registration probe. Neither belongs on the
// public API — a public "reset everything" would be a footgun in game code.
[assembly: InternalsVisibleTo("CrawfisSoftware.EventsPublisher.Tests")]

// The editor's "List Domains" menu re-runs the before-scene-load sweep on demand, because outside play
// mode it has not run and the registry it reports on would be empty. Calling the same sweep the runtime
// calls is what keeps the menu describing the registry rather than a second reflection walk that could
// disagree with it. The sweep stays off the public API for the same reason the reset does.
[assembly: InternalsVisibleTo("CrawfisSoftware.EventsPublisher.Editor")]

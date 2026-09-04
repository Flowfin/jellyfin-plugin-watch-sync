using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchSync.Matching;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What a library event does to the index, which is the half of #29's second condition that was
/// unproven of a running server.
///
/// The three changes the index carries were covered by facts that call them directly, and the
/// gap those facts leave is the whole subject here: an index kept current by a call nobody makes
/// is an index rebuilt once a sweep and stale in between, and every lookup in that window
/// answers unmatched for an item the library holds.
///
/// So each fact below drives the server's own event rather than the index's member. The library
/// is a fake of the manager the server hands over, the event is raised on it the way the server
/// raises it, and what is asserted is what a lookup answers afterwards.
///
/// What they cannot judge is that the server actually starts a hosted service, which is the
/// server's own lifetime and is not in this tree. What holds that end is the registration, and
/// <c>ServiceRegistratorTests</c> is where it is judged.
/// </summary>
public class LibraryEventsTests
{
    /// <summary>
    /// The failure the whole type exists against. An item the library gains between two sweeps
    /// is in the index at the next lookup rather than at the next rebuild.
    /// </summary>
    [Fact]
    public async Task AnItemTheLibraryGainsIsInTheIndexBeforeTheNextRebuild()
    {
        var library = new Library();
        var index = Built(library);
        var events = await Started(library, index);

        var film = library.Film("tt0000002");

        library.RaiseAdded(film);

        var found = index.Lookup(KeyOfFilm("tt0000002"));

        Assert.Equal(MatchAnswer.Matched, found.Answer);
        Assert.Equal(film, found.Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The ordinary reason an item is updated is that somebody repaired the metadata this plugin
    /// keys on, so the key is re-read rather than assumed unchanged. The item leaves the key it
    /// used to produce, which is the half a handler that only adds gets wrong: the old key would
    /// go on resolving to an item that no longer carries it.
    /// </summary>
    [Fact]
    public async Task AnItemWhoseKeyWasRepairedLeavesTheKeyItUsedToCarry()
    {
        var library = new Library();
        var film = library.Film("tt0000001");
        var index = Built(library);
        var events = await Started(library, index);

        library.Rekey(film, "tt0000009");
        library.RaiseUpdated(film);

        Assert.Equal(
            MatchAnswer.NoMatch,
            index.Lookup(KeyOfFilm("tt0000001")).Answer);
        Assert.Equal(
            film,
            index.Lookup(KeyOfFilm("tt0000009")).Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// An item the library loses leaves the index, and only that item. The rest of the library
    /// is still answered, which is what a library rescan depends on.
    /// </summary>
    [Fact]
    public async Task AnItemTheLibraryLosesLeavesTheIndexAndNothingElseDoes()
    {
        var library = new Library();
        var going = library.Film("tt0000001");
        var staying = library.Film("tt0000002");
        var index = Built(library);
        var events = await Started(library, index);

        library.RaiseRemoved(going);

        Assert.Equal(
            MatchAnswer.NoMatch,
            index.Lookup(KeyOfFilm("tt0000001")).Answer);
        Assert.Equal(
            staying,
            index.Lookup(KeyOfFilm("tt0000002")).Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// An item whose metadata was not repaired but removed produces no key at all, and it leaves
    /// the index rather than staying under the key it used to have. An entry nothing can produce
    /// again is exactly the entry that resolves a peer's change onto the wrong file.
    /// </summary>
    [Fact]
    public async Task AnItemThatStoppedProducingAKeyLeavesTheIndex()
    {
        var library = new Library();
        var film = library.Film("tt0000001");
        var index = Built(library);
        var events = await Started(library, index);

        library.Unkey(film);
        library.RaiseUpdated(film);

        Assert.Equal(
            MatchAnswer.NoMatch,
            index.Lookup(KeyOfFilm("tt0000001")).Answer);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The condition's own words: no handler survives a stop. It is asserted through what the
    /// index answers rather than by counting handlers, because a handler that is still attached
    /// and does nothing is not the failure - the failure is a disabled plugin still changing the
    /// index, and this is the shape a reader can check.
    /// </summary>
    [Fact]
    public async Task NoHandlerSurvivesAStop()
    {
        var library = new Library();
        var index = Built(library);
        var events = await Started(library, index);

        await events.StopAsync(CancellationToken.None);

        var film = library.Film("tt0000002");

        library.RaiseAdded(film);

        Assert.Equal(
            MatchAnswer.NoMatch,
            index.Lookup(KeyOfFilm("tt0000002")).Answer);
    }

    /// <summary>
    /// A stop the server makes before any start detaches nothing and leaves the service usable.
    ///
    /// The server decides when a hosted service starts and stops, so a stop arriving first is
    /// its call rather than a mistake here. Detaching handlers that were never attached is a
    /// silent no-op on a multicast delegate, which is why the guard is asserted through a start
    /// that still works afterwards rather than through the stop returning.
    /// </summary>
    [Fact]
    public async Task AStopBeforeAnyStartLeavesTheServiceUsable()
    {
        var library = new Library();
        var index = Built(library);
        var events = new LibraryEvents(library.Manager, index);

        await events.StopAsync(CancellationToken.None);
        await events.StartAsync(CancellationToken.None);

        var film = library.Film("tt0000002");

        library.RaiseAdded(film);

        Assert.Equal(film, index.Lookup(KeyOfFilm("tt0000002")).Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// An event that carries no item changes nothing.
    ///
    /// The item on the server's own event argument is a settable property, so it is read rather
    /// than trusted. What the guard is worth is measured against what its absence costs: a
    /// handler that dereferenced it would throw inside the server's own item pipeline, on the
    /// server's thread, for a plugin that only wanted to keep a cache current.
    /// </summary>
    [Fact]
    public async Task AnEventCarryingNoItemChangesNothing()
    {
        var library = new Library();
        var film = library.Film("tt0000001");
        var index = Built(library);
        var events = await Started(library, index);

        library.RaiseEmptyEvents();

        Assert.Equal(film, index.Lookup(KeyOfFilm("tt0000001")).Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// An episode the library holds under no series produces no key, so it is not indexed.
    ///
    /// The series is where an episode's identifier comes from, and an episode with none is the
    /// ordinary state of a file the server has not matched yet rather than a damaged library.
    ///
    /// The library is not asked for the empty identifier either, and that is the half a lookup
    /// answering nothing hides: a manager asked for an item nobody named answers null here and
    /// on a real server does a database read per event for a question with no answer.
    /// </summary>
    [Fact]
    public async Task AnEpisodeUnderNoSeriesIsNotIndexed()
    {
        var library = new Library();
        var index = Built(library);
        var events = await Started(library, index);

        var orphan = library.EpisodeUnderNoSeries(2, 5);

        library.RaiseAdded(orphan);

        Assert.Equal(MatchAnswer.NoMatch, index.Lookup(KeyOfEpisode("tt0000100", 2, 5)).Answer);
        library.AssertNothingWasAskedForTheEmptyIdentifier();

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A start that ran twice is undone by one stop.
    ///
    /// Two subscriptions and one unsubscription leave one handler attached, which is a plugin
    /// that goes on changing the index after it was stopped and is invisible from every other
    /// fact here: while it is running, a handler attached twice answers exactly what a handler
    /// attached once answers.
    /// </summary>
    [Fact]
    public async Task ASecondStartIsStillUndoneByOneStop()
    {
        var library = new Library();
        var index = Built(library);
        var events = await Started(library, index);

        await events.StartAsync(CancellationToken.None);
        await events.StopAsync(CancellationToken.None);

        var film = library.Film("tt0000002");

        library.RaiseAdded(film);

        Assert.Equal(
            MatchAnswer.NoMatch,
            index.Lookup(KeyOfFilm("tt0000002")).Answer);
    }

    /// <summary>
    /// An aggregate is not a transfer subject, so an event about one puts nothing in the index.
    ///
    /// The near-miss is one line away and it is the update rather than the removal. A handler
    /// that took every kind the library raises would key a series the way it keys a film, off
    /// the series' own identifier, and the index would then answer that identifier with the
    /// series. Applying a change to a series means marking every episode the peer holds under
    /// it, which is the mass marking this plugin exists to refuse; the removal is the harmless
    /// half of the same mistake, because a series was never put in for one to take out.
    /// </summary>
    [Fact]
    public async Task AnEventAboutAnAggregatePutsNothingInTheIndex()
    {
        var library = new Library();
        var episode = library.Episode("tt0000100", 2, 5);
        var index = Built(library);
        var events = await Started(library, index);

        library.RaiseUpdatedSeriesOf(episode);

        Assert.Equal(MatchAnswer.NoMatch, index.Lookup(KeyOfFilm("tt0000100")).Answer);

        library.RaiseRemovedSeriesOf(episode);

        Assert.Equal(episode, index.Lookup(KeyOfEpisode("tt0000100", 2, 5)).Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A virtual item is one the library knows about and holds no file for, so nobody watched it
    /// here and it is not in the index. The walk excludes it in its query; an event carries the
    /// item itself and no query, so the exclusion has to be made again on this side or the two
    /// routes disagree about the same library.
    /// </summary>
    [Fact]
    public async Task AVirtualItemTheLibraryGainsIsNotIndexed()
    {
        var library = new Library();
        var index = Built(library);
        var events = await Started(library, index);

        var ghost = library.VirtualFilm("tt0000003");

        library.RaiseAdded(ghost);

        Assert.Equal(
            MatchAnswer.NoMatch,
            index.Lookup(KeyOfFilm("tt0000003")).Answer);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// An episode is keyed through the series the manager holds rather than through the episode's
    /// own <c>Series</c> property, which resolves off a static the server fills in at start. The
    /// derivation is the walk's, and this is the fact that says the event path reaches it.
    /// </summary>
    [Fact]
    public async Task AnEpisodeTheLibraryGainsIsKeyedThroughTheSeriesTheManagerHolds()
    {
        var library = new Library();
        var index = Built(library);
        var events = await Started(library, index);

        var episode = library.Episode("tt0000200", 1, 3);

        library.RaiseAdded(episode);

        Assert.Equal(episode, index.Lookup(KeyOfEpisode("tt0000200", 1, 3)).Item);

        await events.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The key a film with that identifier produces, derived the same way the plugin derives it.
    /// </summary>
    /// <param name="imdb">The identifier.</param>
    /// <returns>The key.</returns>
    private static MatchKey KeyOfFilm(string imdb)
    {
        var reading = ProviderIdentifier.Normalise(IdentifierProvider.Imdb, imdb);

        Assert.True(reading.IsUsable);

        return MatchKey.Of(reading.Identifier!);
    }

    /// <summary>
    /// The key an episode of a series produces, derived the same way the plugin derives it so
    /// that this file carries no second spelling of the rule.
    /// </summary>
    /// <param name="seriesImdb">The series identifier.</param>
    /// <param name="season">The season number.</param>
    /// <param name="number">The episode number.</param>
    /// <returns>The key.</returns>
    private static MatchKey KeyOfEpisode(string seriesImdb, int season, int number)
    {
        var series = new Dictionary<string, string> { ["Imdb"] = seriesImdb };

        var derived = EpisodeMatchKey.Derive(
            new Dictionary<string, string>(),
            series,
            null,
            season,
            number,
            null);

        return MatchKey.Of(derived.Key!);
    }

    /// <summary>
    /// An index over the library, built before any event is raised.
    ///
    /// The order is the one a server runs in: the sweep builds the index at start, and the
    /// events keep it current from there. An event raised before the first build is not lost
    /// either, because the build reads the library as it stands, but a fact about the events
    /// would then be asserting what the walk found.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <returns>The built index.</returns>
    private static MatchIndex Built(Library library)
    {
        var index = new MatchIndex(new ServerLibrary(library.Manager));

        index.Rebuild();

        return index;
    }

    /// <summary>
    /// The service, started.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="index">The index.</param>
    /// <returns>The started service.</returns>
    private static async Task<LibraryEvents> Started(Library library, MatchIndex index)
    {
        var events = new LibraryEvents(library.Manager, index);

        await events.StartAsync(CancellationToken.None);

        return events;
    }

    /// <summary>
    /// A library the server would hand over, holding items and raising the three events about
    /// them. It is the same shape the walk is judged against, with the events added.
    /// </summary>
    private sealed class Library
    {
        private readonly List<Guid> _ids = new List<Guid>();

        private readonly Dictionary<Guid, BaseItem> _items = new Dictionary<Guid, BaseItem>();

        private readonly Mock<ILibraryManager> _manager;

        internal Library()
        {
            _manager = new Mock<ILibraryManager>();

            _manager
                .Setup(library => library.GetItemIds(It.IsAny<InternalItemsQuery>()))
                .Returns(() => _ids.ToList());

            _manager
                .Setup(library => library.GetItemById(It.IsAny<Guid>()))
                .Returns((Guid id) => _items.TryGetValue(id, out var item) ? item : null);
        }

        internal ILibraryManager Manager => _manager.Object;

        internal Guid Film(string imdb)
        {
            var film = new Movie { Id = Guid.NewGuid() };
            film.ProviderIds["Imdb"] = imdb;

            return Add(film);
        }

        internal Guid VirtualFilm(string imdb)
        {
            var film = new Movie { Id = Guid.NewGuid(), IsVirtualItem = true };
            film.ProviderIds["Imdb"] = imdb;

            return Add(film);
        }

        internal Guid Episode(string seriesImdb, int season, int number)
        {
            var series = new Series { Id = Guid.NewGuid() };
            series.ProviderIds["Imdb"] = seriesImdb;
            _items[series.Id] = series;

            var episode = new Episode
            {
                Id = Guid.NewGuid(),
                SeriesId = series.Id,
                ParentIndexNumber = season,
                IndexNumber = number,
            };

            return Add(episode);
        }

        internal Guid EpisodeUnderNoSeries(int season, int number) =>
            Add(new Episode
            {
                Id = Guid.NewGuid(),
                ParentIndexNumber = season,
                IndexNumber = number,
            });

        internal void AssertNothingWasAskedForTheEmptyIdentifier() =>
            _manager.Verify(library => library.GetItemById(Guid.Empty), Times.Never());

        internal void Rekey(Guid id, string imdb) => _items[id].ProviderIds["Imdb"] = imdb;

        internal void Unkey(Guid id) => _items[id].ProviderIds.Clear();

        internal void RaiseAdded(Guid id) =>
            _manager.Raise(library => library.ItemAdded += null, Sender, Change(_items[id]));

        internal void RaiseUpdated(Guid id) =>
            _manager.Raise(library => library.ItemUpdated += null, Sender, Change(_items[id]));

        internal void RaiseRemoved(Guid id) =>
            _manager.Raise(library => library.ItemRemoved += null, Sender, Change(_items[id]));

        internal void RaiseEmptyEvents()
        {
            _manager.Raise(library => library.ItemAdded += null, Sender, new ItemChangeEventArgs());
            _manager.Raise(library => library.ItemUpdated += null, Sender, new ItemChangeEventArgs());
            _manager.Raise(library => library.ItemRemoved += null, Sender, new ItemChangeEventArgs());
        }

        internal void RaiseUpdatedSeriesOf(Guid episodeId) =>
            _manager.Raise(
                library => library.ItemUpdated += null,
                Sender,
                Change(_items[((Episode)_items[episodeId]).SeriesId]));

        internal void RaiseRemovedSeriesOf(Guid episodeId) =>
            _manager.Raise(
                library => library.ItemRemoved += null,
                Sender,
                Change(_items[((Episode)_items[episodeId]).SeriesId]));

        private object Sender => _manager.Object;

        private static ItemChangeEventArgs Change(BaseItem item) =>
            new ItemChangeEventArgs { Item = item };

        private Guid Add(BaseItem item)
        {
            _ids.Add(item.Id);
            _items[item.Id] = item;

            return item.Id;
        }
    }
}

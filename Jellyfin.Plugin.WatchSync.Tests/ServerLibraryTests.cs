using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Matching;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the index reads when it reads a real library, which is the half of #29's first condition
/// nothing in this repository had.
///
/// Every other implementation of <see cref="IMatchIndexSource"/> here is a fake written to serve
/// the index a page, so each of them returns exactly what it was told to return. The facts below
/// are about the one that does not: it is handed a library holding items nobody keyed, items that
/// went away between two pages, and episodes that key through their series, and the page it
/// answers has to be right in each of those.
///
/// The one most worth having is <see cref="AnItemWithNoKeyDoesNotEndTheWalk"/>. The interface ends
/// a walk on a page shorter than the one asked for, and unkeyed items are not returned, so the
/// obvious implementation - read a page of the library, drop what has no key, hand back the rest -
/// stops at the first library page holding one and leaves the rest of the library out of the
/// index. Nothing about that failure is visible from the index: it reports a build that finished,
/// and every item past the first unkeyed one is unmatched for ever.
/// </summary>
public class ServerLibraryTests
{
    /// <summary>
    /// The failure the whole shape of this type exists against. A single item with no key sits in
    /// the middle of the library, the index asks for pages of two, and it has to reach the last
    /// item rather than stopping at the page the unkeyed one fell in.
    /// </summary>
    [Fact]
    public void AnItemWithNoKeyDoesNotEndTheWalk()
    {
        var library = new Library();
        var first = library.Film("tt0000001");
        library.Unkeyed();
        var last = library.Film("tt0000002");

        var source = new ServerLibrary(library.Manager);

        var page = source.ReadPage(0, 2);

        Assert.Equal(new[] { first, last }, page.Select(item => item.ItemId));
    }

    /// <summary>
    /// The walk as the index makes it, over a library where the unkeyed items are at the front, in
    /// the middle and at the end. Every keyed item comes out once, and the walk ends on the short
    /// page rather than on the first one that was hard to fill.
    /// </summary>
    [Fact]
    public void EveryKeyedItemComesOutOnceAcrossTheWholeWalk()
    {
        var library = new Library();
        library.Unkeyed();
        var expected = new List<Guid> { library.Film("tt0000001") };
        library.Unkeyed();
        expected.Add(library.Film("tt0000002"));
        expected.Add(library.Film("tt0000003"));
        library.Unkeyed();

        var source = new ServerLibrary(library.Manager);

        Assert.Equal(expected, Walk(source, 2));
    }

    /// <summary>
    /// The resume point this type keeps is an optimisation and never the answer. A page asked at
    /// an offset the last one did not end on has no resume point to use, and it has to answer the
    /// same items the sequential walk answered for that offset rather than an empty page or the
    /// wrong one.
    /// </summary>
    [Fact]
    public void APageAskedOutOfOrderAnswersWhatTheWalkWouldHave()
    {
        var library = new Library();
        library.Film("tt0000001");
        library.Unkeyed();
        var third = library.Film("tt0000002");
        var fourth = library.Film("tt0000003");

        var sequential = Walk(new ServerLibrary(library.Manager), 4);

        var jumped = new ServerLibrary(library.Manager);
        jumped.ReadPage(0, 3);
        var page = jumped.ReadPage(1, 2);

        Assert.Equal(new[] { third, fourth }, sequential.Skip(1));
        Assert.Equal(new[] { third, fourth }, page.Select(item => item.ItemId));
    }

    /// <summary>
    /// One walk is over one list of identifiers. The alternative is an offset into a fresh query
    /// per page, and the server offers no total order to make an offset mean the same thing twice,
    /// so two pages of one walk could hold one item twice and miss another.
    /// </summary>
    [Fact]
    public void AWalkTakesTheIdentifiersOnceRatherThanOncePerPage()
    {
        var library = new Library();
        library.Film("tt0000001");
        library.Film("tt0000002");
        library.Film("tt0000003");

        Walk(new ServerLibrary(library.Manager), 1);

        Assert.Equal(1, library.SnapshotsTaken);
    }

    /// <summary>
    /// The other half of the same rule. A rebuild is a new walk and has to see what the library
    /// holds now, so the list is taken again where a walk starts at the beginning.
    /// </summary>
    [Fact]
    public void ARebuildTakesTheIdentifiersAgainAndSeesWhatArrivedSince()
    {
        var library = new Library();
        library.Film("tt0000001");

        var source = new ServerLibrary(library.Manager);

        Assert.Single(Walk(source, 10));

        var arrived = library.Film("tt0000002");

        Assert.Contains(arrived, Walk(source, 10));
        Assert.Equal(2, library.SnapshotsTaken);
    }

    /// <summary>
    /// An item that went away between the list being taken and the page being read. The library
    /// answers nothing for it, and the walk carries on rather than throwing or stopping: a deletion
    /// during a rebuild is ordinary, and a rebuild that gave up on one would leave the index
    /// holding whatever the previous one built with nothing saying so.
    /// </summary>
    [Fact]
    public void AnItemTheLibraryNoLongerHoldsIsSkippedRatherThanEndingTheWalk()
    {
        var library = new Library();
        var first = library.Film("tt0000001");
        var vanishing = library.Film("tt0000002");
        var last = library.Film("tt0000003");

        var source = new ServerLibrary(library.Manager);

        library.Forget(vanishing);

        Assert.Equal(new[] { first, last }, Walk(source, 2));
    }

    /// <summary>
    /// An episode carrying no identifier of its own is keyed through its series and its numbering,
    /// which is #23's rule reached from a library rather than from an argument. The series is read
    /// through the manager this type was handed, which is what makes the fact writable at all.
    /// </summary>
    [Fact]
    public void AnEpisodeIsKeyedThroughItsSeriesWhereItCarriesNoIdentifierOfItsOwn()
    {
        var library = new Library();
        var episode = library.Episode("tt0000004", 2, 5);

        var page = new ServerLibrary(library.Manager).ReadPage(0, 10);

        var expected = EpisodeMatchKey.Derive(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0000004" },
            null,
            2,
            5,
            null);

        Assert.Equal(episode, Assert.Single(page).ItemId);
        Assert.Equal(MatchKey.Of(expected.Key!), Assert.Single(page).Key);
    }

    /// <summary>
    /// An episode whose series the library cannot answer for produces no key rather than a key
    /// derived from half of one. It is the state a library is in while a series is being removed,
    /// and a key built from an absent series would match the same absence on the peer.
    /// </summary>
    [Fact]
    public void AnEpisodeWhoseSeriesTheLibraryCannotAnswerForIsNotKeyed()
    {
        var library = new Library();
        var episode = library.Episode("tt0000004", 2, 5);
        library.ForgetTheSeriesOf(episode);

        Assert.Empty(new ServerLibrary(library.Manager).ReadPage(0, 10));
    }

    /// <summary>
    /// What the list is asked for. The index answers which local item carries a key, and a key
    /// belongs to a leaf item, so a season, a series or a box set in the map is a key nothing may
    /// look up. The virtual item is the other half: the library knows about it and holds no file,
    /// so nobody watched it here.
    /// </summary>
    [Fact]
    public void TheListIsOfLeafItemsTheServerActuallyHolds()
    {
        var library = new Library();
        library.Film("tt0000001");

        new ServerLibrary(library.Manager).ReadPage(0, 1);

        var asked = library.LastQuery;

        Assert.NotNull(asked);
        Assert.Equal(new[] { BaseItemKind.Movie, BaseItemKind.Episode }, asked!.IncludeItemTypes);
        Assert.True(asked.Recursive);
        Assert.False(asked.IsVirtualItem);
    }

    /// <summary>
    /// The two arguments the index passes. A negative offset and a page of nothing are both a
    /// caller's mistake rather than an empty library, and answering an empty page to either would
    /// be read by the index as a library that ended there.
    /// </summary>
    [Fact]
    public void AnOffsetBelowZeroAndAPageOfNothingAreRefused()
    {
        var source = new ServerLibrary(new Library().Manager);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadPage(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadPage(0, 0));
    }

    /// <summary>
    /// The walk the index makes, in the same shape: pages until a short one.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="pageSize">How many items one page asks for.</param>
    /// <returns>Every item the walk produced, in order.</returns>
    private static IReadOnlyList<Guid> Walk(IMatchIndexSource source, int pageSize)
    {
        var found = new List<Guid>();

        while (true)
        {
            var page = source.ReadPage(found.Count, pageSize);

            found.AddRange(page.Select(item => item.ItemId));

            if (page.Count < pageSize)
            {
                return found;
            }
        }
    }

    /// <summary>
    /// A library the facts above build item by item.
    ///
    /// It answers the two calls this type makes and counts the one that matters. The items are the
    /// server's own types rather than a shape of this suite's, because what is being judged is a
    /// read of them.
    /// </summary>
    private sealed class Library
    {
        private readonly List<Guid> _ids = new List<Guid>();

        private readonly Dictionary<Guid, BaseItem> _items = new Dictionary<Guid, BaseItem>();

        internal Library()
        {
            var manager = new Mock<ILibraryManager>();

            manager
                .Setup(library => library.GetItemIds(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery query) =>
                {
                    LastQuery = query;
                    SnapshotsTaken++;
                    return _ids.ToList();
                });

            manager
                .Setup(library => library.GetItemById(It.IsAny<Guid>()))
                .Returns((Guid id) => _items.TryGetValue(id, out var item) ? item : null);

            Manager = manager.Object;
        }

        internal ILibraryManager Manager { get; }

        internal int SnapshotsTaken { get; private set; }

        internal InternalItemsQuery? LastQuery { get; private set; }

        internal Guid Film(string imdb)
        {
            var film = new Movie { Id = Guid.NewGuid() };
            film.ProviderIds["Imdb"] = imdb;

            return Add(film);
        }

        internal Guid Unkeyed() => Add(new Movie { Id = Guid.NewGuid() });

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

        internal void Forget(Guid id) => _items.Remove(id);

        internal void ForgetTheSeriesOf(Guid episodeId) =>
            _items.Remove(((Episode)_items[episodeId]).SeriesId);

        private Guid Add(BaseItem item)
        {
            _ids.Add(item.Id);
            _items[item.Id] = item;

            return item.Id;
        }
    }
}

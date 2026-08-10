using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the map from a key to the local item that carries it.
///
/// Two failures are what these are written against. The first is the cost: a sweep asks
/// which item carries a key once per incoming change, and a plugin that answers by scanning
/// the library is unusable at the size where somebody needs it, so the tests measure what a
/// lookup reads from the library rather than how long it took. The second is the outage:
/// an index that empties itself while it is being rebuilt, or while the server rescans a
/// library, reports every item as unmatched for the length of that window, which reads to an
/// operator as a plugin that lost their libraries.
/// </summary>
public class MatchIndexTests
{
    /// <summary>
    /// A library that hands over pages and counts what was asked of it.
    ///
    /// The count is the measurement. Elapsed time would be the obvious thing to assert on and
    /// it is refused by the headless rule for a good reason: it makes the assertion depend on
    /// whichever machine ran the suite. How many items the index asked the library for is a
    /// property of the index, it is the same number on every machine, and it is the number
    /// that actually decides whether a lookup is linear in the size of the library.
    /// </summary>
    private sealed class CountingLibrary : IMatchIndexSource
    {
        private readonly List<KeyedItem> _items;

        public CountingLibrary(IEnumerable<KeyedItem> items)
        {
            _items = items.ToList();
        }

        /// <summary>
        /// Gets how many items the index has read out of the library.
        /// </summary>
        public int ItemsRead { get; private set; }

        /// <summary>
        /// Gets how many pages the index has asked for.
        /// </summary>
        public int PagesRead { get; private set; }

        /// <summary>
        /// Gets the largest page the index has ever asked for.
        /// </summary>
        public int LargestPageAsked { get; private set; }

        /// <summary>
        /// Gets or sets something to run in the middle of a page read, which is how a test
        /// reaches the window while a walk is in flight without a second thread.
        /// </summary>
        public Action<int>? DuringPage { get; set; }

        /// <summary>
        /// Gets or sets a reason to fail, which is how a test drives a walk that does not
        /// finish.
        /// </summary>
        public string? FailsWith { get; set; }

        public void Add(KeyedItem item) => _items.Add(item);

        public void Remove(Guid itemId) => _items.RemoveAll(item => item.ItemId == itemId);

        public void Forget()
        {
            ItemsRead = 0;
            PagesRead = 0;
        }

        public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count)
        {
            if (FailsWith is not null)
            {
                throw new InvalidOperationException(FailsWith);
            }

            PagesRead++;
            LargestPageAsked = Math.Max(LargestPageAsked, count);

            DuringPage?.Invoke(PagesRead);

            var page = _items.Skip(startIndex).Take(count).ToList();

            ItemsRead += page.Count;

            return page;
        }
    }

    /// <summary>
    /// An identifier the numbering of a test can produce, in the one spelling a key compares.
    /// </summary>
    /// <param name="number">The number, which is never zero because no provider allocates it.</param>
    /// <returns>The identifier.</returns>
    private static ProviderIdentifier Identifier(int number)
    {
        var reading = ProviderIdentifier.Normalise(
            IdentifierProvider.Tmdb,
            number.ToString(CultureInfo.InvariantCulture));

        Assert.True(reading.IsUsable);

        return reading.Identifier!;
    }

    /// <summary>
    /// An item identifier a test can write down and recognise again.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns>The identifier.</returns>
    private static Guid Item(int number) =>
        Guid.ParseExact(number.ToString("D32", CultureInfo.InvariantCulture), "N");

    /// <summary>
    /// A library of films, one identifier each, numbered from one.
    /// </summary>
    /// <param name="count">How many.</param>
    /// <returns>The items.</returns>
    private static List<KeyedItem> Films(int count) =>
        Enumerable
            .Range(1, count)
            .Select(number => new KeyedItem(Item(number), MatchKey.Of(Identifier(number))))
            .ToList();

    [Fact]
    public void AKeyOneItemCarriesResolvesToThatItem()
    {
        var library = new CountingLibrary(Films(3));
        var index = new MatchIndex(library);

        var found = index.Lookup(MatchKey.Of(Identifier(2)));

        Assert.Equal(MatchAnswer.Matched, found.Answer);
        Assert.True(found.IsMatched);
        Assert.Equal(Item(2), found.Item);
        Assert.Empty(found.CompetingItems);
    }

    [Fact]
    public void AKeyNoItemCarriesIsNoMatch()
    {
        var library = new CountingLibrary(Films(3));
        var index = new MatchIndex(library);

        var found = index.Lookup(MatchKey.Of(Identifier(99)));

        Assert.Equal(MatchAnswer.NoMatch, found.Answer);
        Assert.False(found.IsMatched);
        Assert.Equal(Guid.Empty, found.Item);
        Assert.Empty(found.CompetingItems);
    }

    /// <summary>
    /// The same film added twice from two libraries. Nothing moves, and both competing items
    /// are in the answer, because an operator who is told which two items claim the key can
    /// repair the library and one who is told only that something was ambiguous cannot.
    /// </summary>
    [Fact]
    public void TwoItemsClaimingOneKeyIsAnAmbiguityThatNamesBoth()
    {
        var films = Films(2);
        films.Add(new KeyedItem(Item(50), MatchKey.Of(Identifier(1))));

        var index = new MatchIndex(new CountingLibrary(films));

        var found = index.Lookup(MatchKey.Of(Identifier(1)));

        Assert.Equal(MatchAnswer.Ambiguous, found.Answer);
        Assert.False(found.IsMatched);
        Assert.Equal(Guid.Empty, found.Item);
        Assert.Equal(new[] { Item(1), Item(50) }, found.CompetingItems);
    }

    /// <summary>
    /// A scraper that wrote a series' identifier onto an episode leaves the episode carrying
    /// an identifier a film can carry too. The kind travels inside the key, so the two are two
    /// keys and neither the wrong-item write nor a false ambiguity is available.
    /// </summary>
    [Fact]
    public void AFilmAndAnEpisodeCarryingOneIdentifierAreTwoKeys()
    {
        var identifiers = new Dictionary<string, string> { { "Tmdb", "1" } };
        var episode = EpisodeMatchKey.Derive(identifiers, null, null, null, null, null);

        Assert.True(episode.IsKeyed);

        var film = new KeyedItem(Item(1), MatchKey.Of(Identifier(1)));
        var withTheSameNumber = new KeyedItem(Item(2), MatchKey.Of(episode.Key!));

        var index = new MatchIndex(new CountingLibrary([film, withTheSameNumber]));

        Assert.Equal(Item(1), index.Lookup(film.Key).Item);
        Assert.Equal(Item(2), index.Lookup(withTheSameNumber.Key).Item);
    }

    /// <summary>
    /// The walk is what bounds a build's memory, so what it asks the library for is bounded
    /// rather than the library being handed over whole.
    /// </summary>
    [Fact]
    public void AWalkAsksForOnePageAtATimeAndReadsTheWholeLibrary()
    {
        var library = new CountingLibrary(Films((MatchIndex.PageSize * 2) + 7));
        var index = new MatchIndex(library);

        index.Rebuild();

        Assert.Equal(MatchIndex.PageSize, library.LargestPageAsked);
        Assert.Equal(3, library.PagesRead);
        Assert.Equal((MatchIndex.PageSize * 2) + 7, library.ItemsRead);
        Assert.Equal(Item(1), index.Lookup(MatchKey.Of(Identifier(1))).Item);
        Assert.Equal(
            Item((MatchIndex.PageSize * 2) + 7),
            index.Lookup(MatchKey.Of(Identifier((MatchIndex.PageSize * 2) + 7))).Item);
    }

    /// <summary>
    /// The cost of a lookup, measured as what it reads out of the library rather than as
    /// elapsed time. A scan would read every item; this reads none, and it reads none for a
    /// library a hundred times larger, which is the whole reason the index exists.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(10_000)]
    public void ALookupReadsNothingFromTheLibraryHoweverLargeItIs(int size)
    {
        var library = new CountingLibrary(Films(size));
        var index = new MatchIndex(library);

        index.Rebuild();
        library.Forget();

        for (var number = 1; number <= 50; number++)
        {
            Assert.Equal(Item(number), index.Lookup(MatchKey.Of(Identifier(number))).Item);
        }

        Assert.Equal(0, library.ItemsRead);
        Assert.Equal(0, library.PagesRead);
    }

    /// <summary>
    /// The index is a cache and never a record. Clearing it mid-run costs a second walk and
    /// changes not one answer, which is the property that makes it safe to throw away at any
    /// moment.
    /// </summary>
    [Fact]
    public void ClearingTheIndexMidRunChangesNoAnswerAndOnlyCostsAWalk()
    {
        var library = new CountingLibrary(Films(10));
        var index = new MatchIndex(library);

        var uninterrupted = new List<Guid>();
        var interrupted = new List<Guid>();

        for (var number = 1; number <= 10; number++)
        {
            uninterrupted.Add(index.Lookup(MatchKey.Of(Identifier(number))).Item);
        }

        var afterOneWalk = library.ItemsRead;

        for (var number = 1; number <= 10; number++)
        {
            if (number == 5)
            {
                index.Clear();

                Assert.False(index.IsBuilt);
            }

            interrupted.Add(index.Lookup(MatchKey.Of(Identifier(number))).Item);
        }

        Assert.Equal(uninterrupted, interrupted);
        Assert.Equal(afterOneWalk * 2, library.ItemsRead);
    }

    [Fact]
    public void AnItemTheLibraryGainsIsFoundAndOneItLosesIsNot()
    {
        var index = new MatchIndex(new CountingLibrary(Films(3)));

        index.Rebuild();

        index.ItemAdded(new KeyedItem(Item(4), MatchKey.Of(Identifier(4))));

        Assert.Equal(Item(4), index.Lookup(MatchKey.Of(Identifier(4))).Item);

        index.ItemRemoved(Item(4));

        Assert.Equal(MatchAnswer.NoMatch, index.Lookup(MatchKey.Of(Identifier(4))).Answer);
        Assert.Equal(Item(1), index.Lookup(MatchKey.Of(Identifier(1))).Item);
    }

    /// <summary>
    /// The ordinary reason an item is updated is that somebody repaired the metadata this
    /// plugin keys on. The item leaves the key it no longer produces, because an index that
    /// kept it there would answer with an item that has moved on.
    /// </summary>
    [Fact]
    public void AnItemWhoseKeyChangedLeavesTheKeyItNoLongerProduces()
    {
        var index = new MatchIndex(new CountingLibrary(Films(3)));

        index.Rebuild();

        index.ItemUpdated(new KeyedItem(Item(2), MatchKey.Of(Identifier(77))));

        Assert.Equal(MatchAnswer.NoMatch, index.Lookup(MatchKey.Of(Identifier(2))).Answer);
        Assert.Equal(Item(2), index.Lookup(MatchKey.Of(Identifier(77))).Item);
    }

    /// <summary>
    /// An item that stops claiming a key leaves an ambiguity behind it, and the item that is
    /// left is a match rather than one of two competing items. Without the reverse map the
    /// departing item would stay listed and the key would answer ambiguous for good.
    /// </summary>
    [Fact]
    public void ResolvingAnAmbiguityInTheLibraryProducesTheMatchThatWasWithheld()
    {
        var films = Films(1);
        films.Add(new KeyedItem(Item(50), MatchKey.Of(Identifier(1))));

        var index = new MatchIndex(new CountingLibrary(films));

        Assert.Equal(MatchAnswer.Ambiguous, index.Lookup(MatchKey.Of(Identifier(1))).Answer);

        index.ItemRemoved(Item(50));

        var found = index.Lookup(MatchKey.Of(Identifier(1)));

        Assert.Equal(MatchAnswer.Matched, found.Answer);
        Assert.Equal(Item(1), found.Item);
    }

    /// <summary>
    /// A rescan takes each item out and puts it back. Only the item being rescanned is out of
    /// the index at any moment, so the rest of the library stays matched for the whole of it.
    /// The failure this refuses is an index that treats a removal as a reason to start again,
    /// which turns a rescan into a window where nothing matches at all.
    /// </summary>
    [Fact]
    public void ARescanLeavesEveryOtherItemMatchedForTheWholeOfIt()
    {
        var index = new MatchIndex(new CountingLibrary(Films(6)));

        index.Rebuild();

        for (var rescanned = 1; rescanned <= 6; rescanned++)
        {
            index.ItemRemoved(Item(rescanned));

            for (var other = 1; other <= 6; other++)
            {
                var found = index.Lookup(MatchKey.Of(Identifier(other)));

                if (other == rescanned)
                {
                    Assert.Equal(MatchAnswer.NoMatch, found.Answer);
                }
                else
                {
                    Assert.Equal(Item(other), found.Item);
                }
            }

            index.ItemAdded(new KeyedItem(Item(rescanned), MatchKey.Of(Identifier(rescanned))));
        }

        for (var number = 1; number <= 6; number++)
        {
            Assert.Equal(Item(number), index.Lookup(MatchKey.Of(Identifier(number))).Item);
        }
    }

    /// <summary>
    /// A rebuild builds a new map and swaps it in, so a lookup during the walk is answered by
    /// the map that is still in place. Clearing first and refilling would report the whole
    /// library as unmatched for the length of the walk, and at the size this index is for that
    /// window is long enough for a sweep to run inside it.
    /// </summary>
    [Fact]
    public void ARebuildAnswersFromThePreviousMapUntilTheNewOneIsWhole()
    {
        var library = new CountingLibrary(Films(MatchIndex.PageSize + 4));
        var index = new MatchIndex(library);

        index.Rebuild();

        var duringTheWalk = new List<MatchAnswer>();

        library.DuringPage = _ =>
            duringTheWalk.Add(index.Lookup(MatchKey.Of(Identifier(1))).Answer);

        index.Rebuild();

        Assert.NotEmpty(duringTheWalk);
        Assert.All(duringTheWalk, answer => Assert.Equal(MatchAnswer.Matched, answer));
    }

    /// <summary>
    /// An item added while a walk is in flight is in the index when the walk finishes. It is
    /// not in the pages the walk read, because it was not in the library when they were taken,
    /// so an index that dropped what arrived during a rebuild would lose it until the next one.
    /// </summary>
    [Fact]
    public void AnEventThatArrivesDuringAWalkIsAppliedRatherThanLost()
    {
        var library = new CountingLibrary(Films(4));
        var index = new MatchIndex(library);

        library.DuringPage = page =>
        {
            if (page != 1)
            {
                return;
            }

            index.ItemAdded(new KeyedItem(Item(90), MatchKey.Of(Identifier(90))));
            index.ItemRemoved(Item(3));
        };

        index.Rebuild();

        Assert.Equal(Item(90), index.Lookup(MatchKey.Of(Identifier(90))).Item);
        Assert.Equal(MatchAnswer.NoMatch, index.Lookup(MatchKey.Of(Identifier(3))).Answer);
        Assert.Equal(Item(1), index.Lookup(MatchKey.Of(Identifier(1))).Item);
    }

    /// <summary>
    /// A walk that does not finish hands over nothing. The map that was already in place keeps
    /// answering, and what arrived during the failed walk is applied to it rather than
    /// discarded with the half-built one.
    /// </summary>
    [Fact]
    public void AWalkThatFailsLeavesThePreviousAnswersStanding()
    {
        var library = new CountingLibrary(Films(3));
        var index = new MatchIndex(library);

        index.Rebuild();

        library.FailsWith = "the library was not readable";

        Assert.Throws<InvalidOperationException>(index.Rebuild);

        Assert.True(index.IsBuilt);
        Assert.Equal(Item(1), index.Lookup(MatchKey.Of(Identifier(1))).Item);
    }
}

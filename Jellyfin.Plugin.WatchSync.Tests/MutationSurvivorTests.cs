using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Behaviour the matcher already had and nothing asserted.
///
/// Every fact here exists because a mutation run changed the line it is about and the whole
/// suite stayed green. That is a different question from coverage: each of these lines was
/// executed, and the tests that executed them would not have noticed them being wrong. The
/// run that found them, what it left alive on purpose and why, is <c>docs/mutation.md</c>.
///
/// They are grouped by where they came from rather than by type on purpose. A reader who
/// changes one of these lines and sees a test here go red gets the reason in one place, and
/// the alternative is eleven assertions scattered through five files with nothing saying
/// what they are for.
/// </summary>
public class MutationSurvivorTests
{
    /// <summary>
    /// A series carrying one identifier, so an episode below can be keyed from it.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Series =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "121361" };

    /// <summary>
    /// An episode carrying nothing of its own, which is the ordinary case.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Nothing =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The bare prefix and no number at all.
    ///
    /// It is refused for its shape rather than for its length, and the difference is the one
    /// a mutation run found: with the empty run of characters read as a run of digits, an
    /// empty value passes the digit test vacuously and falls through to the seven digit floor,
    /// so a field holding nothing but <c>tt</c> would be reported as an IMDb identifier that
    /// is too short. That names the wrong repair to whoever reads it: a scraper wrote a
    /// prefix and no identifier, and padding it would not help.
    /// </summary>
    [Fact]
    public void TheImdbPrefixWithNoNumberIsRefusedForItsShapeAndNotForItsLength()
    {
        var reading = ProviderIdentifier.Normalise(IdentifierProvider.Imdb, "tt");

        Assert.False(reading.IsUsable, "a bare tt was accepted as an identifier.");
        Assert.Equal(IdentifierRefusal.NotTheProvidersShape, reading.Refusal);
    }

    /// <summary>
    /// Nothing compares equal to nothing.
    ///
    /// The short circuit in front of each of these is what makes the null check load bearing:
    /// read as an or rather than an and, the comparison reaches for a field of the value it
    /// has just established is absent. Both keys carry the same shape and both are asserted,
    /// because a test over one of them says nothing about the other.
    /// </summary>
    [Fact]
    public void AKeyIsNotEqualToTheAbsenceOfOne()
    {
        var film = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0111161" });
        var episode = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 2, 5, null);

        Assert.True(film.IsKeyed, $"the film produced no key: {film.Refusal}.");
        Assert.True(episode.IsKeyed, $"the episode produced no key: {episode.Refusal}.");

        Assert.False(episode.Key!.Equals(null), "an episode key compared equal to nothing.");
        Assert.False(MatchKey.Of(film.Key!).Equals(null), "a film key compared equal to nothing.");
        Assert.False(MatchKey.Of(episode.Key!).Equals(null), "an episode key compared equal to nothing.");
        Assert.False(film.Key!.Equals(null), "an identifier compared equal to nothing.");
    }

    /// <summary>
    /// The written form of a key is the form of the kind it is.
    ///
    /// It is what a record, a diagnostic and an envelope will write, and nothing had ever read
    /// it, so the branch choosing between the two derivations was free to pick either one. A
    /// film key written in the episode form, or the reverse, is the kind of value that reads
    /// as data loss in a support thread months later.
    /// </summary>
    [Fact]
    public void TheWrittenFormOfAKeyIsTheFormOfItsKind()
    {
        var film = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0111161" });
        var episode = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 2, 5, null);

        Assert.Equal("Imdb:tt0111161", MatchKey.Of(film.Key!).Value);
        Assert.Equal("episode/Tvdb:121361/airdate/s2e5", MatchKey.Of(episode.Key!).Value);
    }

    /// <summary>
    /// A key made of nothing is refused where it is made rather than where it is used.
    ///
    /// There is no public constructor on a key, so these two are the whole of the way one is
    /// held. Without the refusal here the absence travels: it reaches the index, is stored
    /// under a kind it does not have, and surfaces as a failure inside a lookup that names
    /// neither the caller nor the field that was empty.
    /// </summary>
    [Fact]
    public void AKeyIsNotMadeFromNothing()
    {
        Assert.Throws<ArgumentNullException>(() => MatchKey.Of((ProviderIdentifier)null!));
        Assert.Throws<ArgumentNullException>(() => MatchKey.Of((EpisodeMatchKey)null!));
    }

    /// <summary>
    /// The index refuses what it is handed rather than failing later inside itself.
    ///
    /// Every one of these is a public entry point that the registrar in #8 and the library
    /// adapter in #29 will call, and the difference the refusal makes is which failure an
    /// operator reads. An argument refused at the door names the argument. The same absence
    /// carried one call further names a field of a type they have never heard of.
    /// </summary>
    [Fact]
    public void TheIndexRefusesAnAbsentArgumentAtTheDoor()
    {
        var index = new MatchIndex(new EmptyLibrary());

        Assert.Throws<ArgumentNullException>(() => new MatchIndex(null!));
        Assert.Throws<ArgumentNullException>(() => index.ItemAdded(null!));
        Assert.Throws<ArgumentNullException>(() => index.ItemUpdated(null!));
    }

    /// <summary>
    /// An item without the key it is supposed to carry is not an item the index holds.
    ///
    /// The constructor is the whole of the way one of these is made, and without the refusal
    /// the absence is simply stored: the item goes into the walk, into the map, and fails at
    /// whichever lookup reaches it, a long way from the adapter that built it.
    /// </summary>
    [Fact]
    public void AKeyedItemWithoutItsKeyIsRefusedWhereItIsMade()
    {
        Assert.Throws<ArgumentNullException>(() => new KeyedItem(Guid.NewGuid(), null!));
    }

    /// <summary>
    /// A lookup of nothing costs nothing.
    ///
    /// The exception is not what distinguishes this one, and that is the point. A dictionary
    /// asked for a null key raises the same kind of failure the refusal does, so a test that
    /// only asserted the type would pass with the refusal deleted. What the refusal buys is
    /// the order: it happens before the index decides it needs to walk the library, so a
    /// caller that lost a key does not also pay for a full build to be told about it.
    /// </summary>
    [Fact]
    public void ALookupOfNothingIsRefusedBeforeTheLibraryIsRead()
    {
        var library = new CountingLibrary();
        var index = new MatchIndex(library);

        Assert.Throws<ArgumentNullException>(() => index.Lookup(null!));
        Assert.Equal(0, library.PagesRead);
        Assert.False(index.IsBuilt, "a refused lookup built the index.");
    }

    /// <summary>
    /// A library that answers with nothing at all ends the walk instead of taking the index
    /// down with it.
    ///
    /// The interface says a page is a list and the end of the library is a short one, so a
    /// null is a source breaking its own contract. It is worth holding anyway: the
    /// implementations of that interface are a fake here and, once #29's adapter lands, a
    /// read of somebody else's library manager, and an index that threw on the first page
    /// would take out every lookup on the server rather than the one item it could not read.
    /// </summary>
    [Fact]
    public void ALibraryThatAnswersWithNothingEndsTheWalkRatherThanThrowing()
    {
        var index = new MatchIndex(new NullPagedLibrary());

        var film = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0111161" });

        var lookup = index.Lookup(MatchKey.Of(film.Key!));

        Assert.Equal(MatchAnswer.NoMatch, lookup.Answer);
        Assert.True(index.IsBuilt, "a walk that read no page left the index reporting itself unbuilt.");
    }

    /// <summary>
    /// A library holding nothing, which is the shape the argument refusals need and nothing
    /// more.
    /// </summary>
    private sealed class EmptyLibrary : IMatchIndexSource
    {
        public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count) => Array.Empty<KeyedItem>();
    }

    /// <summary>
    /// A library holding nothing that says whether it was asked. Whether a walk happened is
    /// the observable difference a refusal at the door makes, and the count is how a test
    /// reads it without measuring elapsed time, which the headless rule refuses.
    /// </summary>
    private sealed class CountingLibrary : IMatchIndexSource
    {
        public int PagesRead { get; private set; }

        public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count)
        {
            PagesRead++;

            return Array.Empty<KeyedItem>();
        }
    }

    /// <summary>
    /// A library that returns no list at all rather than an empty one. The interface refuses
    /// this at compile time on a caller that has nullable references on; it says nothing about
    /// an implementation compiled without them, which is every implementation this plugin does
    /// not own.
    /// </summary>
    private sealed class NullPagedLibrary : IMatchIndexSource
    {
        public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count) => null!;
    }
}

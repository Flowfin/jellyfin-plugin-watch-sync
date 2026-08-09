using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the key one episode is matched by on two servers that number a series differently.
///
/// The failure this is written against is the one the prior art keeps producing: a history
/// import that marks the wrong episodes, because a season and an episode number are a
/// position inside an ordering rather than an identity, and the two servers were not in the
/// same ordering. A missed match costs an operator a line in a record. A wrong match writes
/// one person's watch state onto an episode they have never seen and looks like a working
/// sync while it does it.
/// </summary>
public class EpisodeMatchKeyTests
{
    /// <summary>
    /// A series carrying one identifier, which the episodes below fall back to.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Series =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "121361" };

    /// <summary>
    /// An episode carrying nothing of its own, which is the ordinary case.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Nothing =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The display orders the server documents, spelled as a scraper or an operator might
    /// leave them, with the ordering each one normalises to.
    /// </summary>
    /// <returns>The stored display order and the ordering the key carries.</returns>
    public static TheoryData<string?, string> StoredOrderings() => new TheoryData<string?, string>
    {
        { null, "airdate" },
        { string.Empty, "airdate" },
        { "   ", "airdate" },
        { "airdate", "airdate" },
        { "Airdate", "airdate" },
        { "  DVD  ", "dvd" },
        { "absolute", "absolute" },
    };

    /// <summary>
    /// An episode with no identifier of its own is keyed by its series, the ordering, the
    /// season and the episode number, and by nothing else.
    /// </summary>
    [Fact]
    public void AnEpisodeIsKeyedByItsSeriesAndItsPosition()
    {
        var reading = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 2, 5, null);

        Assert.True(reading.IsKeyed, $"the episode produced no key: {reading.Refusal}.");
        Assert.Equal("episode/Tvdb:121361/airdate/s2e5", reading.Key!.ToString());
        Assert.Equal("Tvdb:121361", reading.Key.Identifier.ToString());
        Assert.Equal("airdate", reading.Key.Ordering);
        Assert.Equal(2, reading.Key.SeasonNumber);
        Assert.Equal(5, reading.Key.EpisodeNumber);
        Assert.False(reading.Key.IsTheEpisodesOwnIdentifier);
    }

    /// <summary>
    /// An episode carrying an identifier of its own is keyed by it, and the position does not
    /// enter the key at all.
    ///
    /// An identifier names one work. A position names a slot two servers can disagree about,
    /// so where both are available the identifier is the stronger of the two and the numbers
    /// are not carried alongside it. Carrying them would mean an episode renumbered on one
    /// side stops matching a peer that identifies it correctly.
    /// </summary>
    [Fact]
    public void AnEpisodeWithItsOwnIdentifierIsKeyedByItRatherThanByItsPosition()
    {
        var reading = EpisodeMatchKey.Derive(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0959621" },
            Series,
            "dvd",
            2,
            5,
            null);

        Assert.Equal("episode/Imdb:tt0959621", reading.Key!.ToString());
        Assert.True(reading.Key.IsTheEpisodesOwnIdentifier);
        Assert.Null(reading.Key.Ordering);
        Assert.Null(reading.Key.SeasonNumber);
        Assert.Null(reading.Key.EpisodeNumber);
    }

    /// <summary>
    /// The same series held under two orderings produces two keys, which is no match rather
    /// than a wrong one.
    ///
    /// This is the case the whole design is for. Season 2 episode 5 in airdate order and
    /// season 2 episode 5 in DVD order are routinely different episodes, and the two servers
    /// have no way to discover that from the numbers. A key that left the ordering out would
    /// make them agree, and the agreement would move one person's watch state onto an episode
    /// they have not seen.
    /// </summary>
    [Fact]
    public void TheSameNumbersUnderTwoOrderingsAreTwoKeys()
    {
        var here = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 2, 5, null);
        var there = EpisodeMatchKey.Derive(Nothing, Series, "dvd", 2, 5, null);

        Assert.True(here.IsKeyed);
        Assert.True(there.IsKeyed);
        Assert.NotEqual(here.Key!.ToString(), there.Key!.ToString());
        Assert.NotEqual(here.Key, there.Key);
    }

    /// <summary>
    /// Two servers in the same ordering agree, however each of them spells it and whether
    /// either of them stores it at all.
    ///
    /// The server stores nothing for its default, so one side leaving the field empty while
    /// the other spells out airdate is the ordinary case rather than a defect. Reading those
    /// as two orderings would leave the majority of libraries unable to match a single
    /// episode, which is the opposite failure to the one above and just as bad.
    /// </summary>
    /// <param name="stored">The display order as the series stores it.</param>
    /// <param name="ordering">The ordering the key carries.</param>
    [Theory]
    [MemberData(nameof(StoredOrderings))]
    public void TheOrderingIsCompareableHoweverTheSeriesSpelledIt(string? stored, string ordering)
    {
        var reading = EpisodeMatchKey.Derive(Nothing, Series, stored, 1, 1, null);

        Assert.Equal(ordering, reading.Key!.Ordering);
        Assert.Equal($"episode/Tvdb:121361/{ordering}/s1e1", reading.Key.ToString());
    }

    /// <summary>
    /// An ordering this plugin does not recognise is carried rather than refused.
    ///
    /// A line that adds a fourth ordering would otherwise stop every series under it from
    /// matching, and two servers holding the same unrecognised value are not disagreeing about
    /// anything. What it does not do is make the value agree with one of the three: the
    /// unrecognised ordering keys apart from airdate, so the fallback is a missed match rather
    /// than a wrong one.
    /// </summary>
    [Fact]
    public void AnUnrecognisedOrderingIsCarriedRatherThanFoldedOntoAKnownOne()
    {
        var one = EpisodeMatchKey.Derive(Nothing, Series, "story", 1, 1, null);
        var other = EpisodeMatchKey.Derive(Nothing, Series, "STORY", 1, 1, null);
        var airdate = EpisodeMatchKey.Derive(Nothing, Series, null, 1, 1, null);

        Assert.Equal("episode/Tvdb:121361/story/s1e1", one.Key!.ToString());
        Assert.Equal(one.Key, other.Key);
        Assert.NotEqual(one.Key, airdate.Key);
    }

    /// <summary>
    /// A special is keyed like any other episode, and season zero is a position rather than a
    /// missing number.
    ///
    /// That is the defined answer this issue asks for, and the reason is that the server
    /// stores the zero. Both sides scraped the specials from the same source under the same
    /// ordering, so they agree on the number the same way they agree on any other, and the
    /// ordering in the key is what covers the case where they did not.
    ///
    /// The second half is what makes it bite. Zero is refused everywhere else in this
    /// matcher, because no provider allocates identifier zero, and a rule copied across from
    /// there would silently stop every special from matching while every test about ordinary
    /// episodes stayed green.
    /// </summary>
    [Fact]
    public void ASpecialIsKeyedBySeasonZeroRatherThanRefused()
    {
        var special = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 0, 3, null);
        var firstSpecial = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 0, 0, null);

        Assert.True(special.IsKeyed, $"a special produced no key: {special.Refusal}.");
        Assert.Equal("episode/Tvdb:121361/airdate/s0e3", special.Key!.ToString());

        Assert.True(firstSpecial.IsKeyed, $"a special numbered zero produced no key: {firstSpecial.Refusal}.");
        Assert.Equal("episode/Tvdb:121361/airdate/s0e0", firstSpecial.Key!.ToString());

        Assert.NotEqual(
            special.Key,
            EpisodeMatchKey.Derive(Nothing, Series, "airdate", 3, 0, null).Key);
    }

    /// <summary>
    /// A number below zero is not a position, and it is a different answer from having no
    /// number at all.
    /// </summary>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void ANumberBelowZeroProducesNoKey(int season, int episode)
    {
        var reading = EpisodeMatchKey.Derive(Nothing, Series, "airdate", season, episode, null);

        Assert.False(reading.IsKeyed);
        Assert.Null(reading.Key);
        Assert.Equal(MatchKeyRefusal.NumberingBelowZero, reading.Refusal);
    }

    /// <summary>
    /// An episode the server could not place produces no key, and says which of the two
    /// numbers it is missing.
    /// </summary>
    [Fact]
    public void AnEpisodeWithNoPositionProducesNoKey()
    {
        var noSeason = EpisodeMatchKey.Derive(Nothing, Series, "airdate", null, 5, null);
        var noEpisode = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 2, null, null);
        var neither = EpisodeMatchKey.Derive(Nothing, Series, "airdate", null, null, null);

        Assert.False(noSeason.IsKeyed);
        Assert.Null(noSeason.Key);
        Assert.Equal(MatchKeyRefusal.NoSeasonNumber, noSeason.Refusal);

        Assert.False(noEpisode.IsKeyed);
        Assert.Null(noEpisode.Key);
        Assert.Equal(MatchKeyRefusal.NoEpisodeNumber, noEpisode.Refusal);

        Assert.Equal(MatchKeyRefusal.NoSeasonNumber, neither.Refusal);
    }

    /// <summary>
    /// One file covering several episodes is refused with its own reason, rather than keyed on
    /// the first of the numbers it covers.
    ///
    /// A file holding episodes 1 and 2 holds one watch state for both. Keying it on episode 1
    /// would send that state to episode 1 on the peer and leave episode 2 unwatched there, or
    /// worse, take a peer's episode 1 state and apply it to the file, marking two episodes
    /// from one. This is the mass marking the rest of the plan refuses, arriving through the
    /// numbering rather than through an aggregate.
    ///
    /// The refusal comes before the episode's own identifier is looked at, which is the half a
    /// reader would not expect. An identifier on a multi-episode file names the first episode
    /// of the run and nothing about the rest, so keying on it is the same failure wearing a
    /// stronger looking key.
    /// </summary>
    [Fact]
    public void AFileCoveringSeveralEpisodesProducesNoKey()
    {
        var run = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 1, 1, 2);

        Assert.False(run.IsKeyed);
        Assert.Null(run.Key);
        Assert.Equal(MatchKeyRefusal.SpansSeveralEpisodes, run.Refusal);

        var runCarryingAnIdentifier = EpisodeMatchKey.Derive(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0959621" },
            Series,
            "airdate",
            1,
            1,
            2);

        Assert.False(runCarryingAnIdentifier.IsKeyed);
        Assert.Equal(MatchKeyRefusal.SpansSeveralEpisodes, runCarryingAnIdentifier.Refusal);

        var runWithNoFirstNumber = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 1, null, 2);

        Assert.Equal(MatchKeyRefusal.SpansSeveralEpisodes, runWithNoFirstNumber.Refusal);
    }

    /// <summary>
    /// A file whose last number is its only number covers one episode, so it is keyed.
    ///
    /// The server writes the end number on a single episode often enough that treating its
    /// presence as the test would refuse ordinary episodes in bulk. What makes a run is the two
    /// numbers differing.
    /// </summary>
    [Fact]
    public void AFileWhoseLastNumberIsItsFirstIsOneEpisode()
    {
        var reading = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 1, 4, 4);

        Assert.True(reading.IsKeyed, $"a single episode produced no key: {reading.Refusal}.");
        Assert.Equal("episode/Tvdb:121361/airdate/s1e4", reading.Key!.ToString());
    }

    /// <summary>
    /// An episode whose series carries no usable identifier produces no key, with the reason
    /// the series' own metadata gives.
    ///
    /// The reason is about the series rather than about the episode, and that is the whole
    /// point of reporting it: an operator repairing the series repairs every episode under it
    /// at once.
    /// </summary>
    [Fact]
    public void AnEpisodeWhoseSeriesHasNoUsableIdentifierProducesNoKey()
    {
        var nothing = EpisodeMatchKey.Derive(Nothing, Nothing, "airdate", 1, 1, null);

        Assert.False(nothing.IsKeyed);
        Assert.Null(nothing.Key);
        Assert.Equal(MatchKeyRefusal.NoIdentifierAtAll, nothing.Refusal);

        var someOtherProvider = EpisodeMatchKey.Derive(
            Nothing,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TvMaze"] = "82" },
            "airdate",
            1,
            1,
            null);

        Assert.Equal(MatchKeyRefusal.NoIdentifierFromAPreferredProvider, someOtherProvider.Refusal);

        var malformed = EpisodeMatchKey.Derive(
            Nothing,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tvdb"] = "https://thetvdb.com/series/lost",
            },
            "airdate",
            1,
            1,
            null);

        Assert.Equal(MatchKeyRefusal.EveryPreferredIdentifierWasRefused, malformed.Refusal);
    }

    /// <summary>
    /// An episode's own identifier that the normal form refuses does not refuse the episode.
    ///
    /// It falls through to the derived key, because the derived key is the ordinary path for an
    /// episode rather than a weaker second attempt. An episode with a URL in its provider field
    /// and a well scraped series is an episode this plugin can still match.
    /// </summary>
    [Fact]
    public void AMalformedIdentifierOnTheEpisodeFallsThroughToTheDerivedKey()
    {
        var reading = EpisodeMatchKey.Derive(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Imdb"] = "https://www.imdb.com/title/tt0959621/",
            },
            Series,
            "airdate",
            2,
            5,
            null);

        Assert.True(reading.IsKeyed, $"the episode produced no key: {reading.Refusal}.");
        Assert.Equal("episode/Tvdb:121361/airdate/s2e5", reading.Key!.ToString());
    }

    /// <summary>
    /// The position is written with a separator per number rather than padded to a fixed width.
    ///
    /// The server's own user data key pads both numbers to three digits and concatenates them.
    /// Copied here, that reads season 1 episode 1000 and season 11 episode 0 as one key, and
    /// two servers each holding one of the two would agree about two different episodes. The
    /// numbers are ordinary and the collision is silent, which is the combination this test
    /// exists for.
    /// </summary>
    [Fact]
    public void ThePositionCannotCollideWhenANumberOutgrowsThreeDigits()
    {
        var deepIntoOneSeason = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 1, 1000, null);
        var earlyInAnotherSeason = EpisodeMatchKey.Derive(Nothing, Series, "airdate", 11, 0, null);

        Assert.Equal("episode/Tvdb:121361/airdate/s1e1000", deepIntoOneSeason.Key!.ToString());
        Assert.Equal("episode/Tvdb:121361/airdate/s11e0", earlyInAnotherSeason.Key!.ToString());
        Assert.NotEqual(deepIntoOneSeason.Key!.ToString(), earlyInAnotherSeason.Key!.ToString());
        Assert.NotEqual(deepIntoOneSeason.Key, earlyInAnotherSeason.Key);
    }

    /// <summary>
    /// An episode key and a film key never render to one string, even when both rest on the
    /// same identifier.
    ///
    /// The two are different types here, so nothing can compare them by accident today. A key
    /// that has been written into a store, a log or an envelope is a string, and there the kind
    /// is the only thing keeping the two apart.
    /// </summary>
    [Fact]
    public void AnEpisodeKeyAndAFilmKeyAreNeverTheSameString()
    {
        var identifiers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Imdb"] = "tt0959621",
        };

        var episode = EpisodeMatchKey.Derive(identifiers, Series, "airdate", 2, 5, null);
        var film = MovieMatchKey.Derive(identifiers);

        Assert.NotEqual(film.Key!.ToString(), episode.Key!.ToString());
        Assert.Contains(film.Key.ToString(), episode.Key.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing about the file is handed to the derivation.
    ///
    /// #25 refuses a path, a file name, a container, a size and a hash by name. This is the
    /// shape half of that refusal: the derivation is given six named facts, and there is no
    /// item, no media source and no path among them for a later change to reach into. A change
    /// that adds a seventh parameter has to move this test to do it, which is the point at
    /// which somebody reads the refusal again.
    /// </summary>
    [Fact]
    public void TheDerivationIsHandedNothingButIdentifiersAnOrderingAndNumbers()
    {
        var expected = new (string Name, Type Type)[]
        {
            ("episodeProviderIdentifiers", typeof(IReadOnlyDictionary<string, string>)),
            ("seriesProviderIdentifiers", typeof(IReadOnlyDictionary<string, string>)),
            ("seriesDisplayOrder", typeof(string)),
            ("seasonNumber", typeof(int?)),
            ("episodeNumber", typeof(int?)),
            ("lastEpisodeNumberInTheItem", typeof(int?)),
        };

        var methods = typeof(EpisodeMatchKey)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .ToList();

        var derive = Assert.Single(methods, method => method.Name == nameof(EpisodeMatchKey.Derive));

        Assert.Equal(
            expected.Select(parameter => $"{parameter.Name}: {parameter.Type}").ToList(),
            derive.GetParameters().Select(parameter => $"{parameter.Name}: {parameter.ParameterType}").ToList());

        Assert.Equal(new[] { nameof(EpisodeMatchKey.Derive) }, methods.Select(method => method.Name).ToArray());
    }
}

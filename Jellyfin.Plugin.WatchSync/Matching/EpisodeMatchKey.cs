using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// The key one episode is matched by across two servers.
///
/// An episode is the hard case, and it is where this class of tool produces its worst
/// outcomes: an import that marks the wrong episodes because the two sides number a series
/// differently. A film is one work with one identifier; an episode usually has no identifier
/// of its own and is named by where it sits in a series, which is a position rather than an
/// identity.
///
/// So the key is one of two things, and <c>docs/matching.md</c> fixes both. Where the episode
/// carries a provider identifier of its own it is keyed by that, because an identifier is
/// unambiguous where a position is only conventional. Otherwise it is keyed by its series'
/// identifier, the ordering that series was matched under, the season number and the episode
/// number, and the ordering travels inside the key so that two servers holding the series
/// under different orderings record an unmatched item rather than agreeing on the wrong
/// episode. <see cref="SeriesOrdering"/> is where that argument is written.
///
/// Nothing about the file reaches the key. #25 refuses a path, a file name, a container, a
/// size and a hash by name, and the derivation below is handed none of them.
/// </summary>
public sealed class EpisodeMatchKey : IEquatable<EpisodeMatchKey>
{
    /// <summary>
    /// What every rendering of an episode key starts with.
    ///
    /// It is there so that an episode keyed by its own identifier and a film keyed by the
    /// same identifier never render to one string. The two are different types here and
    /// cannot be compared by accident, but a key that has been written into a store, a log or
    /// an envelope is a string, and at that point the kind is the only thing keeping them
    /// apart.
    /// </summary>
    private const string Kind = "episode";

    private EpisodeMatchKey(
        ProviderIdentifier identifier,
        string? ordering,
        int? seasonNumber,
        int? episodeNumber)
    {
        Identifier = identifier;
        Ordering = ordering;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
    }

    /// <summary>
    /// Gets the identifier the key rests on: the episode's own where it carries one, and its
    /// series' otherwise.
    /// </summary>
    public ProviderIdentifier Identifier { get; }

    /// <summary>
    /// Gets the ordering the series was matched under, or null where the key is the episode's
    /// own identifier and no position is involved.
    /// </summary>
    public string? Ordering { get; }

    /// <summary>
    /// Gets the season number, or null where the key is the episode's own identifier.
    /// </summary>
    public int? SeasonNumber { get; }

    /// <summary>
    /// Gets the episode number, or null where the key is the episode's own identifier.
    /// </summary>
    public int? EpisodeNumber { get; }

    /// <summary>
    /// Gets a value indicating whether the key is the episode's own identifier rather than a
    /// position inside a series.
    /// </summary>
    public bool IsTheEpisodesOwnIdentifier => Ordering is null;

    /// <summary>
    /// Derives the key, or says why the episode has none.
    ///
    /// The order the refusals are reached in is deliberate. A file covering several episodes
    /// is refused before anything else, including before the episode's own identifier is
    /// looked at, because such a file holds the state of a run and an identifier naming the
    /// first of them would move the whole run's state onto one episode.
    ///
    /// After that the episode's own identifier is preferred, and a malformed one does not
    /// refuse the item: it falls through to the derived key, which is the ordinary path for an
    /// episode rather than a weaker second attempt. Where the derived key cannot be built
    /// either, the reason reported is the derived path's own, because that is the path an
    /// operator repairs.
    /// </summary>
    /// <param name="episodeProviderIdentifiers">
    /// The identifiers the episode itself carries, keyed by provider name as the server holds
    /// them.
    /// </param>
    /// <param name="seriesProviderIdentifiers">
    /// The identifiers the episode's series carries, in the same shape.
    /// </param>
    /// <param name="seriesDisplayOrder">
    /// The display order the series stores, or null where it stores none.
    /// </param>
    /// <param name="seasonNumber">The season number the episode carries, or null.</param>
    /// <param name="episodeNumber">The episode number the episode carries, or null.</param>
    /// <param name="lastEpisodeNumberInTheItem">
    /// The last episode number the item covers, where the item is one file covering a run of
    /// them, and null where it covers one episode.
    /// </param>
    /// <returns>The key, or the reason there is none.</returns>
    public static EpisodeMatchKeyReading Derive(
        IReadOnlyDictionary<string, string>? episodeProviderIdentifiers,
        IReadOnlyDictionary<string, string>? seriesProviderIdentifiers,
        string? seriesDisplayOrder,
        int? seasonNumber,
        int? episodeNumber,
        int? lastEpisodeNumberInTheItem)
    {
        if (lastEpisodeNumberInTheItem.HasValue && lastEpisodeNumberInTheItem != episodeNumber)
        {
            return EpisodeMatchKeyReading.Unkeyed(MatchKeyRefusal.SpansSeveralEpisodes);
        }

        var own = PreferredIdentifier.Of(episodeProviderIdentifiers);

        if (own.IsKeyed)
        {
            return EpisodeMatchKeyReading.Keyed(new EpisodeMatchKey(own.Key!, null, null, null));
        }

        var series = PreferredIdentifier.Of(seriesProviderIdentifiers);

        if (!series.IsKeyed)
        {
            return EpisodeMatchKeyReading.Unkeyed(series.Refusal);
        }

        if (!seasonNumber.HasValue)
        {
            return EpisodeMatchKeyReading.Unkeyed(MatchKeyRefusal.NoSeasonNumber);
        }

        if (!episodeNumber.HasValue)
        {
            return EpisodeMatchKeyReading.Unkeyed(MatchKeyRefusal.NoEpisodeNumber);
        }

        if (seasonNumber < 0 || episodeNumber < 0)
        {
            return EpisodeMatchKeyReading.Unkeyed(MatchKeyRefusal.NumberingBelowZero);
        }

        return EpisodeMatchKeyReading.Keyed(new EpisodeMatchKey(
            series.Key!,
            SeriesOrdering.NormalForm(seriesDisplayOrder),
            seasonNumber,
            episodeNumber));
    }

    /// <inheritdoc />
    public bool Equals(EpisodeMatchKey? other) =>
        other is not null
        && other.Identifier.Equals(Identifier)
        && string.Equals(other.Ordering, Ordering, StringComparison.Ordinal)
        && other.SeasonNumber == SeasonNumber
        && other.EpisodeNumber == EpisodeNumber;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EpisodeMatchKey);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Identifier, Ordering, SeasonNumber, EpisodeNumber);

    /// <summary>
    /// The key as one string, for a store, a log or an envelope to carry.
    ///
    /// The numbers are written with a separator each rather than padded to a fixed width. The
    /// server's own user data key pads both to three digits and concatenates them, which
    /// cannot tell season 1 episode 1000 from season 11 episode 0 once either number outgrows
    /// the padding. Two servers each holding one of those would agree on a key for two
    /// different episodes, which is the failure this whole document refuses.
    /// </summary>
    /// <returns>The kind, the identifier, and the position where there is one.</returns>
    public override string ToString() =>
        IsTheEpisodesOwnIdentifier
            ? string.Create(CultureInfo.InvariantCulture, $"{Kind}/{Identifier}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{Kind}/{Identifier}/{Ordering}/s{SeasonNumber}e{EpisodeNumber}");
}

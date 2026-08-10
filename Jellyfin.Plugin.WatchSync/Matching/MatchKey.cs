using System;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// A derived key in the one form the index compares, whichever derivation produced it.
///
/// The index answers one question, which local item carries this key, and it has to answer
/// it for a film and for an episode without holding two maps. So the two key types are
/// brought together here, and the kind is part of the value rather than a prefix inside a
/// string.
///
/// Each kind keeps its own key and is compared by that key's own equality, so the two
/// derivations do not share a space of values at all. That matters because a scraper that
/// wrote a series' identifier onto an episode leaves the episode carrying an identifier a
/// film can carry too, and one space would make those one key: either a false ambiguity, or
/// one person's watch state written onto the wrong work. This is refused by construction
/// rather than by a check, so there is nothing here for a test to drive: a film's key and
/// an episode's key cannot be assembled into each other.
///
/// There is no public constructor. A value is held only by having called
/// <see cref="Of(ProviderIdentifier)"/> or <see cref="Of(EpisodeMatchKey)"/>, so a key in
/// the index is one some derivation produced and never a string a caller put together.
/// </summary>
public sealed class MatchKey : IEquatable<MatchKey>
{
    private readonly ProviderIdentifier? _film;

    private readonly EpisodeMatchKey? _episode;

    private MatchKey(MatchKeyKind kind, ProviderIdentifier? film, EpisodeMatchKey? episode)
    {
        Kind = kind;
        _film = film;
        _episode = episode;
    }

    /// <summary>
    /// Gets the derivation that produced the key.
    /// </summary>
    public MatchKeyKind Kind { get; }

    /// <summary>
    /// Gets the key in the form a record, a diagnostic or an envelope writes it.
    ///
    /// It is what the key looks like written down and never what two keys are compared by.
    /// Equality is on the kind and on the key itself, so a change to how either derivation
    /// spells itself out cannot bring two kinds of key together.
    /// </summary>
    public string Value => Kind == MatchKeyKind.Movie ? _film!.ToString() : _episode!.ToString();

    /// <summary>
    /// The key of a film.
    /// </summary>
    /// <param name="identifier">The identifier <see cref="MovieMatchKey"/> derived.</param>
    /// <returns>The key.</returns>
    public static MatchKey Of(ProviderIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return new MatchKey(MatchKeyKind.Movie, identifier, null);
    }

    /// <summary>
    /// The key of an episode.
    /// </summary>
    /// <param name="key">The key <see cref="EpisodeMatchKey"/> derived.</param>
    /// <returns>The key.</returns>
    public static MatchKey Of(EpisodeMatchKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new MatchKey(MatchKeyKind.Episode, null, key);
    }

    /// <inheritdoc />
    public bool Equals(MatchKey? other)
    {
        if (other is null || other.Kind != Kind)
        {
            return false;
        }

        return Kind == MatchKeyKind.Movie
            ? _film!.Equals(other._film!)
            : _episode!.Equals(other._episode!);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as MatchKey);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Kind,
            Kind == MatchKeyKind.Movie ? _film!.GetHashCode() : _episode!.GetHashCode());
}

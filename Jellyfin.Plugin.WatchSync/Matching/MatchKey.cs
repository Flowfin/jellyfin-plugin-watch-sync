using System;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// A derived key in the one form the index compares, whichever derivation produced it.
///
/// The index answers one question, which local item carries this key, and it has to answer
/// it for a film and for an episode without holding two maps. So the two key types are
/// brought to one comparable value here, and the kind is part of that value rather than a
/// prefix inside a string somebody could later change.
///
/// There is no public constructor. A value is held only by having called
/// <see cref="Of(ProviderIdentifier)"/> or <see cref="Of(EpisodeMatchKey)"/>, so a key in
/// the index is a key some derivation produced and never a string a caller assembled.
/// </summary>
public sealed class MatchKey : IEquatable<MatchKey>
{
    private MatchKey(MatchKeyKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Gets the derivation that produced the key.
    /// </summary>
    public MatchKeyKind Kind { get; }

    /// <summary>
    /// Gets the key as the derivation writes it.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The key of a film.
    /// </summary>
    /// <param name="identifier">The identifier <see cref="MovieMatchKey"/> derived.</param>
    /// <returns>The key.</returns>
    public static MatchKey Of(ProviderIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return new MatchKey(MatchKeyKind.Movie, identifier.ToString());
    }

    /// <summary>
    /// The key of an episode.
    /// </summary>
    /// <param name="key">The key <see cref="EpisodeMatchKey"/> derived.</param>
    /// <returns>The key.</returns>
    public static MatchKey Of(EpisodeMatchKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new MatchKey(MatchKeyKind.Episode, key.ToString());
    }

    /// <inheritdoc />
    public bool Equals(MatchKey? other) =>
        other is not null
        && other.Kind == Kind
        && string.Equals(other.Value, Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as MatchKey);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(Value));
}

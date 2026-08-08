using System;
using System.Globalization;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// One provider identifier in the one spelling this plugin compares.
///
/// There is no public constructor. The only way to hold one of these is to have called
/// <see cref="Normalise"/>, so a value that reaches a comparison is a value that was
/// normalised, by construction rather than by anybody remembering to.
/// </summary>
public sealed class ProviderIdentifier : IEquatable<ProviderIdentifier>
{
    private ProviderIdentifier(IdentifierProvider provider, string value)
    {
        Provider = provider;
        Value = value;
    }

    /// <summary>
    /// Gets the provider the identifier was stored under.
    /// </summary>
    public IdentifierProvider Provider { get; }

    /// <summary>
    /// Gets the identifier in its normal form.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Turns a value as some scraper stored it into the one spelling this plugin compares,
    /// or says why it cannot be compared at all.
    ///
    /// The two servers scraped at different times with different scrapers, so the same
    /// identifier arrives spelled several ways, and an unnormalised comparison turns that
    /// into an unmatched item. The opposite mistake is worse, and the shape tests are what
    /// hold it off: a value that is not this provider's shape is refused rather than
    /// stretched until it compares equal to something.
    ///
    /// The normal forms are written in <c>docs/matching.md</c> and a test refuses that
    /// table and this type naming different providers.
    /// </summary>
    /// <param name="provider">The provider the value was stored under.</param>
    /// <param name="stored">The value as stored, or null.</param>
    /// <returns>The identifier, or the reason there is none.</returns>
    public static IdentifierReading Normalise(IdentifierProvider provider, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return IdentifierReading.Refused(IdentifierRefusal.Absent);
        }

        var trimmed = stored.Trim();

        return provider switch
        {
            IdentifierProvider.Imdb => ReadImdb(trimmed),
            IdentifierProvider.Tmdb => ReadNumber(IdentifierProvider.Tmdb, trimmed),
            IdentifierProvider.Tvdb => ReadNumber(IdentifierProvider.Tvdb, trimmed),
            _ => IdentifierReading.Refused(IdentifierRefusal.NotTheProvidersShape),
        };
    }

    /// <inheritdoc />
    public bool Equals(ProviderIdentifier? other) =>
        other is not null
        && other.Provider == Provider
        && string.Equals(other.Value, Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ProviderIdentifier);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Provider, StringComparer.Ordinal.GetHashCode(Value));

    /// <summary>
    /// The identifier with the provider written in front of it. #22 keys on the pair rather
    /// than on the bare value, so a TMDb identifier and a TVDb identifier that happen to be
    /// the same number can never be read as the same work.
    /// </summary>
    /// <returns>The provider and the value.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Provider}:{Value}");

    /// <summary>
    /// IMDb writes <c>tt</c> and a number padded to at least seven digits. The prefix may be
    /// absent or in either case, and the padding may be wrong in either direction, so all of
    /// that is normalised away.
    ///
    /// The seven digit floor is the shape of the identifier and it is also what keeps a
    /// number from another provider out. A TMDb identifier stored under IMDb is a short run
    /// of digits, and without the floor it would be read as a well formed IMDb identifier
    /// for a film nobody meant, which is the normalised by accident match rather than the
    /// missed one.
    /// </summary>
    /// <param name="trimmed">The stored value with surrounding whitespace removed.</param>
    /// <returns>The identifier, or the reason there is none.</returns>
    private static IdentifierReading ReadImdb(string trimmed)
    {
        var digits = trimmed.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed;

        if (!IsDigits(digits))
        {
            return IdentifierReading.Refused(IdentifierRefusal.NotTheProvidersShape);
        }

        if (digits.Length < 7)
        {
            return IdentifierReading.Refused(IdentifierRefusal.TooFewDigitsForAnImdbIdentifier);
        }

        var significant = digits.TrimStart('0');

        if (significant.Length == 0)
        {
            return IdentifierReading.Refused(IdentifierRefusal.Zero);
        }

        return IdentifierReading.Read(new ProviderIdentifier(
            IdentifierProvider.Imdb,
            "tt" + significant.PadLeft(7, '0')));
    }

    /// <summary>
    /// TMDb and TVDb write a plain number. Leading zeros are not part of it, so they come
    /// off, and anything that is not a digit means the field holds something other than an
    /// identifier.
    /// </summary>
    /// <param name="provider">The provider the value was stored under.</param>
    /// <param name="trimmed">The stored value with surrounding whitespace removed.</param>
    /// <returns>The identifier, or the reason there is none.</returns>
    private static IdentifierReading ReadNumber(IdentifierProvider provider, string trimmed)
    {
        if (!IsDigits(trimmed))
        {
            return IdentifierReading.Refused(IdentifierRefusal.NotTheProvidersShape);
        }

        var significant = trimmed.TrimStart('0');

        if (significant.Length == 0)
        {
            return IdentifierReading.Refused(IdentifierRefusal.Zero);
        }

        return IdentifierReading.Read(new ProviderIdentifier(provider, significant));
    }

    /// <summary>
    /// A run of ASCII digits and nothing else. Digits outside ASCII are refused rather than
    /// accepted, because two spellings of a number that a reader cannot tell apart is the
    /// shape of a comparison nobody can review.
    /// </summary>
    /// <param name="value">The candidate.</param>
    /// <returns>Whether every character is an ASCII digit and there is at least one.</returns>
    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}

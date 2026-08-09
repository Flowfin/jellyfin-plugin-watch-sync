namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Why a stored value could not be turned into an identifier.
///
/// Every refusal is a distinct value because an operator can act on some of them and not
/// on others. A single unusable flag collapses a scraper that wrote a URL into the field
/// together with an item that was never scraped at all, and those two need different
/// answers from whoever reads the unmatched record.
/// </summary>
public enum IdentifierRefusal
{
    /// <summary>
    /// Nothing was refused.
    /// </summary>
    None,

    /// <summary>
    /// The value was absent, empty, or only whitespace.
    /// </summary>
    Absent,

    /// <summary>
    /// The value is not the shape this provider's identifiers have. A URL where an
    /// identifier was expected lands here, and so does one provider's identifier stored
    /// under another provider's name.
    /// </summary>
    NotTheProvidersShape,

    /// <summary>
    /// The value is digits and there are fewer of them than any IMDb identifier has. IMDb
    /// pads to seven, so a shorter run is a number that came from somewhere else.
    /// </summary>
    TooFewDigitsForAnImdbIdentifier,

    /// <summary>
    /// The value is a number and that number is zero. No provider allocates it, so it is a
    /// placeholder a scraper wrote rather than an identifier.
    /// </summary>
    Zero,
}

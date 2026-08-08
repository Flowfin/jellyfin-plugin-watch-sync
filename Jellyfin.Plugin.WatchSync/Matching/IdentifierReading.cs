namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// What <see cref="ProviderIdentifier.Normalise"/> answered: an identifier, or the reason
/// there is none.
///
/// The reason is carried rather than dropped. #26 records an unmatched item with the reason
/// it was not matched, and a bare null cannot supply one, so the caller would have to guess
/// or record nothing.
/// </summary>
public sealed class IdentifierReading
{
    private IdentifierReading(ProviderIdentifier? identifier, IdentifierRefusal refusal)
    {
        Identifier = identifier;
        Refusal = refusal;
    }

    /// <summary>
    /// Gets the identifier, or null where the value was refused.
    /// </summary>
    public ProviderIdentifier? Identifier { get; }

    /// <summary>
    /// Gets the reason the value was refused, or <see cref="IdentifierRefusal.None"/>.
    /// </summary>
    public IdentifierRefusal Refusal { get; }

    /// <summary>
    /// Gets a value indicating whether there is an identifier to compare.
    /// </summary>
    public bool IsUsable => Refusal == IdentifierRefusal.None;

    /// <summary>
    /// A reading that produced an identifier.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns>The reading.</returns>
    internal static IdentifierReading Read(ProviderIdentifier identifier) =>
        new IdentifierReading(identifier, IdentifierRefusal.None);

    /// <summary>
    /// A reading that produced no identifier, with the reason.
    /// </summary>
    /// <param name="refusal">Why the value cannot be compared.</param>
    /// <returns>The reading.</returns>
    internal static IdentifierReading Refused(IdentifierRefusal refusal) =>
        new IdentifierReading(null, refusal);
}

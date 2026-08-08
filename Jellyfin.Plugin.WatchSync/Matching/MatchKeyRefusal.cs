namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Why an item produced no match key.
///
/// No key is a normal outcome rather than an error, and it is a terminal one: there is no
/// second pass at a weaker comparison. What differs between these is what an operator can
/// do about it, which is why they are distinct values rather than one flag. #26 records
/// them against the item.
/// </summary>
public enum MatchKeyRefusal
{
    /// <summary>
    /// Nothing was refused.
    /// </summary>
    None,

    /// <summary>
    /// The item carries no provider identifier at all. A home video is the ordinary case,
    /// and nothing an operator does to this repository changes it.
    /// </summary>
    NoIdentifierAtAll,

    /// <summary>
    /// The item carries identifiers and none of them is from a provider the key is derived
    /// from. The work was scraped, by a source this plugin does not key on.
    /// </summary>
    NoIdentifierFromAPreferredProvider,

    /// <summary>
    /// The item carries an identifier from a preferred provider and every one of them was
    /// refused by its normal form. This is a metadata defect on the item, usually a URL or
    /// one provider's number written under another provider's name, and it is the one an
    /// operator can actually repair.
    /// </summary>
    EveryPreferredIdentifierWasRefused,
}

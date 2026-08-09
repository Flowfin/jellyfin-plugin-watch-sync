namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Why an item produced no match key.
///
/// No key is a normal outcome rather than an error, and it is a terminal one: there is no
/// second pass at a weaker comparison. What differs between these is what an operator can
/// do about it, which is why they are distinct values rather than one flag. #26 records
/// them against the item.
///
/// One vocabulary covers every kind that carries a key rule. For an episode the three
/// identifier refusals are about its series rather than about the episode, because an
/// episode's own identifier is a preference and its absence is the ordinary case, while a
/// series without a usable identifier leaves nothing for the derived key to be built on.
/// The four numbering refusals below can only be reached by an episode.
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

    /// <summary>
    /// The episode carries no season number, so it has no position in the series' ordering
    /// to be keyed on. An episode the server could not place is the usual cause, and the
    /// repair is on the item's metadata.
    /// </summary>
    NoSeasonNumber,

    /// <summary>
    /// The episode carries no episode number, for the same reason and with the same repair.
    /// It is a separate value because an item numbered into a season but not inside it is a
    /// different metadata defect from one that is in no season at all.
    /// </summary>
    NoEpisodeNumber,

    /// <summary>
    /// The item is one file covering several episodes, so it holds several positions and no
    /// single one of them is the key. Keying it on the first number would move the whole
    /// file's watch state onto one episode of the run, which is the mass marking this plugin
    /// refuses everywhere else. An item whose last number is present while its first is
    /// absent lands here too: it covers a run whose start is unknown.
    /// </summary>
    SpansSeveralEpisodes,

    /// <summary>
    /// A season or episode number below zero. Zero itself is not refused, because the server
    /// numbers a specials season zero and a scraper numbers a special inside it from zero, so
    /// zero is a position rather than a placeholder. Below zero is neither, and it is a
    /// metadata defect rather than an ordering this plugin does not know.
    /// </summary>
    NumberingBelowZero,
}

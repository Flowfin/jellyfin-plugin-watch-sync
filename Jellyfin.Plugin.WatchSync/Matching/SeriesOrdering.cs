namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// The ordering a series was matched under, in the one spelling a key compares.
///
/// A season and an episode number mean nothing on their own. They are positions inside an
/// ordering, and the same series can be held under a different one on each of the two
/// servers: episode 1 of season 2 in airdate order is a different work from episode 1 of
/// season 2 in DVD order. The ordering therefore travels inside the key, so a key derived
/// under one ordering never equals a key derived under another and the two sides record an
/// unmatched item instead of writing one person's watch state onto the wrong episode.
///
/// The server stores it as free text and documents three options, airdate, dvd and
/// absolute, and it stores nothing at all for the default. So an absent value is folded onto
/// airdate: unset and airdate are the same ordering, and a server that spells it out would
/// otherwise never match one that left it empty.
///
/// Anything else is carried through rather than refused. An ordering this plugin does not
/// recognise is not a guess about the work, and two servers holding the same unrecognised
/// value still agree; refusing it would stop a whole series from matching for a string
/// neither side is wrong about. What it costs is named: a line that renames an ordering
/// leaves a server storing the new spelling unable to match one storing the old, which is a
/// missed match rather than a wrong one.
/// </summary>
public static class SeriesOrdering
{
    /// <summary>
    /// The ordering an episode is in when the series says nothing. It is the server's own
    /// default and the one the overwhelming majority of libraries are in.
    /// </summary>
    public const string Airdate = "airdate";

    /// <summary>
    /// Brings the value the series stored to the one spelling a key compares.
    ///
    /// Case is folded and surrounding whitespace comes off, because the field is written by
    /// scrapers, by imports and by an operator typing into a metadata editor, and two
    /// spellings of one ordering would otherwise be two orderings.
    /// </summary>
    /// <param name="stored">The display order as the series stores it, or null.</param>
    /// <returns>The ordering in its normal form.</returns>
    public static string NormalForm(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? Airdate : stored.Trim().ToLowerInvariant();
}

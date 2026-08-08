namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// The providers this plugin derives a match key from, in the order
/// <c>docs/matching.md</c> prefers them.
///
/// The set is the one that document fixes and not the server's whole provider
/// enumeration, which is not the same on both supported lines, so a type naming all of
/// it would be wrong on one of them.
/// </summary>
public enum IdentifierProvider
{
    /// <summary>
    /// IMDb. Its identifiers carry a <c>tt</c> prefix and a zero padded number.
    /// </summary>
    Imdb,

    /// <summary>
    /// The Movie Database. Its identifiers are a plain number.
    /// </summary>
    Tmdb,

    /// <summary>
    /// TheTVDB. Its identifiers are a plain number.
    /// </summary>
    Tvdb,
}

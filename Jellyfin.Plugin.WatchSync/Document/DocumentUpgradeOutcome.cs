namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// What carrying a document forward came to.
///
/// The two are kept apart because #71 asks that an upgrade not be run twice on one document, and
/// a mechanism that answered both cases identically would leave a caller unable to tell a
/// document it just carried from one it never had to.
/// </summary>
public enum DocumentUpgradeOutcome
{
    /// <summary>
    /// The document already carried the version this code writes, so no step ran.
    /// </summary>
    AlreadyCurrent,

    /// <summary>
    /// The document was older and was carried up the ladder one version at a time.
    /// </summary>
    CarriedForward,
}

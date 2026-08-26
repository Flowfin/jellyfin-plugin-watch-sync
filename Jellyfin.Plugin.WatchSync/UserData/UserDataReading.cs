using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.UserData;

/// <summary>
/// What this plugin asked the server about one mapped user and one leaf item, which is the
/// answer <see cref="IUserDataGateway"/> gives to a read.
///
/// Two things, because the caller needs both and the two come from different places on the
/// two supported lines. The state is the moved set as this server holds it, or nothing where
/// the server holds no record for that user and item at all. The runtime is the length of the
/// version this server would resume, which is what a position from a peer is measured against
/// in <c>Jellyfin.Plugin.WatchSync.Versions.VersionLanding</c>.
///
/// Nothing held and an unwatched state are carried apart rather than flattened into one. Both
/// look the same on a dashboard, and only one of them is a value somebody chose: an item the
/// server has no record for has never been touched by that person, and an item recorded as
/// unplayed may have been marked so by hand, which is the intent #34 turns on.
///
/// The runtime is carried here rather than left to a caller to fetch, because fetching it is
/// exactly the question the two lines answer differently: one names the version that drives
/// the resume point and the other has no version to name. A caller that read a runtime for
/// itself would be reading the item's own on both lines and would be right on one of them.
/// </summary>
public sealed class UserDataReading
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataReading"/> class.
    /// </summary>
    /// <param name="state">
    /// The moved set as this server holds it, or null where it holds no record at all.
    /// </param>
    /// <param name="resumeRuntimeTicks">
    /// The runtime of the version this server would resume, or null where neither that version
    /// nor the item carries one, which is what an item the server has not analysed yet looks
    /// like.
    /// </param>
    public UserDataReading(SyncedState? state, long? resumeRuntimeTicks)
    {
        State = state;
        ResumeRuntimeTicks = resumeRuntimeTicks;
    }

    /// <summary>
    /// Gets the moved set as this server holds it, or null where it holds no record.
    /// </summary>
    public SyncedState? State { get; }

    /// <summary>
    /// Gets the runtime of the version this server would resume, or null where there is none.
    ///
    /// Null is not an error and is not a defect in the peer. An item nothing has analysed yet
    /// carries no runtime, and the rule that reads this number drops a position rather than
    /// applying one on the strength of a number that is not there.
    /// </summary>
    public long? ResumeRuntimeTicks { get; }
}

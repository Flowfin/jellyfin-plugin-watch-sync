using System;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// The state this plugin carries between two servers, for one mapped user and one leaf item.
///
/// One member per moved field and nothing else. The server's own per-user record holds ten
/// properties and six of them never leave the server they are on, so a type of this plugin's
/// own is what makes the refusal structural: a field that is not a member here has no way to
/// reach an envelope, whatever a later caller intends.
///
/// It is deliberately not the server's <c>UserItemData</c>. Using that record as the wire type
/// would carry every field the server adds to it into the transfer on the day the server adds
/// it, which is the opposite of a decision, and it would tie the shape of what two servers
/// exchange to a type whose owner has no reason to keep it stable. <c>docs/sync-model.md</c>
/// holds the disposition of each of the ten, and <c>SyncModelDocumentTests</c> refuses that
/// table and this type disagreeing.
/// </summary>
public sealed class SyncedState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncedState"/> class.
    /// </summary>
    /// <param name="played">Whether the person watched the work.</param>
    /// <param name="playCount">How often the person watched the work.</param>
    /// <param name="playbackPositionTicks">Where the person stopped, in ticks.</param>
    /// <param name="lastPlayedDate">When the person last watched the work, or null.</param>
    public SyncedState(
        bool played,
        int playCount,
        long playbackPositionTicks,
        DateTime? lastPlayedDate)
    {
        Played = played;
        PlayCount = playCount;
        PlaybackPositionTicks = playbackPositionTicks;
        LastPlayedDate = lastPlayedDate;
    }

    /// <summary>
    /// Gets a value indicating whether the person watched the work.
    ///
    /// #31 refuses regressing it to a partial position.
    /// </summary>
    public bool Played { get; }

    /// <summary>
    /// Gets how often the person watched the work.
    ///
    /// #33 reconciles it against the agreed record so that a sync never invents a play.
    /// </summary>
    public int PlayCount { get; }

    /// <summary>
    /// Gets where the person stopped, in ticks.
    ///
    /// The thresholds in #17 bound how many changes one playback produces, so this is not a
    /// field every progress report ships.
    /// </summary>
    public long PlaybackPositionTicks { get; }

    /// <summary>
    /// Gets when the person last watched the work, or null where they never did.
    ///
    /// It is the field that lets a disagreement about position be settled by recency rather
    /// than by whichever server spoke last, bounded by the tolerated clock skew in #32. The
    /// type is the server's own, a <see cref="DateTime"/> the server stores in UTC, because
    /// converting on the way in and out of this type would add a second place where an
    /// offset can be lost.
    /// </summary>
    public DateTime? LastPlayedDate { get; }
}

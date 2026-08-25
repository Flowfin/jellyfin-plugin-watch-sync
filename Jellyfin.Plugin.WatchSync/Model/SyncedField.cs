namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// One member per field this plugin moves, so that a rule about a field can name it.
///
/// <see cref="SyncedState"/> is a reading of all four at one moment and cannot say which of
/// them a change is about. A change is about one field, because the list a peer reads holds
/// at most one entry per pairing, mapped user, item and field, and a list keyed on a reading
/// rather than on a field would hold an entry per moment instead.
///
/// It is a second list of the moved set and the drift is what makes it dangerous, so nothing
/// keeps the two in step by hand: <c>ChangeCollapseTests</c> reads the properties of
/// <see cref="SyncedState"/> by reflection and refuses this enumeration disagreeing with them
/// in either direction. A field added to the moved set with no member here is a field no
/// change can be about, and a member here that is not a moved field is an entry nothing can
/// ever fill.
///
/// The names are the property names of <see cref="SyncedState"/> rather than shorter ones,
/// because the comparison that holds the two together is over names and a friendlier spelling
/// would need a translation table, which is the drift again one level in.
///
/// <c>docs/sync-model.md</c> fixes which fields move and why the other six do not. This type
/// points at that document rather than restating it.
/// </summary>
public enum SyncedField
{
    /// <summary>
    /// Whether the person watched the work.
    /// </summary>
    Played,

    /// <summary>
    /// How often the person watched the work.
    /// </summary>
    PlayCount,

    /// <summary>
    /// Where the person stopped, in ticks.
    /// </summary>
    PlaybackPositionTicks,

    /// <summary>
    /// When the person last watched the work.
    /// </summary>
    LastPlayedDate,
}

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// What the status found when it went to the record one of its numbers is read from.
///
/// Three states and not two, because a record that could not be read and a record that is not
/// there are different facts on a status surface. Both would show as zero if the surface
/// counted for itself, and zero reads as fine. #62's rule is that every number is read from the
/// record the code uses, so the surface answers what the read came back with, and a page shows
/// an unreadable record as something to look at rather than as nothing to see.
/// </summary>
public enum RecordReading
{
    /// <summary>
    /// The store holds no document of this kind for the pairing and the person. A pairing that
    /// has never exchanged and a person nothing has been recorded about both answer this.
    /// </summary>
    Absent,

    /// <summary>
    /// The document was read and the numbers beside this state are its own.
    /// </summary>
    Read,

    /// <summary>
    /// The store holds a document under this name and it could not be read as this kind of
    /// record, or it was written by a version of this plugin this code does not know. The
    /// numbers beside this state say nothing.
    /// </summary>
    Unreadable,
}

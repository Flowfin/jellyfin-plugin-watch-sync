namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What a document in this plugin's store turned out to be when it was read as the agreed
/// record of one pairing and one mapped user.
/// </summary>
public enum AgreedRecordsAnswer
{
    /// <summary>
    /// The document is an agreed record and every entry in it was read.
    /// </summary>
    Readable,

    /// <summary>
    /// The document is not an agreed record, or one of its entries is not an agreement.
    ///
    /// One answer rather than one per way, because every way has the same repair. A store this
    /// plugin wrote does not produce any of them, so what is on disk was written by something
    /// else or damaged, and the record is rebuilt by a full reconciliation rather than read.
    /// Reading the entries that happen to parse would be worse than refusing: an agreement
    /// missing from the record is a first exchange for that item, so a partial read is a record
    /// that quietly says two servers agreed less than they did.
    /// </summary>
    NotAnAgreedRecord,
}

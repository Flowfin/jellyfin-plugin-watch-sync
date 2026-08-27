namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What a document in this plugin's store turned out to be when it was read as the items that did
/// not match under one pairing for one mapped user.
/// </summary>
public enum UnmatchedRecordsAnswer
{
    /// <summary>
    /// The document is a record of unmatched items and every entry in it was read.
    /// </summary>
    Readable,

    /// <summary>
    /// The document is not a record of unmatched items, or one of its entries is not one.
    ///
    /// One answer rather than one per way, for the reason
    /// <see cref="Jellyfin.Plugin.WatchSync.Agreement.AgreedRecordsAnswer.NotAnAgreedRecord"/>
    /// gives: every way has the same repair, and a store this plugin wrote produces none of them.
    /// The repair here is the cheapest of the three kinds. This record is rebuilt by the next
    /// pass over the library, because it is a reading of what the matcher answers rather than an
    /// account of a decision or a copy of somebody's values, so a refused document costs a count
    /// until that pass and nothing after it.
    ///
    /// It is still refused whole rather than read in part. An operator uses this record to find
    /// out how much of their library is not syncing, and a list that is quietly short answers
    /// that question with a smaller number, which reads as the thing improving.
    /// </summary>
    NotARecordOfUnmatchedItems,
}

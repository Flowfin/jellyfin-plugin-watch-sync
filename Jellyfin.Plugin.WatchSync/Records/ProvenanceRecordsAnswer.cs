namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What a document in this plugin's store turned out to be when it was read as the provenance of
/// what this plugin wrote under one pairing for one mapped user.
/// </summary>
public enum ProvenanceRecordsAnswer
{
    /// <summary>
    /// The document is a record of provenance and every entry in it was read.
    /// </summary>
    Readable,

    /// <summary>
    /// The document is not a record of provenance, or one of its entries is not a write.
    ///
    /// One answer rather than one per way, for the reason
    /// <see cref="Jellyfin.Plugin.WatchSync.Agreement.AgreedRecordsAnswer.NotAnAgreedRecord"/>
    /// gives: every way has the same repair, and a store this plugin wrote produces none of them.
    /// What that repair is is the harder half here and it is harder than it is for a conflict. An
    /// agreed record is rebuilt by a full reconciliation and a record of conflicts cannot be
    /// rebuilt but only diagnoses; this one cannot be rebuilt and something depends on it. A
    /// refusal here means the writes this document covered can no longer be undone, so a
    /// revocation afterwards deletes what it can find and leaves the rest, and nothing else in
    /// this plugin knows what the rest was.
    ///
    /// It is still the right direction. Reading the entries that happen to parse would hand a
    /// revocation a list of writes to undo that looks complete and is not, and an undo driven by
    /// half a record restores half a person's values and reports that it finished.
    /// </summary>
    NotARecordOfProvenance,
}

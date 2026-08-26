namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What a document in this plugin's store turned out to be when it was read as the conflicts
/// recorded under one pairing for one mapped user.
/// </summary>
public enum ConflictRecordsAnswer
{
    /// <summary>
    /// The document is a record of conflicts and every entry in it was read.
    /// </summary>
    Readable,

    /// <summary>
    /// The document is not a record of conflicts, or one of its entries is not a conflict.
    ///
    /// One answer rather than one per way, for the reason
    /// <see cref="Jellyfin.Plugin.WatchSync.Agreement.AgreedRecordsAnswer.NotAnAgreedRecord"/>
    /// gives: every way has the same repair, and a store this plugin wrote produces none of
    /// them. What that repair is differs here and is the harder half. An agreed record is
    /// rebuilt by a full reconciliation, and this one cannot be rebuilt by anything, because it
    /// is an account of decisions that have already been taken. So a refusal here loses an
    /// operator's answer to why an episode is marked watched, and it is still the right answer:
    /// reading the entries that happen to parse would hand that operator a list which looks
    /// complete and is not, and a diagnostic nobody can trust is worse than one that says it is
    /// unreadable.
    /// </summary>
    NotARecordOfConflicts,
}

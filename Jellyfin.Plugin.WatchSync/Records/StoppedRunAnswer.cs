namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What a document in this plugin's store turned out to be when it was read as the record of a
/// run the cap stopped.
/// </summary>
public enum StoppedRunAnswer
{
    /// <summary>
    /// The document is a stopped run and every item in it was read.
    /// </summary>
    Readable,

    /// <summary>
    /// The document is not a stopped run, or one of its items is not one.
    ///
    /// One answer rather than one per way, for the reason
    /// <see cref="ConflictRecordsAnswer.NotARecordOfConflicts"/> gives: every way has the same
    /// repair, and a store this plugin wrote produces none of them. What a refusal costs here is
    /// the plan, and that is the right cost: an approval applied from a plan that half parsed
    /// would write the items that happened to read and leave an operator believing the rest
    /// were written too.
    /// </summary>
    NotAStoppedRun,
}

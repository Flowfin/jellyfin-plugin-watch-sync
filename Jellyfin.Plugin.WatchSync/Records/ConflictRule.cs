namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// One member per rule the conflict table declares, so that a record of a conflict can name
/// which rule decided it.
///
/// The words are the document's own. <c>docs/conflicts.md</c> declares the rule column as a
/// closed set and argues each member in the prose above the table, and
/// <c>ConflictRecordTests</c> refuses this enumeration and that declaration disagreeing in
/// either direction. A member here that the document does not declare is a rule argued
/// nowhere, and a rule declared there with no member here is one no record can name, so a
/// conflict decided by it would be recorded as something else or not at all.
///
/// It is deliberately not one member per answer. The three rules on the mainline answer in
/// enumerations of their own and there are nine of those answers between them, which is a
/// finer grain than the table has and a grain nothing holds to the document. What a record
/// names is the row, because the row is what an operator asking why an episode is marked
/// watched can be shown.
/// </summary>
public enum ConflictRule
{
    /// <summary>
    /// One state is stronger than the other and wins whatever the two clocks say.
    /// </summary>
    Ratchet,

    /// <summary>
    /// The answer is computed from what the two sides last agreed rather than chosen between
    /// the two readings.
    /// </summary>
    Reckon,

    /// <summary>
    /// The later reading wins, bounded by the tolerated clock skew, with the tie rule written
    /// down rather than left to whichever side is asked first.
    /// </summary>
    Recency,

    /// <summary>
    /// The answer is the greater of the two values, because the field is a high-water mark of
    /// something that already happened.
    /// </summary>
    Maximum,
}

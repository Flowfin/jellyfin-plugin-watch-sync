namespace Jellyfin.Plugin.WatchSync.Undo;

/// <summary>
/// Why one value this plugin wrote is left standing rather than put back.
///
/// #44's body says the undo is bounded and honest about it, and every member here is one of the
/// bounds. A skip is a normal outcome rather than an error: the record of provenance reaches back
/// as far as its retention and no further, the person owns their own account in the meantime, and
/// a revocation that quietly overwrote either of those would be worse than one that stopped short
/// and said where.
///
/// One member per cause rather than one meaning "could not", because the four have four different
/// answers for an operator. A value the person changed is a value nobody should touch. A record
/// that no longer stands is an item whose history the server has already lost. A write over
/// nothing is this plugin's own residual and the one that says the store cannot express what an
/// undo would need. A value that does not fit its field is a document this plugin did not write in
/// that shape, and it is the one an operator should be told about rather than have smoothed over.
/// </summary>
public enum SkipReason
{
    /// <summary>
    /// The server holds no record for the item at all, so there is nothing standing to correct.
    ///
    /// Putting a value back here would create a record where the person has none, which is a
    /// stronger change than the one the undo was asked for.
    /// </summary>
    NoRecordStandsNow,

    /// <summary>
    /// The value standing now is not the value this plugin wrote, so somebody changed it
    /// afterwards and their action outranks the undo.
    ///
    /// This is #44's third condition. It is decided by comparing what the record stands on now
    /// against <see cref="Records.ProvenanceRecord.Written"/>, which is the member that exists
    /// for this comparison and nothing else.
    /// </summary>
    NotTheValueThisPluginLeft,

    /// <summary>
    /// This server held nothing for the field before the write, and a write assigns every moved
    /// field, so there is no way to say "hold nothing again".
    ///
    /// It is a residual of the write interface rather than of the record: the record carries the
    /// absence faithfully, and <see cref="Model.SyncedField.LastPlayedDate"/> is the one field
    /// whose absence a write can express, so it is the one field this reason never names.
    /// </summary>
    NothingToPutBack,

    /// <summary>
    /// The recorded value is outside what the field can hold, so putting it back would either
    /// throw or silently write a different number.
    ///
    /// A record is read out of bytes and a number parsed out of bytes converts between widths
    /// where one assembled in memory does not: a play count is recorded as the number a document
    /// holds and assigned back as the count the server keeps, and a date is recorded as ticks and
    /// assigned back as a moment. A truncation here would put back a play count nobody had.
    /// </summary>
    ValueDoesNotFitTheField,
}

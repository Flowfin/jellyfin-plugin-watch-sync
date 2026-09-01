namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What offering one agreement to a record came back with, which is #313.
/// </summary>
public enum AgreementAdmissionAnswer
{
    /// <summary>
    /// The agreement is in the record, replacing whatever was agreed about that item.
    /// </summary>
    Agreed,

    /// <summary>
    /// The record already holds <see cref="AgreedRecords.MaximumEntries"/> items and this
    /// agreement is about an item that is not one of them.
    ///
    /// It is a refusal rather than a truncation on purpose, and the difference is the whole
    /// point of the bound. Dropping the oldest entry to make room, which is what
    /// <see cref="Jellyfin.Plugin.WatchSync.Records.ConflictRecords"/> does, unagrees an item
    /// that two servers had settled, and an item with no agreed record is a first exchange
    /// rather than an item nobody has looked at. So a peer that kept offering new items would
    /// silently turn the far end of the library back into a first exchange, one item per
    /// agreement, and a first exchange is the run allowed to change the most.
    /// </summary>
    AtTheBound,
}

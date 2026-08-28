using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// One field of a moved set read as the number a record holds it as.
///
/// <see cref="ProvenanceRecord"/> and <see cref="ConflictRecord"/> both hold a value as ticks or
/// a count rather than as the field's own type, because one record type holds all four rows and
/// what a number means is decided by the field beside it. Both of them state that convention in
/// their own bodies; until this, nothing carried it out, and the first caller that had to convert
/// a reading into one of those numbers would have written the conversion at its own call site.
///
/// It is here rather than at that call site because the conversion is where the two states this
/// plugin has to tell apart are collapsed by accident. A last played date of nothing and a last
/// played date at the first instant a date can hold are different states, and a conversion that
/// answered zero for both would make an undo write a date nobody ever had. So the absence travels
/// as an absence through this type and the caller never invents a sentinel.
/// </summary>
internal static class RecordedValue
{
    /// <summary>
    /// What a record holds for one field of a reading, or <c>null</c> where the reading holds
    /// nothing for it.
    ///
    /// A whole reading that is absent answers nothing for every field rather than answering the
    /// values an unwatched item would carry. A server holding no record for an item and a server
    /// holding a record saying the person never watched it are different states: restoring the
    /// second where the first was true leaves a row on an item the person has never opened.
    /// </summary>
    /// <param name="state">The moved set, or null where this server holds none.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>The number, or null where there is none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The field is not one this type has an arm for. One arm per member and no default that
    /// guesses, which is <c>OutstandingChanges</c>'s rule and is the same failure seen one step
    /// later: a field added to the moved set with no arm here would be read as the same value
    /// before and after every write, so the record would hold nothing about it and an undo would
    /// leave the peer's value standing.
    /// </exception>
    internal static long? Of(SyncedState? state, SyncedField field)
    {
        if (state is null)
        {
            return null;
        }

        return field switch
        {
            SyncedField.Played => state.Played ? 1 : 0,
            SyncedField.PlayCount => state.PlayCount,
            SyncedField.PlaybackPositionTicks => state.PlaybackPositionTicks,
            SyncedField.LastPlayedDate => state.LastPlayedDate?.Ticks,
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "This is a field that moves and no arm here says what a record holds for it, so every write of it would be recorded as a value that did not change and an undo would never see it."),
        };
    }
}

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;

namespace Jellyfin.Plugin.WatchSync.Undo;

/// <summary>
/// What putting back everything one pairing wrote for one mapped user would change, decided
/// against what the server holds now.
///
/// This is #44's third condition and the half of that issue's body that the record alone cannot
/// carry. Decision 5 on the pairing board is the strict answer, that on revocation what came from
/// the peer is deleted, and <see cref="ProvenanceRecords"/> exists so that answer stays available.
/// The record says what was replaced; this says what may be put back, which is a different
/// question, because a person owns their own account in the days between the write and the
/// revocation.
///
/// <para>
/// It decides and does not write. Whether an undo runs at revocation, on request, or not at all
/// follows the decision on the pairing board, which #44's last condition leaves there on purpose,
/// and a type that read the record and called the gateway would have taken that decision by
/// existing. So this answers, the caller writes, and the same answer is what a dry run in #65
/// would show and what a page in #62 would count.
/// </para>
///
/// <para>
/// The walk is the one <see cref="ProvenanceRecords"/> describes: the entries are oldest first,
/// so the last entry for a field is the newest write of it, and that entry's
/// <see cref="ProvenanceRecord.Written"/> is what the record should still be standing on. Reading
/// the oldest entry instead would put back a value the person had already replaced themselves,
/// and reading every entry would put back a chain of values in an order nothing declares.
/// </para>
///
/// <para>
/// It reads no clock and takes no retention. What the record still holds is what
/// <see cref="ProvenanceRecords.Retaining"/> and <see cref="ProvenanceRecords.MaximumEntries"/>
/// have already decided, and an undo asking a second question about age would answer differently
/// from the record it is reading. The consequence is the residual that record already states: a
/// write dropped by either bound is one this can no longer put back, and nothing here can tell
/// an operator that a revocation reached back as far as it could rather than as far as it should.
/// </para>
///
/// <para>
/// Nothing calls this. The revocation that would is #45, the surface an operator would ask from
/// is #62 and #64, and neither exists.
/// </para>
/// </summary>
public static class UndoOfWhatAPairingWrote
{
    /// <summary>
    /// Decides what to put back and what to leave standing.
    /// </summary>
    /// <param name="provenance">
    /// What this plugin wrote under one pairing for one mapped user.
    /// </param>
    /// <param name="held">
    /// What the server holds now for each item the record names, keyed on the item, with a null
    /// value where the server holds no record for that item at all. Nothing and a never-watched
    /// record are different states and this undo answers differently for them, so a caller that
    /// cannot read an item passes the absence rather than a state standing in for it.
    /// </param>
    /// <returns>The items to write, and the values left standing with the reason for each.</returns>
    /// <exception cref="ArgumentNullException">The record or the readings are null.</exception>
    /// <exception cref="ArgumentException">
    /// The readings hold nothing for an item the record names. A caller that read some of the
    /// items and asked anyway would be answered with an undo that silently covered part of the
    /// pairing, and a partial undo reported as a whole one is the shape of claim #44's body
    /// refuses: somebody told what a pairing wrote has been put back has been told something
    /// specific.
    /// </exception>
    public static UndoAnswer Decide(
        ProvenanceRecords provenance,
        IReadOnlyDictionary<Guid, SyncedState?> held)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(held);

        var order = new List<Guid>();
        var newest = new Dictionary<Guid, Dictionary<SyncedField, ProvenanceRecord>>();

        foreach (var write in provenance.All)
        {
            if (!newest.TryGetValue(write.ItemId, out var fields))
            {
                fields = new Dictionary<SyncedField, ProvenanceRecord>();
                newest[write.ItemId] = fields;
                order.Add(write.ItemId);
            }

            // The entries are oldest first, so the assignment that stands at the end of the walk
            // is the newest write of that field.
            fields[write.Field] = write;
        }

        var restore = new List<ItemToRestore>();
        var skipped = new List<SkippedValue>();

        foreach (var itemId in order)
        {
            if (!held.TryGetValue(itemId, out var standing))
            {
                throw new ArgumentException(
                    "The readings hold nothing for an item the record of provenance names, so this undo would cover part of a pairing while reporting the whole of it.",
                    nameof(held));
            }

            PutBack(itemId, newest[itemId], standing, restore, skipped);
        }

        return new UndoAnswer(restore, skipped);
    }

    /// <summary>
    /// Decides one item, adding what it puts back to one list and what it leaves standing to the
    /// other.
    /// </summary>
    /// <param name="itemId">The local item.</param>
    /// <param name="writes">The newest write of each field of that item.</param>
    /// <param name="standing">What the server holds for it now, or null where it holds nothing.</param>
    /// <param name="restore">The items to write, added to where this one has a field to put back.</param>
    /// <param name="skipped">The values left standing, added to per field.</param>
    private static void PutBack(
        Guid itemId,
        Dictionary<SyncedField, ProvenanceRecord> writes,
        SyncedState? standing,
        List<ItemToRestore> restore,
        List<SkippedValue> skipped)
    {
        if (standing is null)
        {
            foreach (var field in Enum.GetValues<SyncedField>())
            {
                if (writes.ContainsKey(field))
                {
                    skipped.Add(new SkippedValue(itemId, field, SkipReason.NoRecordStandsNow));
                }
            }

            return;
        }

        var played = standing.Played;
        var playCount = standing.PlayCount;
        var position = standing.PlaybackPositionTicks;
        var lastPlayed = standing.LastPlayedDate;
        var fields = new List<SyncedField>();

        foreach (var field in Enum.GetValues<SyncedField>())
        {
            if (!writes.TryGetValue(field, out var write))
            {
                continue;
            }

            if (RecordedValue.Of(standing, field) != write.Written)
            {
                skipped.Add(new SkippedValue(itemId, field, SkipReason.NotTheValueThisPluginLeft));

                continue;
            }

            var before = write.Before;

            if (before is null && field != SyncedField.LastPlayedDate)
            {
                skipped.Add(new SkippedValue(itemId, field, SkipReason.NothingToPutBack));

                continue;
            }

            if (!Fits(field, before))
            {
                skipped.Add(new SkippedValue(itemId, field, SkipReason.ValueDoesNotFitTheField));

                continue;
            }

            switch (field)
            {
                case SyncedField.Played:
                    played = before!.Value == 1;
                    break;
                case SyncedField.PlayCount:
                    playCount = (int)before!.Value;
                    break;
                case SyncedField.PlaybackPositionTicks:
                    position = before!.Value;
                    break;
                default:
                    lastPlayed = before is null
                        ? null
                        : new DateTime(before.Value, DateTimeKind.Utc);
                    break;
            }

            fields.Add(field);
        }

        if (fields.Count > 0)
        {
            restore.Add(new ItemToRestore(
                itemId,
                new SyncedState(played, playCount, position, lastPlayed),
                fields));
        }
    }

    /// <summary>
    /// Whether a recorded number is one the field can hold.
    ///
    /// A record is read out of bytes and a number parsed out of bytes converts between widths
    /// where one assembled in memory does not, so a document holding a play count above what the
    /// server's own count can hold would be put back as a different number entirely, and one
    /// holding a date outside the range a moment can take would throw out of a decision. Both are
    /// answered as a skip, because an undo that stopped on one item would abandon the rest of a
    /// revocation for a value it could not have written anyway.
    /// </summary>
    /// <param name="field">The moved field.</param>
    /// <param name="value">The recorded value, or null where the record holds none.</param>
    /// <returns>Whether it can be assigned.</returns>
    private static bool Fits(SyncedField field, long? value)
    {
        if (value is null)
        {
            return true;
        }

        return field switch
        {
            SyncedField.Played => value.Value is 0 or 1,
            SyncedField.PlayCount => value.Value >= int.MinValue && value.Value <= int.MaxValue,
            SyncedField.PlaybackPositionTicks => true,
            _ => value.Value >= DateTime.MinValue.Ticks && value.Value <= DateTime.MaxValue.Ticks,
        };
    }
}

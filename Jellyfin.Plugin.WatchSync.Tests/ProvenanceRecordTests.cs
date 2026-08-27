using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What one record of a write may carry and what it refuses to be, which is the type #44's
/// undo is written against.
///
/// Two refusals are what this set is about. A write attributed to nobody cannot be bounded by a
/// mapping, so a revocation would either revert it on a pairing it did not come in on or leave
/// it behind. And a write that replaced a value with itself is not a write: it gives an undo an
/// entry to act on that never changed anything, and it makes the record longer without making
/// anything recoverable, which matters because the record is capped.
///
/// Beside them sits the rule the record shares with <see cref="ConflictRecord"/> and needs more
/// than it does: this is the one kind in the store that holds copies of what somebody watched,
/// so what it may be made of is a closed set of types rather than a habit.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public class ProvenanceRecordTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _peerUser = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The types a record of a write may be made of. Identifiers, the field, the two values and
    /// the moment. A type outside this set is refused rather than judged, so the guard does not
    /// have to know what a title is called next time.
    ///
    /// Both values are the nullable width and neither is the plain one. A number that has to
    /// exist cannot express the write that clears a person's last played date, and that write is
    /// one of the four this record is for.
    /// </summary>
    private static readonly IReadOnlyList<Type> _permitted = new[]
    {
        typeof(Guid),
        typeof(SyncedField),
        typeof(long?),
        typeof(DateTimeOffset),
    };

    /// <summary>
    /// Every member of the record is one of the permitted types, so a string cannot be added to
    /// it whatever it is named.
    ///
    /// It costs more here than it does on a conflict. A conflict record holds two numbers a
    /// person's record happened to pass through; this one holds, per entry, the value somebody's
    /// record stood at before this plugin touched it, kept for as long as the retention runs. A
    /// title beside that turns the document from an account of what this plugin did into a
    /// readable list of what a person watched and when.
    /// </summary>
    [Fact]
    public void TheRecordCarriesNothingOutsideThePermittedTypes()
    {
        var members = Members();

        Assert.NotEmpty(members);

        Assert.Empty(members
            .Where(member => !_permitted.Contains(member.PropertyType))
            .Select(member =>
                $"{member.Name} is a {member.PropertyType} and a record of a write carries identifiers, the field, the two values and the moment. A title or a path makes the record a viewing history, and text a peer chose reaches a page and a log through it."));
    }

    /// <summary>
    /// A write attributed to no peer user is refused.
    ///
    /// The pairing says which arrangement the value arrived under and the mapping says whose
    /// record was touched. Neither says which account on the other machine it came out of, and
    /// that is the one a mapping removed and made again changes without the pairing moving. An
    /// entry naming nobody is one an undo bounded by a mapping cannot place, so it is refused
    /// where it is made rather than skipped where it is read.
    /// </summary>
    [Fact]
    public void AWriteAttributedToNobodyIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => new ProvenanceRecord(
            _pairing,
            _user,
            Guid.Empty,
            _film,
            SyncedField.Played,
            0,
            1,
            _evening));

        Assert.Equal("peerUserId", refused.ParamName);
    }

    /// <summary>
    /// A write that replaced a value with itself is refused.
    ///
    /// Nothing was replaced, so there is nothing for an undo to put back, and the entry would
    /// still occupy a place under the cap that a write which did change something needs. It is
    /// also the shape a caller produces by recording every field of a state rather than the ones
    /// that moved, which is the mistake this refusal is here to meet.
    /// </summary>
    [Fact]
    public void AWriteThatChangedNothingIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => new ProvenanceRecord(
            _pairing,
            _user,
            _peerUser,
            _film,
            SyncedField.PlayCount,
            3,
            3,
            _evening));

        Assert.Equal("written", refused.ParamName);
    }

    /// <summary>
    /// Nothing and zero are different states, and a write over nothing is recordable.
    ///
    /// A position of zero is somebody who started the work and stopped at the beginning; no
    /// position at all is somebody who never opened it. An undo that restored the first where the
    /// second was true would leave a resume point on an item the person has not touched, so the
    /// record has to be able to hold the difference rather than collapsing it into a number.
    /// </summary>
    [Fact]
    public void AWriteOverNothingIsRecordableAndKeepsTheDifference()
    {
        var record = new ProvenanceRecord(
            _pairing,
            _user,
            _peerUser,
            _film,
            SyncedField.PlaybackPositionTicks,
            null,
            0,
            _evening);

        Assert.Null(record.Before);
        Assert.Equal(0, record.Written);
    }

    /// <summary>
    /// A write that cleared a value is recordable, and it is the write this record could not hold
    /// until the value written took the shape the value replaced already had.
    ///
    /// One of the four moved fields has no number for having no value. A last played date is
    /// nullable in the state that travels and is assigned straight through, so a peer's answer
    /// that clears one is a write that happened and left nothing behind. It is also the value an
    /// undo most needs: a cleared date is exactly what the peer's answer overwrote, and a record
    /// missing it looks complete because every other field of that item is in it.
    /// </summary>
    [Fact]
    public void AWriteThatClearedAValueIsRecordable()
    {
        var record = new ProvenanceRecord(
            _pairing,
            _user,
            _peerUser,
            _film,
            SyncedField.LastPlayedDate,
            _evening.UtcTicks,
            null,
            _evening);

        Assert.Equal(_evening.UtcTicks, record.Before);
        Assert.Null(record.Written);
    }

    /// <summary>
    /// Nothing written where there was nothing is refused, which is the same refusal as a value
    /// written over itself rather than a second rule.
    ///
    /// A field that had no value and still has none is a field this plugin did not change, so
    /// there is nothing for an undo to put back and the entry would occupy a place under the cap
    /// that a write which did change something needs. It is the shape a caller produces by
    /// recording every field of a state rather than the ones that moved, in the one direction
    /// that has no number to compare.
    /// </summary>
    [Fact]
    public void NothingWrittenWhereThereWasNothingIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => new ProvenanceRecord(
            _pairing,
            _user,
            _peerUser,
            _film,
            SyncedField.LastPlayedDate,
            null,
            null,
            _evening));

        Assert.Equal("written", refused.ParamName);
    }

    /// <summary>
    /// What the record carries is what it was handed, which is what an undo reads back.
    /// </summary>
    [Fact]
    public void TheRecordCarriesWhatItWasHanded()
    {
        var record = new ProvenanceRecord(
            _pairing,
            _user,
            _peerUser,
            _film,
            SyncedField.PlayCount,
            1,
            4,
            _evening);

        Assert.Equal(_pairing, record.PairingId);
        Assert.Equal(_user, record.MappedUserId);
        Assert.Equal(_peerUser, record.PeerUserId);
        Assert.Equal(_film, record.ItemId);
        Assert.Equal(SyncedField.PlayCount, record.Field);
        Assert.Equal(1, record.Before);
        Assert.Equal(4, record.Written);
        Assert.Equal(_evening, record.WrittenAt);
    }

    /// <summary>
    /// The public instance properties of the record.
    /// </summary>
    /// <returns>Its members.</returns>
    private static IReadOnlyList<PropertyInfo> Members() =>
        typeof(ProvenanceRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToList();
}

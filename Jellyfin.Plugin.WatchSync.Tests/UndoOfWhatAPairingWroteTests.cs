using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Undo;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What an undo of one pairing's writes puts back and what it leaves standing, which is #44's
/// third condition.
///
/// The failure the set is written against is a revocation that overwrites a person's own action.
/// Decision 5 on the pairing board is the strict answer, and carrying it out means writing over
/// values that have been standing on a server for days or months. Anything the person changed in
/// the meantime is theirs, and an undo that could not tell their change from its own write would
/// take it away in the name of a pairing they never heard of.
///
/// Nothing here reads a clock, calls a server or writes anything. The undo answers and the caller
/// writes, which is why every fact below is an assertion about an answer.
/// </summary>
public class UndoOfWhatAPairingWroteTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _mappedUser = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _peerUser = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _film = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _secondFilm = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset _evening = new(2026, 8, 27, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _laterEvening = new(2026, 8, 28, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _watchedBefore = new(2026, 8, 20, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The ordinary case. A value that is still standing exactly as this plugin left it is the
    /// value the peer's answer replaced, so the undo puts back what was there before it.
    /// </summary>
    [Fact]
    public void AValueStillStandingAsThisPluginWroteItIsPutBack()
    {
        var record = Record(
            Write(_film, SyncedField.Played, before: 0, written: 1),
            Write(_film, SyncedField.PlaybackPositionTicks, before: 300, written: 900));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: true, playCount: 1, position: 900, lastPlayed: _watchedAt)));

        var item = Assert.Single(answer.Restore);

        Assert.Equal(_film, item.ItemId);
        Assert.False(item.Restored.Played);
        Assert.Equal(300, item.Restored.PlaybackPositionTicks);
        Assert.Empty(answer.Skipped);
        Assert.Equal(2, answer.Restoring);
    }

    /// <summary>
    /// #44's third condition. The person watched the film again after this plugin wrote, so the
    /// value standing is not the one it left, their action outranks the undo, and the skip is an
    /// entry rather than an absence a caller has to notice.
    ///
    /// The item is left out of the write entirely rather than written with three of its four
    /// fields put back, because a write assigns all four and the position it would carry is the
    /// one the person's own play produced.
    /// </summary>
    [Fact]
    public void AValueThePersonChangedAfterwardsIsSkippedAndTheSkipIsRecorded()
    {
        var record = Record(Write(_film, SyncedField.PlaybackPositionTicks, before: 300, written: 900));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: false, playCount: 0, position: 4200, lastPlayed: _watchedAt)));

        Assert.Empty(answer.Restore);
        Assert.Equal(0, answer.Restoring);

        var skip = Assert.Single(answer.Skipped);

        Assert.Equal(_film, skip.ItemId);
        Assert.Equal(SyncedField.PlaybackPositionTicks, skip.Field);
        Assert.Equal(SkipReason.NotTheValueThisPluginLeft, skip.Reason);
    }

    /// <summary>
    /// One field written twice. The newest entry is what the record should still be standing on
    /// and its replaced value is what to put back; the older entry's replaced value is one the
    /// person had already moved on from before this plugin wrote the second time.
    ///
    /// This is the walk <c>ProvenanceRecords</c> describes, and it is the difference a reader
    /// cannot see in an answer: reading the oldest entry instead restores 300 here rather than
    /// 900, and both are numbers this plugin genuinely recorded.
    /// </summary>
    [Fact]
    public void TheNewestWriteOfAFieldIsTheOneTheUndoReads()
    {
        var record = Record(
            Write(_film, SyncedField.PlaybackPositionTicks, before: 300, written: 900),
            Write(_film, SyncedField.PlaybackPositionTicks, before: 900, written: 1500, at: _laterEvening));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: false, playCount: 0, position: 1500, lastPlayed: _watchedAt)));

        var item = Assert.Single(answer.Restore);

        Assert.Equal(900, item.Restored.PlaybackPositionTicks);
        Assert.Equal(new[] { SyncedField.PlaybackPositionTicks }, item.Fields);
        Assert.Empty(answer.Skipped);
    }

    /// <summary>
    /// A write over a server that held nothing. The record carries the absence faithfully and the
    /// write interface has no way to say "hold nothing again", so this is a residual of the
    /// interface rather than of the record, and it is named as its own reason because an operator
    /// asking what a revocation left behind is owed the difference between this and a person's own
    /// change.
    /// </summary>
    [Fact]
    public void AWriteOverAServerThatHeldNothingIsSkippedRatherThanGuessed()
    {
        var record = Record(Write(_film, SyncedField.Played, before: null, written: 1));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: true, playCount: 1, position: 0, lastPlayed: _watchedAt)));

        Assert.Empty(answer.Restore);

        var skip = Assert.Single(answer.Skipped);

        Assert.Equal(SkipReason.NothingToPutBack, skip.Reason);
    }

    /// <summary>
    /// The one field whose absence a write can express. A last played date this plugin overwrote
    /// on a record that had none goes back to having none, rather than to the first instant a date
    /// can hold, which is what a sentinel at this call site would have produced.
    /// </summary>
    [Fact]
    public void ALastPlayedDateThisPluginWroteOverNothingGoesBackToNothing()
    {
        var record = Record(Write(_film, SyncedField.LastPlayedDate, before: null, written: _watchedAt.Ticks));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: true, playCount: 1, position: 0, lastPlayed: _watchedAt)));

        var item = Assert.Single(answer.Restore);

        Assert.Null(item.Restored.LastPlayedDate);
        Assert.Empty(answer.Skipped);
    }

    /// <summary>
    /// An item the server holds no record for at all. There is nothing standing to correct, and
    /// putting a value back would create a record where the person has none, which is a stronger
    /// change than the one the undo was asked for.
    /// </summary>
    [Fact]
    public void AnItemTheServerNoLongerHoldsARecordForIsLeftAlone()
    {
        var record = Record(
            Write(_film, SyncedField.Played, before: 0, written: 1),
            Write(_film, SyncedField.PlayCount, before: 0, written: 1));

        var answer = UndoOfWhatAPairingWrote.Decide(record, Held(_film, null));

        Assert.Empty(answer.Restore);
        Assert.Equal(2, answer.Skipped.Count);
        Assert.All(answer.Skipped, skip => Assert.Equal(SkipReason.NoRecordStandsNow, skip.Reason));
    }

    /// <summary>
    /// A number a document can hold and the field cannot. The record keeps every value as the
    /// number a document holds, and a play count read back out of bytes can be wider than the
    /// count the server keeps, so assigning it would put back a number nobody ever had.
    ///
    /// It is a skip rather than a refusal, because stopping here would abandon the rest of a
    /// revocation over one value that could not have been written in the first place.
    /// </summary>
    [Fact]
    public void ARecordedValueTooWideForItsFieldIsSkippedRatherThanTruncated()
    {
        var record = Record(
            Write(_film, SyncedField.PlayCount, before: (long)int.MaxValue + 1, written: 5));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: true, playCount: 5, position: 0, lastPlayed: _watchedAt)));

        Assert.Empty(answer.Restore);

        var skip = Assert.Single(answer.Skipped);

        Assert.Equal(SkipReason.ValueDoesNotFitTheField, skip.Reason);
    }

    /// <summary>
    /// The fields nobody is putting back arrive at the server as the values standing now, because
    /// a write assigns all four. An undo that assembled its state from the record alone would
    /// carry three values this plugin never wrote and would undo a person's own play along with
    /// the peer's answer.
    /// </summary>
    [Fact]
    public void TheFieldsNobodyIsPuttingBackAreTheValuesStandingNow()
    {
        var record = Record(Write(_film, SyncedField.Played, before: 0, written: 1));

        var answer = UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: true, playCount: 3, position: 4200, lastPlayed: _watchedAt)));

        var item = Assert.Single(answer.Restore);

        Assert.False(item.Restored.Played);
        Assert.Equal(3, item.Restored.PlayCount);
        Assert.Equal(4200, item.Restored.PlaybackPositionTicks);
        Assert.Equal(_watchedAt, item.Restored.LastPlayedDate);
        Assert.Equal(new[] { SyncedField.Played }, item.Fields);
    }

    /// <summary>
    /// Every field the record names is accounted for, in one list or the other, across items that
    /// answer differently. A count of skips is only readable beside a count of restorations if
    /// there is no fifth outcome the answer is quiet about.
    /// </summary>
    [Fact]
    public void EveryFieldTheRecordNamesIsEitherPutBackOrSkipped()
    {
        var record = Record(
            Write(_film, SyncedField.Played, before: 0, written: 1),
            Write(_film, SyncedField.PlaybackPositionTicks, before: 300, written: 900),
            Write(_secondFilm, SyncedField.PlayCount, before: 1, written: 2),
            Write(_secondFilm, SyncedField.LastPlayedDate, before: _watchedBefore.Ticks, written: _watchedAt.Ticks));

        var held = new Dictionary<Guid, SyncedState?>
        {
            [_film] = State(played: true, playCount: 1, position: 900, lastPlayed: _watchedAt),
            [_secondFilm] = State(played: true, playCount: 9, position: 0, lastPlayed: _watchedAt),
        };

        var answer = UndoOfWhatAPairingWrote.Decide(record, held);

        Assert.Equal(4, answer.Restoring + answer.Skipped.Count);
        Assert.Equal(3, answer.Restoring);

        var skip = Assert.Single(answer.Skipped);

        Assert.Equal(_secondFilm, skip.ItemId);
        Assert.Equal(SyncedField.PlayCount, skip.Field);
        Assert.Equal(SkipReason.NotTheValueThisPluginLeft, skip.Reason);
    }

    /// <summary>
    /// A caller that read some of the items and asked anyway is refused. An answer assembled from
    /// a partial reading would be an undo covering part of a pairing while reporting the whole of
    /// it, and the person told their data was put back would have been told something specific.
    /// </summary>
    [Fact]
    public void AReadingMissingForAnItemTheRecordNamesIsRefused()
    {
        var record = Record(
            Write(_film, SyncedField.Played, before: 0, written: 1),
            Write(_secondFilm, SyncedField.Played, before: 0, written: 1));

        Assert.Throws<ArgumentException>(() => UndoOfWhatAPairingWrote.Decide(
            record,
            Held(_film, State(played: true, playCount: 1, position: 0, lastPlayed: _watchedAt))));
    }

    /// <summary>
    /// A pairing that wrote nothing has nothing to undo, and answers so rather than refusing. A
    /// revocation of a pairing that never exchanged anything is an ordinary event.
    /// </summary>
    [Fact]
    public void APairingThatWroteNothingDecidesNothing()
    {
        var answer = UndoOfWhatAPairingWrote.Decide(
            ProvenanceRecords.NoneYet(_pairing, _mappedUser),
            new Dictionary<Guid, SyncedState?>());

        Assert.Empty(answer.Restore);
        Assert.Empty(answer.Skipped);
        Assert.Equal(0, answer.Restoring);
    }

    /// <summary>
    /// An item with no field to put back is not an item to write. The answer never builds one, and
    /// the type refuses it so that a second caller assembling an answer of its own cannot hand a
    /// writer an item whose four assigned values are all the person's own.
    /// </summary>
    [Fact]
    public void AnItemWithNoFieldToPutBackIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ItemToRestore(
            _film,
            State(played: true, playCount: 1, position: 0, lastPlayed: _watchedAt),
            Array.Empty<SyncedField>()));
    }

    private static ProvenanceRecords Record(params ProvenanceRecord[] writes) =>
        writes.Aggregate(
            ProvenanceRecords.NoneYet(_pairing, _mappedUser),
            (record, write) => record.With(write));

    private static ProvenanceRecord Write(
        Guid itemId,
        SyncedField field,
        long? before,
        long? written,
        DateTimeOffset? at = null) =>
        new ProvenanceRecord(
            _pairing,
            _mappedUser,
            _peerUser,
            itemId,
            field,
            before,
            written,
            at ?? _evening);

    private static SyncedState State(
        bool played,
        int playCount,
        long position,
        DateTime? lastPlayed) =>
        new SyncedState(played, playCount, position, lastPlayed);

    private static Dictionary<Guid, SyncedState?> Held(Guid itemId, SyncedState? state) =>
        new Dictionary<Guid, SyncedState?> { [itemId] = state };
}

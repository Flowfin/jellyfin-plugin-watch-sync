using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Exchange;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Tests.Harness;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What a first exchange writes down about the values it discarded, which is the first condition
/// of #36.
///
/// A conflict is a moment where the two servers hold different values for one mapped user, one
/// leaf item and one moved field, which is the sentence <c>docs/conflicts.md</c> opens with. So
/// what these facts hold is a row-by-row correspondence: a row whose two readings disagree leaves
/// exactly one record, that record names the rule the table's own rule column gives the row, and
/// the side it names as having lost is the side whose reading is not the resolved one. A row
/// whose two readings agree leaves nothing, because nothing was answered and nothing was thrown
/// away.
///
/// The two facts worth reading twice are the ones that count zero. An item the run left standing
/// and an item an earlier run already agreed each carry rows that disagree, and neither is a
/// conflict this run resolved: recording either would tell an operator that this plugin discarded
/// a value on a run where it decided nothing.
///
/// Every moment here is the harness clock rather than the machine clock, under the rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class FirstExchangeConflictRecordTests
{
    private static readonly Guid _pairing = new Guid("11111111111111111111111111111111");

    private static readonly Guid _someone = new Guid("22222222222222222222222222222222");

    private static readonly DateTimeOffset _agreedAt = TwoServers.Epoch.AddHours(2);

    private static readonly DateTime _now = TwoServers.Epoch.UtcDateTime.AddHours(2);

    /// <summary>
    /// One item disagreeing on all four rows leaves one record per row, and each names the rule
    /// the table gives that row.
    ///
    /// This is the whole table run at once and counted, which is what the first condition of #36
    /// asks for. Counting alone would pass over a run that wrote four records naming one rule, so
    /// the field, the rule, the two readings and the discarded side are asserted per row rather
    /// than the number of records being the assertion.
    /// </summary>
    [Fact]
    public void EveryRowThatDisagreedLeavesOneRecordNamingItsOwnRule()
    {
        var item = Guid.NewGuid();

        var exchange = Run(Pair(item, Finished(1, At(30)), Partway(900, At(10))));

        Assert.Equal(4, exchange.Conflicts.Count);

        var played = Row(exchange, item, SyncedField.Played);
        Assert.Equal(ConflictRule.Ratchet, played.Rule);
        Assert.Equal(1, played.Here);
        Assert.Equal(0, played.AtThePeer);
        Assert.Equal(ConflictSide.AtThePeer, played.Discarded);

        var count = Row(exchange, item, SyncedField.PlayCount);
        Assert.Equal(ConflictRule.Reckon, count.Rule);
        Assert.Equal(1, count.Here);
        Assert.Equal(0, count.AtThePeer);
        Assert.Equal(ConflictSide.Neither, count.Discarded);

        var position = Row(exchange, item, SyncedField.PlaybackPositionTicks);
        Assert.Equal(ConflictRule.Ratchet, position.Rule);
        Assert.Equal(0, position.Here);
        Assert.Equal(900, position.AtThePeer);
        Assert.Equal(ConflictSide.AtThePeer, position.Discarded);

        var lastPlayed = Row(exchange, item, SyncedField.LastPlayedDate);
        Assert.Equal(ConflictRule.Maximum, lastPlayed.Rule);
        Assert.Equal(At(30).Ticks, lastPlayed.Here);
        Assert.Equal(At(10).Ticks, lastPlayed.AtThePeer);
        Assert.Equal(ConflictSide.Neither, lastPlayed.Discarded);
    }

    /// <summary>
    /// The position offered against a completion is the ratchet's loss, and the record says which
    /// side offered it.
    ///
    /// The table's own loser column for the played row is this sentence, and it is asserted from
    /// both sides because a rule that named the discarded side by reading which server it was
    /// asked from would answer the same pair differently in the two directions. Which of the two
    /// runs a first exchange is not something either server chooses.
    /// </summary>
    [Fact]
    public void ThePositionOfferedAgainstACompletionIsRecordedAsTheLoss()
    {
        var here = Guid.NewGuid();
        var atThePeer = Guid.NewGuid();

        var exchange = Run(
            Pair(here, Finished(1, At(30)), Partway(900, At(10))),
            Pair(atThePeer, Partway(900, At(10)), Finished(1, At(30))));

        var offeredHere = Row(exchange, atThePeer, SyncedField.PlaybackPositionTicks);
        Assert.Equal(ConflictRule.Ratchet, offeredHere.Rule);
        Assert.Equal(900, offeredHere.Here);
        Assert.Equal(ConflictSide.Here, offeredHere.Discarded);

        var offeredAtThePeer = Row(exchange, here, SyncedField.PlaybackPositionTicks);
        Assert.Equal(ConflictRule.Ratchet, offeredAtThePeer.Rule);
        Assert.Equal(900, offeredAtThePeer.AtThePeer);
        Assert.Equal(ConflictSide.AtThePeer, offeredAtThePeer.Discarded);
    }

    /// <summary>
    /// Where neither side holds the work played, the position row is recency's and the older
    /// reading is what the record names as discarded.
    ///
    /// The tie is the second half of the same row and is asserted beside it, because the tie rule
    /// is the one a reader is most likely to take for an absence of a decision. Two positions
    /// whose moments are closer together than the tolerated skew are not compared at all, the
    /// greater position stands, and the smaller one is a value somebody had that this plugin
    /// stopped showing them.
    /// </summary>
    [Fact]
    public void APositionSettledByRecencyRecordsTheReadingThatDidNotSurvive()
    {
        var compared = Guid.NewGuid();
        var tied = Guid.NewGuid();

        var exchange = Run(
            Pair(compared, Partway(1200, At(70)), Partway(400, At(5))),
            Pair(tied, Partway(400, At(70)), Partway(1200, At(70))));

        var later = Row(exchange, compared, SyncedField.PlaybackPositionTicks);
        Assert.Equal(ConflictRule.Recency, later.Rule);
        Assert.Equal(1200, later.Here);
        Assert.Equal(400, later.AtThePeer);
        Assert.Equal(ConflictSide.AtThePeer, later.Discarded);

        var greater = Row(exchange, tied, SyncedField.PlaybackPositionTicks);
        Assert.Equal(ConflictRule.Recency, greater.Rule);
        Assert.Equal(400, greater.Here);
        Assert.Equal(1200, greater.AtThePeer);
        Assert.Equal(ConflictSide.Here, greater.Discarded);
    }

    /// <summary>
    /// A row whose two readings agree leaves no record.
    ///
    /// Two servers holding the same value for one field are not in conflict about it, so a record
    /// there would be an entry saying a value was discarded on a row where both readings survived
    /// intact. It also decides what the bound in <c>ConflictRecords</c> is spent on: a first
    /// exchange over a library two servers largely agree about writes nothing for the items they
    /// agree about, so the two hundred entries it keeps are two hundred disagreements.
    /// </summary>
    [Fact]
    public void AnItemTheTwoSidesAgreeAboutLeavesNothingRecorded()
    {
        var identical = Guid.NewGuid();
        var oneRowApart = Guid.NewGuid();

        var exchange = Run(
            Pair(identical, Finished(2, At(30)), Finished(2, At(30))),
            Pair(oneRowApart, Finished(2, At(30)), Finished(2, At(45))));

        Assert.Equal(2, exchange.Decided.Count);
        Assert.DoesNotContain(exchange.Conflicts.All, each => each.ItemId == identical);

        var only = Assert.Single(exchange.Conflicts.All, each => each.ItemId == oneRowApart);
        Assert.Equal(SyncedField.LastPlayedDate, only.Field);
    }

    /// <summary>
    /// An item the run left standing leaves no record, whatever its rows disagree about.
    ///
    /// Nothing was decided for it and nothing was discarded: both sides keep exactly what they
    /// held, and what an operator has to be shown is the reason it was left standing, which the
    /// resolution carries. A record here would say this plugin threw a value away on the one
    /// outcome where it deliberately threw nothing away.
    ///
    /// The fixture disagrees on two rows rather than one, so a run that recorded undecided items
    /// is caught by a count of two rather than by a count of one that a later fixture change
    /// could make accidentally right.
    /// </summary>
    [Fact]
    public void AnItemTheRunLeftStandingIsNotRecordedAsAConflict()
    {
        var standing = Guid.NewGuid();

        var exchange = Run(Pair(standing, Finished(2, At(20)), Finished(3, At(50))));

        Assert.Single(exchange.Undecided);
        Assert.Equal(0, exchange.Conflicts.Count);
    }

    /// <summary>
    /// An item an earlier run of the same first exchange already agreed is not recorded again.
    ///
    /// The rules are not asked for it, so there is no rule for a record to name, and a resumed
    /// run that wrote one would report a conflict at the moment it resumed rather than at the
    /// moment the value was discarded. The two sides still disagree in this fixture, which is
    /// what makes the count discriminate: the item would leave three records if the run recorded
    /// what it skipped.
    /// </summary>
    [Fact]
    public void AnItemAnEarlierRunAgreedIsNotRecordedAgain()
    {
        var already = Guid.NewGuid();
        var subject = Subject(already);

        var records = AgreedRecords
            .NoneYet(_pairing, _someone)
            .With(new AgreedRecord(
                subject,
                Finished(1, At(30)),
                TwoServers.Epoch.AddHours(1),
                EnvelopeVersions.Current));

        var exchange = FirstExchange.Over(
            records,
            new[] { new ItemOnBothSides(subject, Finished(2, At(60)), Partway(500, At(10))) },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        Assert.Equal(ResolutionAnswer.AlreadyAgreed, Assert.Single(exchange.Resolutions).Answer);
        Assert.Equal(0, exchange.Conflicts.Count);
    }

    /// <summary>
    /// Every record is filed under the pairing and the person the run was about, and carries the
    /// moment that run decided.
    ///
    /// The record is one pairing's and one person's, which <c>ConflictRecords</c> refuses a
    /// conflict from elsewhere for, and this is the other half of that: what this run produces
    /// has to be admissible into the document the store holds for that pair. The moment is the
    /// run's own rather than a second reading of a clock, so a record and the agreement it came
    /// with cannot be found disagreeing about when the exchange happened.
    /// </summary>
    [Fact]
    public void EveryRecordIsFiledUnderThePairingThePersonAndTheMoment()
    {
        var item = Guid.NewGuid();

        var exchange = Run(Pair(item, Finished(1, At(30)), Partway(900, At(10))));

        Assert.Equal(_pairing, exchange.Conflicts.PairingId);
        Assert.Equal(_someone, exchange.Conflicts.MappedUserId);
        Assert.NotEmpty(exchange.Conflicts.All);

        foreach (var conflict in exchange.Conflicts.All)
        {
            Assert.Equal(_pairing, conflict.PairingId);
            Assert.Equal(_someone, conflict.MappedUserId);
            Assert.Equal(item, conflict.ItemId);
            Assert.Equal(_agreedAt, conflict.RecordedAt);
        }
    }

    private static ConflictRecord Row(FirstExchange exchange, Guid itemId, SyncedField field) =>
        exchange.Conflicts.All.Single(each => each.ItemId == itemId && each.Field == field);

    private static FirstExchange Run(params ItemOnBothSides[] items) =>
        FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, _someone),
            items,
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

    private static SyncedState Finished(int playCount, DateTime lastPlayed) =>
        new SyncedState(true, playCount, 0, lastPlayed);

    private static SyncedState Partway(long positionTicks, DateTime lastPlayed) =>
        new SyncedState(false, 0, positionTicks, lastPlayed);

    private static DateTime At(int minutes) => TwoServers.Epoch.UtcDateTime.AddMinutes(minutes);

    private static TransferSubject Subject(Guid itemId) =>
        TransferSubject.From(_someone, itemId, BaseItemKind.Movie).Value!;

    private static ItemOnBothSides Pair(Guid itemId, SyncedState here, SyncedState atThePeer) =>
        new ItemOnBothSides(Subject(itemId), here, atThePeer);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using Jellyfin.Plugin.WatchSync.Tests.Apply;
using Jellyfin.Plugin.WatchSync.Transfer;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The cap judged before anything is written, and the approval of a run it stopped, which are
/// the second, third and fourth conditions of #38.
///
/// The failure the whole set is written against is a run that marks a large part of a library
/// watched, or clears it, because a mapping was wrong, a match was wrong, or a peer was restored
/// from a backup. Every other rule in this plugin assumes its own inputs are right; this is the
/// one that limits the damage when one of them is not. So the facts here are about what reaches
/// the server: a run the cap stops writes nothing, a run within it writes exactly what the walk
/// would have, and an approved plan writes what it recorded and nothing that moved since.
///
/// Nothing here reads a clock. The moment a run or an approval happens is a parameter, which is
/// the injected clock invariant and the headless rule together.
/// </summary>
public sealed class CappedApplyTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _peerUser = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _evening = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 9, 3, 20, 0, 0, DateTimeKind.Utc);

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="CappedApplyTests"/> class.
    /// </summary>
    public CappedApplyTests()
    {
        _programData = TemporaryDirectory.Create("stopped");
    }

    /// <summary>
    /// The fourth condition of #38. A run well under the cap is unaffected and pays no visible
    /// cost: the server sees the same writes in the same order as it would from the walk alone,
    /// and the same reads, because a cap that read every item before judging would be a cost an
    /// ordinary evening pays for having been judged. No plan is recorded.
    /// </summary>
    [Fact]
    public void ARunWithinTheCapWalksAsTheWalkWouldHaveAndPaysNothingVisible()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(3);
        var capped = new RecordedWrites();
        var plain = new RecordedWrites();

        var answer = CappedApply.Apply(
            user,
            Items(user, films),
            matched: 1000,
            RunCap.DefaultMaximumChanges,
            RunCap.DefaultMaximumShare,
            capped,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        ItemByItemApply.Apply(
            user,
            Items(user, films),
            plain,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.False(answer.IsStopped);
        Assert.Null(answer.Stopped);
        Assert.Equal(RunCapAnswer.Within, answer.Verdict.Answer);

        var walk = Assert.IsType<ApplyAnswer>(answer.Walk);

        Assert.Equal(3, walk.Applied.Count);
        Assert.Equal(3, walk.Agreed.Count);
        Assert.Equal(
            plain.Writes.Select(write => write.ItemId).ToList(),
            capped.Writes.Select(write => write.ItemId).ToList());
        Assert.Equal(plain.Reads, capped.Reads);
    }

    /// <summary>
    /// The second condition of #38, on the count. A run that would exceed the cap stops, writes
    /// nothing, and records what it would have done: every item in the order it would have been
    /// written, the state decided for it, and what this server held at that moment.
    ///
    /// Six changes against a count of five is the one-item mistake, and the library is large
    /// enough that the share allows a hundred, so what stopped this run is the count and nothing
    /// else.
    /// </summary>
    [Fact]
    public void ARunOverTheCountStopsWritesNothingAndRecordsWhatItWouldHaveDone()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(6);
        var server = new RecordedWrites();
        var held = new SyncedState(false, 0, 1200, null);
        server.Hold(films[2], held);

        var answer = CappedApply.Apply(
            user,
            Items(user, films),
            matched: 1000,
            maximumChanges: 5,
            maximumShare: 0.10,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.True(answer.IsStopped);
        Assert.Null(answer.Walk);
        Assert.Empty(server.Writes);

        var plan = Assert.IsType<StoppedRun>(answer.Stopped);

        Assert.Equal(RunCapAnswer.ExceedsCount, plan.Answer);
        Assert.Equal(6, plan.Changes);
        Assert.Equal(5, plan.Allowed);
        Assert.Equal(1000, plan.Matched);
        Assert.Equal(_evening, plan.StoppedAt);
        Assert.Equal(_pairing, plan.PairingId);
        Assert.Equal(user.Id, plan.MappedUserId);
        Assert.Equal(films, plan.Items.Select(item => item.Subject.ItemId).ToArray());
        Assert.All(plan.Items, item => Assert.True(item.HeldWasRead));
        Assert.All(plan.Items, item => Assert.True(item.Decided.Played));
        Assert.Null(plan.Items[0].Held);
        Assert.True(StoppedRunItem.SameReading(held, plan.Items[2].Held));
    }

    /// <summary>
    /// The same condition on the share, which is the bound the count is blind to. Three changes
    /// against twenty matched items is fifteen per cent of somebody's history and is nowhere
    /// near a count of a hundred, and the plan says which bound stopped it, because an operator
    /// deciding whether to approve has to be told.
    /// </summary>
    [Fact]
    public void ARunOverTheShareStopsOnALibraryTheCountWouldHaveLetThrough()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        var answer = CappedApply.Apply(
            user,
            Items(user, Films(3)),
            matched: 20,
            RunCap.DefaultMaximumChanges,
            RunCap.DefaultMaximumShare,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.True(answer.IsStopped);
        Assert.Empty(server.Writes);

        var plan = Assert.IsType<StoppedRun>(answer.Stopped);

        Assert.Equal(RunCapAnswer.ExceedsShare, plan.Answer);
        Assert.Equal(3, plan.Changes);
        Assert.Equal(2, plan.Allowed);
    }

    /// <summary>
    /// The third condition of #38, on the route a plan actually takes. The plan is written to the
    /// store, read back as an operator's approval would read it, and approved, and what reaches
    /// the server is exactly what the plan recorded, in the order it recorded it. The approval
    /// does not ask the cap again: six changes against a count of five were stopped once, and the
    /// operator's approval is the answer to that question. Nor does it ask which peer user the
    /// values came from or which envelope version carried them, because the plan is what knows.
    /// </summary>
    [Fact]
    public void ARecordedPlanSurvivesTheStoreAndIsApprovedAsRecorded()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(6);
        var server = new RecordedWrites();
        server.Hold(films[2], new SyncedState(false, 0, 1200, null));

        var stopped = CappedApply.Apply(
            user,
            Items(user, films),
            matched: 1000,
            maximumChanges: 5,
            maximumShare: 0.10,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            3,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None).Stopped!;

        Assert.Equal(_peerUser, stopped.PeerUserId);
        Assert.Equal(3, stopped.EnvelopeVersion);

        var name = StoppedRun.DocumentName(_pairing, user.Id);

        new DocumentStore(Folder()).Write(name, _ => stopped.ToDocument());

        var reading = StoppedRun.Read(new DocumentStore(Folder()).Read(name)!.Document!);

        Assert.False(reading.IsRefused);

        var approval = CappedApply.Approve(
            reading.Run!,
            user,
            Library(films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            FailureShare.DefaultMaximumShare,
            _evening.AddDays(1),
            CancellationToken.None);

        Assert.Empty(approval.SetAside);
        Assert.Equal(films, approval.Applied.Select(subject => subject.ItemId).ToArray());
        Assert.Equal(films, server.Writes.Select(write => write.ItemId).ToArray());
        Assert.Equal(6, approval.Walk.Agreed.Count);

        // The peer user and the envelope version the approval stamps and agrees are the plan's
        // and nobody else's, because the plan is the only thing that still knows them.
        Assert.NotEmpty(approval.Walk.Provenance.All);
        Assert.All(approval.Walk.Provenance.All, entry => Assert.Equal(_peerUser, entry.PeerUserId));
        Assert.All(films, film => Assert.Equal(3, approval.Walk.Agreed.For(film)!.EnvelopeVersion));

        foreach (var film in films)
        {
            var written = Assert.IsType<SyncedState>(server.HeldFor(film));

            Assert.True(written.Played);
            Assert.Equal(2, written.PlayCount);
        }
    }

    /// <summary>
    /// The clause of the third condition that is the hard one: nothing that changed in the
    /// meantime is written without being noticed. One item moved between the stop and the
    /// approval, because a person watched it, and the approval sets it aside, writes the others,
    /// and leaves the moved value where the person put it.
    ///
    /// Deleting the comparison in the approval turns this red and nothing else, which is the
    /// proof this guard bites for the reason it names.
    /// </summary>
    [Fact]
    public void AnItemThatMovedSinceTheRunStoppedIsSetAsideAndNotWritten()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(3);
        var server = new RecordedWrites();
        server.Hold(films[1], new SyncedState(false, 0, 1200, null));

        var plan = Stopped(user, films, server);

        var moved = new SyncedState(false, 0, 4800, _watchedAt);
        server.Hold(films[1], moved);

        var approval = Approve(plan, user, films, server);

        var aside = Assert.Single(approval.SetAside);

        Assert.Equal(films[1], aside.Subject.ItemId);
        Assert.Equal(SetAsideReason.HeldMovedSinceTheRunStopped, aside.Reason);
        Assert.Equal(new[] { films[0], films[2] }, server.Writes.Select(write => write.ItemId).ToArray());
        Assert.Same(moved, server.HeldFor(films[1]));
        Assert.Null(approval.Walk.Agreed.For(films[1]));
    }

    /// <summary>
    /// An absence is a reading too. The plan recorded that this server held nothing for an item,
    /// and by the approval it holds a record, so the item moved and is set aside; and the other
    /// way round, a plan that recorded a state meets an item the record is gone from. Neither is
    /// the same reading and neither is written.
    /// </summary>
    [Fact]
    public void AnAbsenceThatBecameAStateIsAMoveAndSoIsTheReverse()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(2);
        var server = new RecordedWrites();
        server.Hold(films[1], new SyncedState(false, 0, 1200, null));

        var plan = Stopped(user, films, server);

        server.Hold(films[0], new SyncedState(false, 1, 0, _watchedAt));
        server.Forget(films[1]);

        var approval = Approve(plan, user, films, server);

        Assert.Equal(2, approval.SetAside.Count);
        Assert.All(approval.SetAside, aside => Assert.Equal(SetAsideReason.HeldMovedSinceTheRunStopped, aside.Reason));
        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// The library no longer holds an item the plan names, so there is nothing to write against
    /// and the item is set aside rather than the approval failing or the item being skipped in
    /// silence.
    /// </summary>
    [Fact]
    public void AnItemTheLibraryNoLongerHoldsIsSetAside()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(3);
        var server = new RecordedWrites();

        var plan = Stopped(user, films, server);

        var approval = CappedApply.Approve(
            plan,
            user,
            Library(films.Take(2).ToArray()),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            FailureShare.DefaultMaximumShare,
            _evening.AddDays(1),
            CancellationToken.None);

        var aside = Assert.Single(approval.SetAside);

        Assert.Equal(films[2], aside.Subject.ItemId);
        Assert.Equal(SetAsideReason.ItemGoneFromTheLibrary, aside.Reason);
        Assert.Equal(2, approval.Applied.Count);
    }

    /// <summary>
    /// A read refused at the moment the run stopped leaves the plan with no baseline for that
    /// item, and the plan says so rather than recording nothing as if it had been read. The
    /// approval sets that item aside for that reason, and it is that reason rather than a refusal
    /// at the approval, because the plan's own word comes first.
    /// </summary>
    [Fact]
    public void AnItemWhoseBaselineWasNotReadIsRecordedAsUnreadAndSetAside()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(3);
        var server = new RecordedWrites();
        server.RefuseRead(films[1], new InvalidOperationException("the record is not readable"));

        var plan = Stopped(user, films, server);

        Assert.False(plan.Items[1].HeldWasRead);
        Assert.Null(plan.Items[1].Held);
        Assert.True(plan.Items[0].HeldWasRead);

        var approval = Approve(plan, user, films, server);

        var aside = Assert.Single(approval.SetAside);

        Assert.Equal(films[1], aside.Subject.ItemId);
        Assert.Equal(SetAsideReason.HeldWasNotReadWhenTheRunStopped, aside.Reason);
        Assert.Equal(new[] { films[0], films[2] }, server.Writes.Select(write => write.ItemId).ToArray());
    }

    /// <summary>
    /// A read refused at the approval leaves whether the item moved undecidable, and an item
    /// nobody can say has not moved is not written on a guess.
    /// </summary>
    [Fact]
    public void AnItemThatCannotBeReadAtTheApprovalIsSetAside()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(2);
        var server = new RecordedWrites();

        var plan = Stopped(user, films, server);

        server.RefuseRead(films[0], new TimeoutException("the database is busy"));

        var approval = Approve(plan, user, films, server);

        var aside = Assert.Single(approval.SetAside);

        Assert.Equal(films[0], aside.Subject.ItemId);
        Assert.Equal(SetAsideReason.HeldCouldNotBeReadAtTheApproval, aside.Reason);
        Assert.Equal(films[1], Assert.Single(server.Writes).ItemId);
    }

    /// <summary>
    /// A setting outside what either rule accepts is refused before the first read, on both
    /// paths. A run that read six items and then threw for a bound it was handed at the start
    /// would have cost the server six reads for a run that was never going to be legal, and one
    /// that wrote three would have cost far more.
    /// </summary>
    /// <param name="maximumChanges">The count bound to offer.</param>
    /// <param name="maximumShare">The share bound to offer.</param>
    /// <param name="maximumFailureShare">The failure share to offer.</param>
    [Theory]
    [InlineData(0, 0.10, 0.50)]
    [InlineData(100, 0.51, 0.50)]
    [InlineData(100, 0.10, 0.10)]
    public void ASettingOutsideTheBoundIsRefusedBeforeAnythingIsReadOrWritten(
        int maximumChanges,
        double maximumShare,
        double maximumFailureShare)
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        Assert.Throws<ArgumentOutOfRangeException>(() => CappedApply.Apply(
            user,
            Items(user, Films(3)),
            matched: 1000,
            maximumChanges,
            maximumShare,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            maximumFailureShare,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Reads);
        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// A set that crosses two people is refused on the stopped path before the first read, as
    /// the walk refuses it before the first write. A plan recorded for such a set would be a plan
    /// an operator could approve into somebody else's account.
    /// </summary>
    [Fact]
    public void ADecidedItemAboutAnotherPersonIsRefusedBeforeAPlanIsRecorded()
    {
        var user = UserDataFixtures.Someone();
        var somebodyElse = UserDataFixtures.Someone();
        var films = Films(3);
        var server = new RecordedWrites();

        var crossed = new List<ItemToApply>
        {
            Item(user, films[0]),
            Item(user, films[1]),
            Item(somebodyElse, films[2]),
        };

        Assert.Throws<ArgumentException>(() => CappedApply.Apply(
            user,
            crossed,
            matched: 1000,
            maximumChanges: 2,
            maximumShare: 0.10,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Reads);
    }

    /// <summary>
    /// An approval of a plan about another person, or under another pairing, is refused before
    /// anything is read. Both are the crossing the walk refuses, met one step earlier, where the
    /// plan rather than the items is what names the person.
    /// </summary>
    [Fact]
    public void AnApprovalOfAPlanAboutAnotherPersonOrPairingIsRefusedBeforeAnythingIsRead()
    {
        var user = UserDataFixtures.Someone();
        var somebodyElse = UserDataFixtures.Someone();
        var films = Films(2);
        var server = new RecordedWrites();

        var theirs = Stopped(somebodyElse, films, new RecordedWrites());
        var mine = Stopped(user, films, new RecordedWrites());
        var otherPairing = new Guid("99999999-9999-9999-9999-999999999999");

        Assert.Throws<ArgumentException>(() => Approve(theirs, user, films, server));

        Assert.Throws<ArgumentException>(() => CappedApply.Approve(
            mine,
            user,
            Library(films),
            server,
            AgreedRecords.NoneYet(otherPairing, user.Id),
            ProvenanceRecords.NoneYet(otherPairing, user.Id),
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Reads);
        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// A cancellation during the reads a stop makes leaves the run rather than being recorded as
    /// an unread baseline. It was asked for by this side and says nothing about the item, and a
    /// plan recording it as unread would set the item aside at the approval for something the
    /// operator did.
    /// </summary>
    [Fact]
    public void ACancelledReadLeavesTheStopRatherThanBeingRecordedAsUnread()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(2);
        var server = new RecordedWrites();
        server.RefuseRead(films[0], new OperationCanceledException());

        Assert.Throws<OperationCanceledException>(() => Stopped(user, films, server));
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    private static Guid[] Films(int count) =>
        Enumerable
            .Range(1, count)
            .Select(index => new Guid($"77777777-0000-0000-0000-{index:D12}"))
            .ToArray();

    private static TransferSubject Subject(User user, Guid itemId)
    {
        var reading = TransferSubject.From(user.Id, itemId, BaseItemKind.Movie);

        Assert.True(reading.IsSubject);

        return reading.Value!;
    }

    private static ItemToApply Item(User user, Guid itemId) =>
        new ItemToApply(
            Subject(user, itemId),
            UserDataFixtures.Work(itemId, TimeSpan.FromHours(2).Ticks),
            new SyncedState(true, 2, 0, _watchedAt));

    private static IReadOnlyList<ItemToApply> Items(User user, params Guid[] itemIds) =>
        itemIds.Select(itemId => Item(user, itemId)).ToList();

    /// <summary>
    /// A library holding exactly these films, answering nothing for any other identifier.
    /// </summary>
    /// <param name="itemIds">The films it holds.</param>
    /// <returns>The lookup.</returns>
    private static Func<Guid, BaseItem?> Library(Guid[] itemIds)
    {
        var items = itemIds.ToDictionary(
            itemId => itemId,
            itemId => UserDataFixtures.Work(itemId, TimeSpan.FromHours(2).Ticks));

        return itemId => items.TryGetValue(itemId, out var item) ? item : null;
    }

    /// <summary>
    /// A run over these films that the count stops, with a bound one below the number of films.
    /// </summary>
    private static StoppedRun Stopped(User user, Guid[] films, RecordedWrites server)
    {
        var answer = CappedApply.Apply(
            user,
            Items(user, films),
            matched: 1000,
            maximumChanges: films.Length - 1,
            maximumShare: 0.10,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.True(answer.IsStopped);
        Assert.Empty(server.Writes);

        return answer.Stopped!;
    }

    private static ApprovalAnswer Approve(StoppedRun plan, User user, Guid[] films, RecordedWrites server) =>
        CappedApply.Approve(
            plan,
            user,
            Library(films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            FailureShare.DefaultMaximumShare,
            _evening.AddDays(1),
            CancellationToken.None);

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }
}

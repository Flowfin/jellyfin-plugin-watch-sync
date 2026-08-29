using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Tests.Apply;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the apply path does with a decided set of items, which is #54.
///
/// The failure the whole set is written against is the all-or-nothing exchange. There is no
/// transaction across two servers, so an exchange that half succeeded is the ordinary outcome of
/// a bad evening, and a walk that stopped at the first refusal would leave the rest of somebody's
/// evening unwritten every time one item was missing from a library. The other direction is worse
/// and is the one the unwind section of <c>docs/transfer.md</c> refuses: a walk that put back what
/// it had already written would make a second pass of writes at the moment the server is already
/// refusing them.
///
/// Nothing here reads a clock. The moment the walk runs is a parameter, which is the injected
/// clock invariant and the headless rule together.
/// </summary>
public class ItemByItemApplyTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _peerUser = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _firstFilm = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _secondFilm = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _thirdFilm = new("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset _evening = new(2026, 8, 27, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The ordinary walk. Every item is written and every one is agreed, and the two lists say
    /// between them what the walk examined.
    /// </summary>
    [Fact]
    public void EveryDecidedItemIsWrittenAndAgreed()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(3, answer.Applied.Count);
        Assert.Empty(answer.Failed);
        Assert.Equal(3, answer.Examined);
        Assert.Equal(3, answer.Agreed.Count);
        Assert.Equal(3, server.Writes.Count);
    }

    /// <summary>
    /// The first condition of #54. One item fails in the middle of the walk, the items on either
    /// side of it are written, and the failure names the item rather than a count.
    /// </summary>
    [Fact]
    public void AnItemThatFailsDoesNotStopTheOnesAfterIt()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_secondFilm, new InvalidOperationException("the library no longer holds it"));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(
            new[] { _firstFilm, _thirdFilm },
            answer.Applied.Select(subject => subject.ItemId).ToArray());

        var failure = Assert.Single(answer.Failed);

        Assert.Equal(_secondFilm, failure.Subject.ItemId);
        Assert.Equal(3, answer.Examined);
    }

    /// <summary>
    /// The second condition of #54, read at the only place it can be read before an exchange
    /// exists. The next exchange retries the failed item and nothing else, and what decides that
    /// is the agreed record: the item that failed keeps the record it had, so it is still
    /// outstanding, and the ones that were written are agreed and are not.
    /// </summary>
    [Fact]
    public void TheAgreedRecordAdvancesForTheWrittenItemsAndForNoOthers()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_secondFilm, new InvalidOperationException("the library no longer holds it"));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.NotNull(answer.Agreed.For(_firstFilm));
        Assert.NotNull(answer.Agreed.For(_thirdFilm));
        Assert.Null(answer.Agreed.For(_secondFilm));
    }

    /// <summary>
    /// The same rule from the other end, where an agreement already existed. A failed item is not
    /// re-agreed and it is not un-agreed either: it keeps exactly the agreement it had, which is
    /// what makes the next exchange offer that item and no other.
    /// </summary>
    [Fact]
    public void AFailedItemKeepsTheAgreementItAlreadyHad()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_secondFilm, new InvalidOperationException("the library no longer holds it"));

        var before = new SyncedState(false, 1, 300, _watchedAt);
        var standing = AgreedRecords
            .NoneYet(_pairing, user.Id)
            .With(new AgreedRecord(Subject(user, _secondFilm), before, _evening, 1));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _secondFilm),
            server,
            standing,
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening.AddHours(1),
            CancellationToken.None);

        var kept = Assert.IsType<AgreedRecord>(answer.Agreed.For(_secondFilm));

        Assert.Equal(before.PlayCount, kept.Agreed.PlayCount);
        Assert.Equal(before.PlaybackPositionTicks, kept.Agreed.PlaybackPositionTicks);
        Assert.Equal(_evening, kept.AgreedAt);
    }

    /// <summary>
    /// The unwind rule in <c>docs/transfer.md</c>, asserted rather than described. An item written
    /// before the failure holds the value it was given afterwards, and nothing writes it a second
    /// time on the way out.
    /// </summary>
    [Fact]
    public void NothingAlreadyWrittenIsWrittenAgainAfterAFailure()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_secondFilm, new InvalidOperationException("the library no longer holds it"));

        ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(
            new[] { _firstFilm, _secondFilm, _thirdFilm },
            server.Writes.Select(write => write.ItemId).ToArray());

        var held = Assert.IsType<SyncedState>(server.HeldFor(_firstFilm));

        Assert.True(held.Played);
        Assert.Equal(2, held.PlayCount);
    }

    /// <summary>
    /// #50, at the one place the walk decides it. The same decided set applied twice leaves the
    /// same state, the play count included, because every field is assigned and none is added to.
    /// </summary>
    [Fact]
    public void ApplyingTheSameSetTwiceLeavesTheSameState()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var items = Items(user, _firstFilm);

        ItemByItemApply.Apply(
            user,
            items,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        var afterOnce = Assert.IsType<SyncedState>(server.HeldFor(_firstFilm));

        ItemByItemApply.Apply(
            user,
            items,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening.AddHours(1),
            CancellationToken.None);

        var afterTwice = Assert.IsType<SyncedState>(server.HeldFor(_firstFilm));

        Assert.Equal(afterOnce.Played, afterTwice.Played);
        Assert.Equal(afterOnce.PlayCount, afterTwice.PlayCount);
        Assert.Equal(afterOnce.PlaybackPositionTicks, afterTwice.PlaybackPositionTicks);
        Assert.Equal(afterOnce.LastPlayedDate, afterTwice.LastPlayedDate);
    }

    /// <summary>
    /// What an operator is left with. The reason is the name of the type the write was refused
    /// with, and never its message, because a message on this surface carries a path or the title
    /// of a work somebody watched and this record is counted on a page and written into a log.
    /// </summary>
    [Fact]
    public void TheFailureCarriesTheTypeItWasRefusedWithAndNotTheMessage()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_firstFilm, new TimeoutException("D:\\media\\Some Film (2019).mkv"));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        var failure = Assert.Single(answer.Failed);

        Assert.Equal(nameof(TimeoutException), failure.Reason);
        Assert.DoesNotContain("Some Film", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal this plugin has never heard of is a failure of that item like any other. The
    /// server's user data manager is not this plugin's, so a walk that caught the failures
    /// somebody thought of would stop at the first one nobody did.
    /// </summary>
    [Fact]
    public void ARefusalOfAnyTypeFailsThatItemAndNoOther()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_firstFilm, new NotSupportedException("this line does not do that"));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(nameof(NotSupportedException), Assert.Single(answer.Failed).Reason);
        Assert.Equal(_secondFilm, Assert.Single(answer.Applied).ItemId);
    }

    /// <summary>
    /// The reason every write carries. It says the value came from outside this server, which is
    /// what an applied change is, and it is deliberately one an ordinary metadata scan also
    /// produces: nothing may read it back as this plugin's signature.
    /// </summary>
    [Fact]
    public void EveryWriteCarriesTheReasonThatSaysItCameFromElsewhere()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.All(server.Writes, write => Assert.Equal(UserDataSaveReason.Import, write.Reason));
    }

    /// <summary>
    /// #42's fourth condition, refused per apply rather than assumed from the caller. A set that
    /// crosses two people is the worst outcome available to this plugin, and neither the writes
    /// nor the record would look wrong afterwards.
    ///
    /// The assertion is that nothing was written and not only that something was thrown. The
    /// agreed record refuses an agreement about another person on its own, so a walk with no
    /// refusal of its own still throws, and it throws after the write has already landed in
    /// somebody else's account. That is the whole difference between the two guards, and it is
    /// invisible to a case that asserts the exception alone.
    /// </summary>
    [Fact]
    public void ADecidedItemAboutAnotherPersonIsRefusedBeforeAnythingIsWritten()
    {
        var user = UserDataFixtures.Someone();
        var somebodyElse = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        var crossed = new List<ItemToApply>
        {
            Item(somebodyElse, _secondFilm),
            Item(user, _firstFilm),
        };

        Assert.Throws<ArgumentException>(() => ItemByItemApply.Apply(
            user,
            crossed,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// The same crossing from the other end. A record belongs to one person, so a walk handed one
    /// person's record and another person's items would agree a state under a record it is not
    /// about.
    ///
    /// It is refused before the walk begins rather than at the first agreement, for the reason
    /// the case above states: the record's own refusal arrives after a write has already been
    /// made, and a person's history is in another person's account by then.
    /// </summary>
    [Fact]
    public void AnAgreedRecordAboutAnotherPersonIsRefusedBeforeAnythingIsWritten()
    {
        var user = UserDataFixtures.Someone();
        var somebodyElse = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        Assert.Throws<ArgumentException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            server,
            AgreedRecords.NoneYet(_pairing, somebodyElse.Id),
            ProvenanceRecords.NoneYet(_pairing, somebodyElse.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// The pair that has to name one item. A decided state written against a different item is
    /// the failure this plugin exists to refuse, and nothing later in the walk could notice it.
    /// </summary>
    [Fact]
    public void AnItemThatIsNotTheOneTheSubjectNamesIsRefused()
    {
        var user = UserDataFixtures.Someone();

        Assert.Throws<ArgumentException>(() => new ItemToApply(
            Subject(user, _firstFilm),
            UserDataFixtures.Work(_secondFilm, null),
            new SyncedState(true, 2, 0, _watchedAt)));
    }

    /// <summary>
    /// An envelope version below one would be written into every entry this walk agrees, and an
    /// agreement records which version carried it so that a reader asking why a field never moved
    /// has the answer in the record.
    /// </summary>
    [Fact]
    public void AnEnvelopeVersionBelowOneIsRefused()
    {
        var user = UserDataFixtures.Someone();

        Assert.Throws<ArgumentOutOfRangeException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            new RecordedWrites(),
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            0,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None));
    }

    /// <summary>
    /// A cancelled walk stops between two items and never inside one. What it leaves is a smaller
    /// exchange rather than a damaged one: the tail was not tried, so it is in neither list and
    /// carries no failure an operator would read as the peer's.
    /// </summary>
    [Fact]
    public void ACancelledWalkStopsBetweenItemsAndRecordsNoFailureForTheTail()
    {
        var user = UserDataFixtures.Someone();
        using var stopping = new CancellationTokenSource();
        var server = new RecordedWrites();
        server.OnWrite(() => stopping.Cancel());

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            stopping.Token);

        Assert.Equal(_firstFilm, Assert.Single(answer.Applied).ItemId);
        Assert.Empty(answer.Failed);
        Assert.Equal(1, answer.Examined);
    }

    /// <summary>
    /// A write that was cancelled is not a failure of the item and is not recorded as one. It was
    /// asked for by this side, it says nothing about the item or about the peer, and an entry
    /// naming it would put a row on an operator's page for something the operator did.
    ///
    /// It leaves the walk rather than being caught, which is the difference from every other
    /// refusal: a cancellation part-way through a set is the caller's own answer to give, and a
    /// walk that swallowed it would report a run as examined that was stopped.
    /// </summary>
    [Fact]
    public void ACancelledWriteIsNotRecordedAsAFailureOfTheItem()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_firstFilm, new OperationCanceledException());

        Assert.Throws<OperationCanceledException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None));
    }

    /// <summary>
    /// An empty set is a walk that examined nothing, and the record it answers is the one it was
    /// handed. A caller cannot tell an empty walk from one it never made by comparing what it
    /// holds, which is why the count is what says so.
    /// </summary>
    [Fact]
    public void AWalkOverNoItemsAnswersTheRecordItWasHanded()
    {
        var user = UserDataFixtures.Someone();
        var standing = AgreedRecords.NoneYet(_pairing, user.Id);

        var answer = ItemByItemApply.Apply(
            user,
            Array.Empty<ItemToApply>(),
            new RecordedWrites(),
            standing,
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(0, answer.Examined);
        Assert.Same(standing, answer.Agreed);
    }

    /// <summary>
    /// A failure carrying no reason is one an operator can see the count of and nothing else, and
    /// the count alone is what a support thread starts from.
    /// </summary>
    [Fact]
    public void AFailureWithoutAReasonIsRefused()
    {
        var user = UserDataFixtures.Someone();

        Assert.Throws<ArgumentException>(() => new ApplyFailure(Subject(user, _firstFilm), "  "));
    }

    /// <summary>
    /// The third condition of #54. Where the failures have stopped being about the items, the walk
    /// stops rather than working through the rest of the envelope, and the answer says it stopped.
    ///
    /// Five of this person's items are refused and three are written, which is what a mapping
    /// pointing at somebody else's record or a side that has stopped accepting writes looks like
    /// from in here. The stop lands on the eighth attempt rather than on the fifth refusal, because
    /// a share under the floor is arithmetic on too few points, and the ninth item is never tried.
    /// </summary>
    [Fact]
    public void AWalkWhoseFailuresHaveStoppedBeingAboutTheItemsStops()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var films = Films(9);

        foreach (var refused in films.Take(5))
        {
            server.Refuse(refused, new InvalidOperationException("the record is not writable"));
        }

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.True(answer.StoppedOnFailureShare);
        Assert.Equal(8, answer.Examined);
        Assert.Equal(5, answer.Failed.Count);
        Assert.Equal(3, answer.Applied.Count);
        Assert.DoesNotContain(films[8], server.Writes.Select(write => write.ItemId));
        Assert.Null(answer.Agreed.For(films[8]));
    }

    /// <summary>
    /// The other side of the same walk, and the one that costs more to get wrong. Failures at the
    /// share are the ordinary outcome of an exchange, so the walk reaches every item it was handed
    /// and reports that it was not stopped.
    ///
    /// Without this fact a rule that stopped every walk with a refusal in it would pass the one
    /// above, and what it would produce is the all-or-nothing exchange this issue exists against.
    /// </summary>
    [Fact]
    public void AWalkWhoseFailuresAreOrdinaryReachesEveryItem()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var films = Films(9);

        foreach (var refused in films.Take(4))
        {
            server.Refuse(refused, new InvalidOperationException("the library no longer holds it"));
        }

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.False(answer.StoppedOnFailureShare);
        Assert.Equal(9, answer.Examined);
        Assert.NotNull(answer.Agreed.For(films[8]));
    }

    /// <summary>
    /// One refused item out of three does not stop a walk, which is the floor reaching the walk
    /// rather than only the rule.
    ///
    /// It is the smallest envelope somebody actually sends and it is the case a share alone gets
    /// wrong: one of three failing is a third, and a third of nothing much is not evidence of
    /// anything.
    /// </summary>
    [Fact]
    public void AWalkTooShortToJudgeIsNotStopped()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_firstFilm, new InvalidOperationException("the library no longer holds it"));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.False(answer.StoppedOnFailureShare);
        Assert.Equal(3, answer.Examined);
    }

    /// <summary>
    /// A stop is the walk declining to attempt what is left and never a second pass over what it
    /// wrote, which is the unwind rule in <c>docs/transfer.md</c> read at the one route out of this
    /// walk that was added after that rule was written.
    ///
    /// The assertion is on the order of the writes rather than on the state they left, for the
    /// reason the fact about a failure is: a walk that put an item back leaves the same state as
    /// one that never touched it.
    /// </summary>
    [Fact]
    public void NothingWrittenBeforeAStopIsWrittenAgainOnTheWayOut()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var films = Films(9);

        foreach (var refused in films.Take(5))
        {
            server.Refuse(refused, new InvalidOperationException("the record is not writable"));
        }

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.True(answer.StoppedOnFailureShare);
        Assert.Equal(8, server.Writes.Count);
        Assert.Equal(
            films.Take(8).ToList(),
            server.Writes.Select(write => write.ItemId).ToList());
    }

    /// <summary>
    /// A share outside what the rule accepts is refused before the first item is written.
    ///
    /// A walk that wrote three items and then threw for a value it was handed at the start has
    /// already changed somebody's record for a run that was never going to be legal, and the
    /// assertion is that the server saw nothing rather than that something was thrown.
    /// </summary>
    [Fact]
    public void AFailureShareOutsideTheBoundIsRefusedBeforeAnythingIsWritten()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        Assert.Throws<ArgumentOutOfRangeException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm, _secondFilm, _thirdFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.SmallestConfigurableShare - 0.01,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// The share an operator configured is the share the walk stops at, which is the last clause
    /// of #54's third condition.
    ///
    /// The same nine items and the same five refusals as the two facts above, run twice. Under the
    /// default the walk stops on the eighth attempt; with the share raised on the configuration
    /// document the walk finishes all nine, and the ninth item is written rather than skipped. So
    /// the setting decides the outcome rather than sitting on a page, which is the difference
    /// between a configured share and a declared one.
    ///
    /// The value comes through <see cref="ServerWideSettings"/> rather than as a number typed here,
    /// because what the condition asks about is the path from the stored document to the rule, and
    /// a fact handing the walk a literal would pass with nothing in between them.
    /// </summary>
    [Fact]
    public void TheShareAnOperatorConfiguredIsTheShareTheWalkStopsAt()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(9);

        var reading = ServerWideSettings.Read(new PluginConfiguration
        {
            MaximumFailureSharePercent =
                (int)Math.Round(FailureShare.LargestConfigurableShare * 100),
        });

        Assert.True(reading.IsRead);

        var server = new RecordedWrites();

        foreach (var refused in films.Take(5))
        {
            server.Refuse(refused, new InvalidOperationException("the record is not writable"));
        }

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            reading.MaximumFailureShare!.Value,
            _evening,
            CancellationToken.None);

        Assert.False(answer.StoppedOnFailureShare);
        Assert.Equal(9, answer.Examined);
        Assert.Equal(4, answer.Applied.Count);
        Assert.Contains(films[8], server.Writes.Select(write => write.ItemId));
    }

    /// <summary>
    /// The default that the same document carries where nobody has chosen anything stops the same
    /// walk.
    ///
    /// It is the other half of the fact above and it is what says the difference is the setting
    /// rather than anything else about the run. Both readings come out of a configuration
    /// document; only one of them has been changed.
    /// </summary>
    [Fact]
    public void TheShareOnAnUntouchedDocumentStopsTheSameWalk()
    {
        var user = UserDataFixtures.Someone();
        var films = Films(9);

        var reading = ServerWideSettings.Read(new PluginConfiguration());

        Assert.True(reading.IsRead);

        var server = new RecordedWrites();

        foreach (var refused in films.Take(5))
        {
            server.Refuse(refused, new InvalidOperationException("the record is not writable"));
        }

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, films),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            reading.MaximumFailureShare!.Value,
            _evening,
            CancellationToken.None);

        Assert.True(answer.StoppedOnFailureShare);
        Assert.Equal(8, answer.Examined);
        Assert.DoesNotContain(films[8], server.Writes.Select(write => write.ItemId));
    }

    private static Guid[] Films(int count) =>
        Enumerable
            .Range(1, count)
            .Select(index => new Guid($"66666666-0000-0000-0000-{index:D12}"))
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
}

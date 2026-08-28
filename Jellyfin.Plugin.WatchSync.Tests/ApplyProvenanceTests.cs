using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Tests.Apply;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the apply path writes down about the values it replaced, which is #44's first and second
/// conditions.
///
/// The failure the set is written against is a revocation that cannot be carried out. Decision 5
/// on the pairing board is that on revocation what came from the peer is deleted, and a value this
/// plugin wrote is indistinguishable from one the server wrote itself unless something recorded
/// that this plugin wrote it. A walk that wrote without stamping would leave a person's record
/// holding a household's viewing with nothing saying where any of it came from, and the strict
/// answer would be unavailable for writes already made rather than merely unimplemented.
///
/// The whole apply surface is one walk. <c>IUserDataGateway.Write</c> is the one place anything
/// this plugin decides reaches a person's record, and <c>ItemByItemApply</c> is its one caller, so
/// the first condition is a fact over this type rather than a rule somebody follows at each new
/// call site. The invariant that keeps it one place is <c>user-data-behind-the-adapter</c>.
///
/// Nothing here reads a clock. The moment of a write is the moment the walk was handed.
/// </summary>
public class ApplyProvenanceTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _peerUser = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _firstFilm = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _secondFilm = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset _evening = new(2026, 8, 27, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _watchedBefore = new(2026, 8, 20, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// #44's first condition, over the whole apply surface. Every item the walk wrote has an
    /// entry, and every entry names the pairing, the mapped user, the peer user, the item, the
    /// field and the moment.
    ///
    /// The count is per field rather than per item, because an undo puts one value back. Each of
    /// these two items moves all four fields, which is what makes the count checkable rather than
    /// a number read off the answer.
    /// </summary>
    [Fact]
    public void EveryItemTheWalkWroteHasAnEntryForEveryFieldItChanged()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var answer = Walk(user, server, Items(user, _firstFilm, _secondFilm));

        Assert.Equal(2, answer.Applied.Count);
        Assert.Equal(8, answer.Provenance.Count);

        foreach (var subject in answer.Applied)
        {
            var entries = answer.Provenance.All
                .Where(entry => entry.ItemId == subject.ItemId)
                .ToList();

            Assert.Equal(
                Enum.GetValues<SyncedField>().OrderBy(field => field).ToArray(),
                entries.Select(entry => entry.Field).OrderBy(field => field).ToArray());

            Assert.All(entries, entry =>
            {
                Assert.Equal(_pairing, entry.PairingId);
                Assert.Equal(user.Id, entry.MappedUserId);
                Assert.Equal(_peerUser, entry.PeerUserId);
                Assert.Equal(_evening, entry.WrittenAt);
            });
        }
    }

    /// <summary>
    /// #44's second condition. What this server held immediately before each write is in the
    /// record, so the value the peer's answer replaced can be put back.
    ///
    /// The state held here is a person part-way through a film they had watched a week earlier,
    /// and every one of the four fields moves, so a stamp taken from the decided state rather than
    /// from the reading would agree with the assertion on none of them.
    /// </summary>
    [Fact]
    public void TheValueThisServerHeldIsRecoverableForEveryWrite()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Hold(
            _firstFilm,
            new SyncedState(false, 1, TimeSpan.FromMinutes(20).Ticks, _watchedBefore));

        var answer = Walk(user, server, Items(user, _firstFilm));

        Assert.Equal(0, Before(answer, SyncedField.Played));
        Assert.Equal(1, Before(answer, SyncedField.PlayCount));
        Assert.Equal(TimeSpan.FromMinutes(20).Ticks, Before(answer, SyncedField.PlaybackPositionTicks));
        Assert.Equal(_watchedBefore.Ticks, Before(answer, SyncedField.LastPlayedDate));

        Assert.Equal(1, Stamped(answer, SyncedField.Played));
        Assert.Equal(2, Stamped(answer, SyncedField.PlayCount));
        Assert.Equal(0, Stamped(answer, SyncedField.PlaybackPositionTicks));
        Assert.Equal(_watchedAt.Ticks, Stamped(answer, SyncedField.LastPlayedDate));
    }

    /// <summary>
    /// An item this server holds no record for is recorded as holding nothing, and not as holding
    /// the values an unwatched item would carry.
    ///
    /// Nothing and a record saying the person never watched the work are different states, and an
    /// undo that restored the second where the first was true leaves a resume point and a play
    /// count on an item the person has never opened.
    /// </summary>
    [Fact]
    public void AnItemThisServerHeldNothingForIsRecordedAsHoldingNothing()
    {
        var user = UserDataFixtures.Someone();
        var answer = Walk(user, new RecordedWrites(), Items(user, _firstFilm));

        Assert.Null(Before(answer, SyncedField.Played));
        Assert.Null(Before(answer, SyncedField.PlayCount));
        Assert.Null(Before(answer, SyncedField.PlaybackPositionTicks));
        Assert.Null(Before(answer, SyncedField.LastPlayedDate));
    }

    /// <summary>
    /// A write that clears somebody's last played date is recorded, with the absence as what was
    /// written.
    ///
    /// This is the write the record could not hold until #281, and it is the one an undo most
    /// needs: a cleared date is exactly what a peer's answer overwrote, and the record would have
    /// looked complete with every other field of the item in it. The three fields that did not
    /// move are not recorded, which is what says the entry is about the clearing rather than about
    /// the write.
    /// </summary>
    [Fact]
    public void AWriteThatClearsTheLastPlayedDateIsRecordedAsAnAbsence()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Hold(_firstFilm, new SyncedState(true, 2, 0, _watchedBefore));

        var answer = Walk(
            user,
            server,
            new[] { Item(user, _firstFilm, new SyncedState(true, 2, 0, null)) });

        var entry = Assert.Single(answer.Provenance.All);

        Assert.Equal(SyncedField.LastPlayedDate, entry.Field);
        Assert.Equal(_watchedBefore.Ticks, entry.Before);
        Assert.Null(entry.Written);
    }

    /// <summary>
    /// A field the write did not move is not recorded.
    ///
    /// An entry for it would tell a revocation to put back a value this plugin never touched, and
    /// the person who set it would find their own choice reverted by somebody else unpairing.
    /// </summary>
    [Fact]
    public void AFieldTheWriteDidNotMoveIsNotRecorded()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Hold(_firstFilm, new SyncedState(true, 2, 0, _watchedBefore));

        var answer = Walk(
            user,
            server,
            new[] { Item(user, _firstFilm, new SyncedState(true, 2, 0, _watchedAt)) });

        Assert.Equal(SyncedField.LastPlayedDate, Assert.Single(answer.Provenance.All).Field);
    }

    /// <summary>
    /// A write that changed nothing is stamped with nothing, and the walk still agreed the item.
    ///
    /// It is the record's own rule rather than a shortcut here: an entry whose written value is
    /// the value that was already there is refused, because there is nothing for an undo to put
    /// back. So the count of entries is the count of fields that moved and never the count of
    /// writes, and that is the sentence a reader of the first condition has to have.
    /// </summary>
    [Fact]
    public void AWriteThatChangedNothingIsStampedWithNothingAndIsStillAgreed()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Hold(_firstFilm, new SyncedState(true, 2, 0, _watchedAt));

        var answer = Walk(user, server, Items(user, _firstFilm));

        Assert.Single(answer.Applied);
        Assert.Equal(0, answer.Provenance.Count);
        Assert.NotNull(answer.Agreed.For(_firstFilm));
    }

    /// <summary>
    /// A write the server refused leaves no record of provenance, and the item beside it is
    /// stamped as usual.
    ///
    /// The failed item was attempted and did not land, so there is nothing on this server that
    /// came from the peer and nothing for an undo to put back. An entry for it would make a
    /// revocation write a value this plugin never managed to write, over whatever the person holds
    /// now.
    /// </summary>
    [Fact]
    public void AWriteTheServerRefusedIsNotStamped()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.Refuse(_firstFilm, new InvalidOperationException("the library no longer holds it"));

        var answer = Walk(user, server, Items(user, _firstFilm, _secondFilm));

        Assert.Equal(_firstFilm, Assert.Single(answer.Failed).Subject.ItemId);
        Assert.All(answer.Provenance.All, entry => Assert.Equal(_secondFilm, entry.ItemId));
        Assert.Equal(4, answer.Provenance.Count);
    }

    /// <summary>
    /// A read the server refused is a failure of that item rather than something thrown out of the
    /// walk, and nothing is written and nothing is stamped for it.
    ///
    /// The read is what the stamp is taken from, so it happens inside the same attempt as the
    /// write. A walk that let a refused read escape would stop at that item and leave the rest of
    /// somebody's evening unwritten, which is the all-or-nothing exchange #54 refuses, arrived at
    /// through the change that made provenance possible.
    /// </summary>
    [Fact]
    public void AReadTheServerRefusedIsAFailureOfThatItemAndOfNoOther()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        server.RefuseRead(_firstFilm, new InvalidOperationException("the library no longer holds it"));

        var answer = Walk(user, server, Items(user, _firstFilm, _secondFilm));

        var failure = Assert.Single(answer.Failed);

        Assert.Equal(_firstFilm, failure.Subject.ItemId);
        Assert.Equal(nameof(InvalidOperationException), failure.Reason);
        Assert.DoesNotContain(server.Writes, write => write.ItemId == _firstFilm);
        Assert.All(answer.Provenance.All, entry => Assert.Equal(_secondFilm, entry.ItemId));
    }

    /// <summary>
    /// A walk handed a record of provenance about another mapped user is refused before anything
    /// is written.
    ///
    /// It is the same failure the agreed record is refused for, one register over: the writes
    /// would land, the record would be written, and what this plugin did to one person's account
    /// would be filed under another person's.
    ///
    /// That nothing was written is half of what is asserted and it is the half with teeth. The
    /// record refuses the entry itself, so a walk that did not check would still throw, but only
    /// at the first stamp, which is after the first item has landed in somebody's record. The
    /// difference between the two is invisible in the exception and is the whole point of asking
    /// before the walk starts.
    /// </summary>
    [Fact]
    public void ARecordOfProvenanceAboutAnotherUserIsRefusedBeforeAnythingIsWritten()
    {
        var user = UserDataFixtures.Someone();
        var somebodyElse = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        Assert.Throws<ArgumentException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, somebodyElse.Id),
            _peerUser,
            1,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// A walk handed a record of provenance about another pairing is refused before anything is
    /// written.
    ///
    /// An undo is bounded by the pairing that was revoked. An entry filed under the wrong one is
    /// either reverted by a revocation it has nothing to do with, or left standing by the one that
    /// revoked it, and neither is visible in what the walk answers.
    ///
    /// Nothing written is asserted here for the reason given at the case above: the record refuses
    /// the entry on its own, one item too late.
    /// </summary>
    [Fact]
    public void ARecordOfProvenanceAboutAnotherPairingIsRefusedBeforeAnythingIsWritten()
    {
        var user = UserDataFixtures.Someone();
        var anotherPairing = new Guid("66666666-6666-6666-6666-666666666666");
        var server = new RecordedWrites();

        Assert.Throws<ArgumentException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(anotherPairing, user.Id),
            _peerUser,
            1,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// A walk given no peer user is refused before anything is written.
    ///
    /// The record refuses an entry naming nobody for the same reason, and a walk that discovered
    /// it at the first stamp would have written the item and then thrown, leaving a person's
    /// record changed and nothing recorded about it.
    /// </summary>
    [Fact]
    public void AWalkGivenNoPeerUserIsRefusedBeforeAnythingIsWritten()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        Assert.Throws<ArgumentException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            Guid.Empty,
            1,
            _evening,
            CancellationToken.None));

        Assert.Empty(server.Writes);
    }

    /// <summary>
    /// A walk that was handed no record at all is refused rather than stamping into one it made
    /// for itself.
    ///
    /// A record the walk invented would be answered and thrown away by every caller that did not
    /// know to keep it, and what it held would be the only account of writes that had already
    /// landed in somebody's record.
    /// </summary>
    [Fact]
    public void AWalkWithNoRecordOfProvenanceIsRefused()
    {
        var user = UserDataFixtures.Someone();

        Assert.Throws<ArgumentNullException>(() => ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            new RecordedWrites(),
            AgreedRecords.NoneYet(_pairing, user.Id),
            null!,
            _peerUser,
            1,
            _evening,
            CancellationToken.None));
    }

    /// <summary>
    /// A walk over no items answers the record it was handed, so a caller cannot tell a walk that
    /// stamped nothing from one it never made by comparing what the record holds.
    /// </summary>
    [Fact]
    public void AWalkOverNoItemsAnswersTheRecordOfProvenanceItWasHanded()
    {
        var user = UserDataFixtures.Someone();
        var standing = ProvenanceRecords.NoneYet(_pairing, user.Id);

        var answer = ItemByItemApply.Apply(
            user,
            Array.Empty<ItemToApply>(),
            new RecordedWrites(),
            AgreedRecords.NoneYet(_pairing, user.Id),
            standing,
            _peerUser,
            1,
            _evening,
            CancellationToken.None);

        Assert.Same(standing, answer.Provenance);
    }

    /// <summary>
    /// The record the walk answers carries what it was handed as well as what it wrote, so a
    /// second exchange does not lose the first one's account of itself.
    /// </summary>
    [Fact]
    public void TheWalkAddsToTheRecordItWasHandedRatherThanReplacingIt()
    {
        var user = UserDataFixtures.Someone();
        var earlier = ProvenanceRecords.NoneYet(_pairing, user.Id).With(new ProvenanceRecord(
            _pairing,
            user.Id,
            _peerUser,
            _secondFilm,
            SyncedField.PlayCount,
            1,
            2,
            _evening.AddDays(-1)));

        var answer = ItemByItemApply.Apply(
            user,
            Items(user, _firstFilm),
            new RecordedWrites(),
            AgreedRecords.NoneYet(_pairing, user.Id),
            earlier,
            _peerUser,
            1,
            _evening,
            CancellationToken.None);

        Assert.Equal(4, answer.Provenance.Count - earlier.Count);
        Assert.Equal(_secondFilm, answer.Provenance.All[0].ItemId);
    }

    private static ApplyAnswer Walk(
        User user,
        RecordedWrites server,
        IReadOnlyList<ItemToApply> items) =>
        ItemByItemApply.Apply(
            user,
            items,
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            _evening,
            CancellationToken.None);

    private static long? Before(ApplyAnswer answer, SyncedField field) =>
        answer.Provenance.All.Single(entry => entry.Field == field).Before;

    private static long? Stamped(ApplyAnswer answer, SyncedField field) =>
        answer.Provenance.All.Single(entry => entry.Field == field).Written;

    private static TransferSubject Subject(User user, Guid itemId)
    {
        var reading = TransferSubject.From(user.Id, itemId, BaseItemKind.Movie);

        Assert.True(reading.IsSubject);

        return reading.Value!;
    }

    private static ItemToApply Item(User user, Guid itemId, SyncedState decided) =>
        new ItemToApply(
            Subject(user, itemId),
            UserDataFixtures.Work(itemId, TimeSpan.FromHours(2).Ticks),
            decided);

    private static IReadOnlyList<ItemToApply> Items(User user, params Guid[] itemIds) =>
        itemIds
            .Select(itemId => Item(user, itemId, new SyncedState(true, 2, 0, _watchedAt)))
            .ToList();
}

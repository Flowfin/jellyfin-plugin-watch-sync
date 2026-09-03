using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The record of a run the cap stopped, as a document the store keeps, which is the plan #38's
/// second condition asks a stopped run to write and its third condition asks an operator to
/// approve.
///
/// The facts here are about the record and not about a run: that it is one moment's plan and
/// refuses to be anything else, that it names exactly the run that was stopped, and that what is
/// written is what is read back, including the readings of nothing and of unread that the
/// approval turns on. What a run does with it is <c>CappedApplyTests</c>.
/// </summary>
public class StoppedRunTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _film = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _episode = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _other = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _peer = new("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset _evening = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 9, 3, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The document is named for the pairing and the person, so a walk over the store can say
    /// whose plan it is without opening it, and so two people's plans never collide on one name.
    /// </summary>
    [Fact]
    public void TheDocumentIsNamedForThePairingAndThePerson()
    {
        var name = StoppedRun.DocumentName(_pairing, _user);

        Assert.StartsWith("stopped-", name, StringComparison.Ordinal);
        Assert.Contains(_pairing.ToString("n"), name, StringComparison.Ordinal);
        Assert.Contains(_user.ToString("n"), name, StringComparison.Ordinal);
        Assert.NotEqual(name, StoppedRun.DocumentName(_pairing, _other));
    }

    /// <summary>
    /// What is written is what is read back, through the bytes rather than through the object,
    /// because a document the store has just read and one this record has just built are not the
    /// same subject to a reader that asks for one number width only. Three readings of what was
    /// held travel through: a state, nothing, and unread.
    /// </summary>
    [Fact]
    public void AWrittenPlanReadsBackAsItself()
    {
        var written = Plan();

        var reading = StoppedRun.Read(Document(written.ToDocument().ToJson()));

        Assert.False(reading.IsRefused);

        var read = reading.Run!;

        Assert.Equal(_pairing, read.PairingId);
        Assert.Equal(_user, read.MappedUserId);
        Assert.Equal(_peer, read.PeerUserId);
        Assert.Equal(3, read.EnvelopeVersion);
        Assert.Equal(RunCapAnswer.ExceedsShare, read.Answer);
        Assert.Equal(3, read.Changes);
        Assert.Equal(2, read.Allowed);
        Assert.Equal(20, read.Matched);
        Assert.Equal(_evening, read.StoppedAt);
        Assert.Equal(3, read.Items.Count);

        var first = read.Items[0];

        Assert.Equal(_film, first.Subject.ItemId);
        Assert.Equal(BaseItemKind.Movie, first.Subject.Kind);
        Assert.True(first.Decided.Played);
        Assert.Equal(2, first.Decided.PlayCount);
        Assert.Equal(_watchedAt, first.Decided.LastPlayedDate);
        Assert.True(first.HeldWasRead);
        Assert.True(StoppedRunItem.SameReading(new SyncedState(false, 0, 1200, null), first.Held));

        var second = read.Items[1];

        Assert.Equal(_episode, second.Subject.ItemId);
        Assert.Equal(BaseItemKind.Episode, second.Subject.Kind);
        Assert.True(second.HeldWasRead);
        Assert.Null(second.Held);

        var third = read.Items[2];

        Assert.False(third.HeldWasRead);
        Assert.Null(third.Held);
        Assert.Null(third.Decided.LastPlayedDate);
    }

    /// <summary>
    /// A run within the cap is not a stopped run and refuses to be recorded as one, because a
    /// plan for a run that proceeded would be approved into a second write of everything it
    /// already wrote.
    /// </summary>
    [Fact]
    public void ARunTheCapLetThroughIsNotAPlan()
    {
        var within = RunCap.Judge(changes: 1, matched: 100, maximumChanges: 100, maximumShare: 0.10);

        Assert.Throws<ArgumentException>(() => StoppedRun.Of(
            _pairing,
            _user,
            _peer,
            1,
            within,
            100,
            new[] { StoppedRunItem.Read(Subject(_film, BaseItemKind.Movie), Decided(), null) },
            _evening));
    }

    /// <summary>
    /// The plan is the run. A record saying three changes were stopped and listing two of them
    /// would be approved as three, so the count the cap judged and the items listed are held to
    /// each other in both directions.
    /// </summary>
    /// <param name="listed">How many items to list against a verdict of three changes.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void APlanListingOtherThanTheRunIsRefused(int listed)
    {
        var stopped = RunCap.Judge(changes: 3, matched: 20, maximumChanges: 100, maximumShare: 0.10);

        var items = Enumerable
            .Range(1, listed)
            .Select(index => StoppedRunItem.Read(
                Subject(new Guid($"66666666-0000-0000-0000-{index:D12}"), BaseItemKind.Movie),
                Decided(),
                null))
            .ToList();

        Assert.Throws<ArgumentException>(() => StoppedRun.Of(_pairing, _user, _peer, 1, stopped, 20, items, _evening));
    }

    /// <summary>
    /// A plan is one person's. An item about somebody else in it would be approved into that
    /// person's account under this person's record.
    /// </summary>
    [Fact]
    public void AnItemAboutAnotherPersonIsRefused()
    {
        var stopped = RunCap.Judge(changes: 1, matched: 0, maximumChanges: 100, maximumShare: 0.10);
        var theirs = TransferSubject.From(_other, _film, BaseItemKind.Movie).Value!;

        Assert.Throws<ArgumentException>(() => StoppedRun.Of(
            _pairing,
            _user,
            _peer,
            1,
            stopped,
            0,
            new[] { StoppedRunItem.Read(theirs, Decided(), null) },
            _evening));
    }

    /// <summary>
    /// An empty identifier names nobody and no pairing, and a plan about nobody is one a walk
    /// over the store would file under every empty name.
    /// </summary>
    [Fact]
    public void AnEmptyIdentifierIsRefused()
    {
        var stopped = RunCap.Judge(changes: 0, matched: 0, maximumChanges: 100, maximumShare: 0.10);

        // Zero changes against nothing matched is within the cap, so the refusal below has to be
        // the identifier's and not the verdict's; the verdict is stopped by the share instead.
        Assert.Equal(RunCapAnswer.Within, stopped.Answer);

        var share = RunCap.Judge(changes: 1, matched: 0, maximumChanges: 100, maximumShare: 0.10);
        var items = new[] { StoppedRunItem.Read(Subject(_film, BaseItemKind.Movie), Decided(), null) };

        Assert.Throws<ArgumentException>(() => StoppedRun.Of(Guid.Empty, _user, _peer, 1, share, 0, items, _evening));
        Assert.Throws<ArgumentException>(() => StoppedRun.Of(_pairing, Guid.Empty, _peer, 1, share, 0, items, _evening));
        Assert.Throws<ArgumentException>(() => StoppedRun.Of(_pairing, _user, Guid.Empty, 1, share, 0, items, _evening));
    }

    /// <summary>
    /// The envelope version the plan carries is written into every agreement an approval
    /// records, so a version below one is refused at the plan rather than at the approval.
    /// </summary>
    [Fact]
    public void AnEnvelopeVersionBelowOneIsRefused()
    {
        var share = RunCap.Judge(changes: 1, matched: 0, maximumChanges: 100, maximumShare: 0.10);
        var items = new[] { StoppedRunItem.Read(Subject(_film, BaseItemKind.Movie), Decided(), null) };

        Assert.Throws<ArgumentOutOfRangeException>(() => StoppedRun.Of(_pairing, _user, _peer, 0, share, 0, items, _evening));
    }

    /// <summary>
    /// A decided state no conflict rule produces is refused at the item, so a plan cannot carry
    /// it into an approval.
    /// </summary>
    [Fact]
    public void ADecidedStateBelowZeroIsRefused()
    {
        var subject = Subject(_film, BaseItemKind.Movie);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StoppedRunItem.Read(subject, new SyncedState(true, -1, 0, null), null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StoppedRunItem.Unread(subject, new SyncedState(true, 0, -1, null)));
    }

    /// <summary>
    /// A document that is not a stopped run is refused whole rather than read in part, because a
    /// plan approved from the items that happened to parse would leave an operator believing the
    /// rest were written too. Each case is one member away from a document this record writes.
    /// </summary>
    /// <param name="damage">What to do to a document this record wrote.</param>
    [Theory]
    [MemberData(nameof(Damages))]
    public void ADocumentThatIsNotAStoppedRunIsRefusedWhole(string damage)
    {
        var members = JsonNode.Parse(Plan().ToDocument().ToJson())!.AsObject();

        Damage(members, damage);

        var reading = StoppedRun.Read(Document(members.ToJsonString()));

        Assert.True(reading.IsRefused);
        Assert.Equal(StoppedRunAnswer.NotAStoppedRun, reading.Answer);
        Assert.Null(reading.Run);
    }

    /// <summary>
    /// The damages the theory above walks, named so a failure says which one.
    /// </summary>
    /// <returns>One name per damage.</returns>
    public static IEnumerable<object[]> Damages() => new[]
    {
        new object[] { "no-pairing" },
        new object[] { "no-peer-user" },
        new object[] { "envelope-version-zero" },
        new object[] { "answer-within" },
        new object[] { "answer-by-number" },
        new object[] { "changes-disagree-with-items" },
        new object[] { "items-not-a-list" },
        new object[] { "a-state-beside-unread" },
        new object[] { "no-held-member" },
        new object[] { "no-decided" },
        new object[] { "an-aggregate-kind" },
        new object[] { "a-negative-count" },
    };

    private static void Damage(JsonObject members, string damage)
    {
        var items = members["items"]!.AsArray();
        var first = items[0]!.AsObject();
        var third = items[2]!.AsObject();

        switch (damage)
        {
            case "no-pairing":
                members.Remove("pairing");
                break;
            case "no-peer-user":
                members.Remove("peerUser");
                break;
            case "envelope-version-zero":
                members["envelopeVersion"] = 0;
                break;
            case "answer-within":
                members["answer"] = "Within";
                break;
            case "answer-by-number":
                members["answer"] = "2";
                break;
            case "changes-disagree-with-items":
                members["changes"] = 2;
                break;
            case "items-not-a-list":
                members["items"] = new JsonObject();
                break;
            case "a-state-beside-unread":
                third["held"] = new JsonObject
                {
                    ["played"] = false,
                    ["playCount"] = 0,
                    ["positionTicks"] = 0,
                    ["lastPlayed"] = null,
                };
                break;
            case "no-held-member":
                first.Remove("held");
                break;
            case "no-decided":
                first.Remove("decided");
                break;
            case "an-aggregate-kind":
                first["kind"] = "Series";
                break;
            case "a-negative-count":
                first["decided"]!.AsObject()["playCount"] = -1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(damage), damage, "This damage has no arm.");
        }
    }

    private static StoppedRun Plan()
    {
        var verdict = RunCap.Judge(changes: 3, matched: 20, maximumChanges: 100, maximumShare: 0.10);

        Assert.Equal(RunCapAnswer.ExceedsShare, verdict.Answer);

        return StoppedRun.Of(
            _pairing,
            _user,
            _peer,
            3,
            verdict,
            20,
            new[]
            {
                StoppedRunItem.Read(
                    Subject(_film, BaseItemKind.Movie),
                    Decided(),
                    new SyncedState(false, 0, 1200, null)),
                StoppedRunItem.Read(Subject(_episode, BaseItemKind.Episode), Decided(), null),
                StoppedRunItem.Unread(
                    Subject(_other, BaseItemKind.Movie),
                    new SyncedState(false, 0, 600, null)),
            },
            _evening);
    }

    private static SyncedState Decided() => new SyncedState(true, 2, 0, _watchedAt);

    private static TransferSubject Subject(Guid itemId, BaseItemKind kind)
    {
        var reading = TransferSubject.From(_user, itemId, kind);

        Assert.True(reading.IsSubject);

        return reading.Value!;
    }

    private static StoredDocument Document(string json) =>
        StoredDocument.Read(json, DocumentVersions.Current).Document!;
}

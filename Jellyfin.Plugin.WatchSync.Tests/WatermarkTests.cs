using System;
using System.IO;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The point up to which two servers have agreed, which is #51.
///
/// Three properties are what this set is written against. The point advances when the far side
/// confirms it and at no other ending, which is the first condition and is the failure this
/// issue leads with. A point the peer does not recognise is a full reconciliation rather than a
/// refusal, which is the third condition and is what a peer restored from a backup looks like
/// from this end. And the point is written where the agreements are written, so a restart and a
/// restore of the store both bring back the same one, which is the fourth.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class WatermarkTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _programData;
    private readonly TemporaryDirectory _backup;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatermarkTests"/> class, with two directories
    /// of its own: one standing in for what a server hands over, and one for what a restore of
    /// that server's data folder onto another machine produces.
    /// </summary>
    public WatermarkTests()
    {
        _programData = TemporaryDirectory.Create("watermark");
        _backup = TemporaryDirectory.Create("watermark-restored");
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(RestoredDataPath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _programData.Dispose();
        _backup.Dispose();
    }

    /// <summary>
    /// A pairing and a mapped user that have confirmed nothing ask for everything.
    ///
    /// The state is a value rather than an absence, so a caller that forgot the case reaches a
    /// rule holding a watermark that says what to do instead of holding nothing at all.
    /// </summary>
    [Fact]
    public void APairingThatHasConfirmedNothingAsksForEverything()
    {
        Assert.True(Watermark.NoneYet.IsNoneYet);
        Assert.Equal(NextExchange.FullReconciliation, Watermark.NoneYet.Asks);
        Assert.Equal(string.Empty, Watermark.NoneYet.Point);
    }

    /// <summary>
    /// The first condition of #51. A send that the far side did not confirm leaves the point
    /// where it was, and every ending but a confirmation is that ending.
    ///
    /// This is the failure the issue leads with and it is silent in the direction that costs
    /// most: a point moved on a send loses every change between the old point and the new one,
    /// and the next exchange asks from after them, so neither side ever mentions them again.
    /// The refusal, the timeout, the envelope refused for its version and the run that stopped
    /// part way are one value here because <c>docs/transfer.md</c> gives them one answer.
    /// </summary>
    [Fact]
    public void ASendThatWasNotConfirmedLeavesTheWatermarkWhereItWas()
    {
        var held = Confirmed("page-4", _evening);

        var after = held.After(ExchangeEnd.NotConfirmed, Confirmed("page-9", _evening.AddHours(1)));

        Assert.True(after.IsAt("page-4"));
        Assert.Equal(_evening, after.ConfirmedAt);
        Assert.Equal(NextExchange.SinceTheWatermark, after.Asks);
    }

    /// <summary>
    /// A confirmation is the one ending that moves the point, and it moves it to the point the
    /// answer named rather than to anything this server computed.
    /// </summary>
    [Fact]
    public void AConfirmedExchangeStandsAtThePointTheAnswerNamed()
    {
        var after = Confirmed("page-4", _evening)
            .After(ExchangeEnd.ConfirmedTo, Confirmed("page-9", _evening.AddHours(1)));

        Assert.True(after.IsAt("page-9"));
        Assert.Equal(_evening.AddHours(1), after.ConfirmedAt);
    }

    /// <summary>
    /// An ending that says it confirmed a point and names none is refused rather than answered.
    ///
    /// Answering it by leaving the watermark where it was would make an exchange that confirmed
    /// nothing indistinguishable from one that did, which is the first condition defeated by the
    /// type that exists to hold it.
    /// </summary>
    [Fact]
    public void AnEndingThatConfirmsAndNamesNoPointIsRefused()
    {
        var held = Confirmed("page-4", _evening);

        Assert.Throws<ArgumentException>(
            () => held.After(ExchangeEnd.ConfirmedTo, Watermark.NoneYet));
    }

    /// <summary>
    /// The third condition of #51. A point the peer does not recognise, which is what a peer
    /// restored from a backup looks like from this end, produces the full reconciliation in #52
    /// rather than a refusal.
    ///
    /// It is asserted as a state and not as an exception on purpose. A rule that threw here
    /// would make a peer's ordinary recovery an error a caller has to catch, and the caller that
    /// catches it is the one that decides what to do about it, which is exactly the decision
    /// this type exists to take once.
    /// </summary>
    [Fact]
    public void APointThePeerDoesNotRecogniseIsAFullReconciliationAndNotARefusal()
    {
        var after = Confirmed("page-4", _evening)
            .After(ExchangeEnd.PointNotRecognised, Watermark.NoneYet);

        Assert.True(after.IsNoneYet);
        Assert.Equal(NextExchange.FullReconciliation, after.Asks);
        Assert.False(after.IsAt("page-4"));
    }

    /// <summary>
    /// The point is compared as the far side wrote it and never normalised on the way.
    ///
    /// The far side is the only thing that knows what its points mean, so two that differ by
    /// case, by surrounding space or by a Unicode normal form are two points here whatever they
    /// would be to a reader. A comparison that folded any of them would resume from a point the
    /// peer never issued, and would do it only on the peers whose points happen to carry one.
    /// </summary>
    [Theory]
    [InlineData("PAGE-4")]
    [InlineData("page-4 ")]
    [InlineData(" page-4")]
    [InlineData("page-04")]
    public void APointIsComparedAsItWasWrittenAndNeverNormalised(string other)
    {
        var held = Confirmed("page-4", _evening);

        Assert.True(held.IsAt("page-4"));
        Assert.False(held.IsAt(other));
    }

    /// <summary>
    /// A record that has confirmed nothing stands at no point, so it is not at the empty string
    /// either. A caller comparing against what it last offered would otherwise be told yes by a
    /// record that has agreed nothing at all.
    /// </summary>
    [Fact]
    public void ARecordThatHasConfirmedNothingStandsAtNoPointAtAll()
    {
        Assert.False(Watermark.NoneYet.IsAt(string.Empty));
        Assert.False(Watermark.NoneYet.IsAt(null));
    }

    /// <summary>
    /// An answer that named no point is refused rather than read as a record that has confirmed
    /// nothing. The two are different exchanges and only one of them is a state to resume from.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AnAnswerThatNamedNoPointIsRefusedRatherThanReadAsNoneYet(string? point)
    {
        var reading = Watermark.Confirmed(point, _evening);

        Assert.True(reading.IsRefused);
        Assert.Equal(WatermarkAnswer.NoPointAtAll, reading.Answer);
        Assert.Null(reading.Mark);
    }

    /// <summary>
    /// The point is bounded, at the bound every string in an envelope is held to, because that is
    /// where it arrives from and a second bound would be a second answer to one question.
    ///
    /// Both sides of the boundary, so a bound that refused everything would fail here rather than
    /// passing as a cautious one.
    /// </summary>
    [Fact]
    public void APointLongerThanAnEnvelopeStringIsRefusedAndOneAtTheBoundIsNot()
    {
        var atTheBound = new string('p', EnvelopeBounds.LongestStringLength);
        var oneOver = new string('p', EnvelopeBounds.LongestStringLength + 1);

        Assert.False(Watermark.Confirmed(atTheBound, _evening).IsRefused);

        var refused = Watermark.Confirmed(oneOver, _evening);

        Assert.True(refused.IsRefused);
        Assert.Equal(WatermarkAnswer.TooLong, refused.Answer);
    }

    /// <summary>
    /// The bound is counted as a reader sees the characters rather than in the units the runtime
    /// stores them as, so a peer whose points are written in characters outside the basic plane
    /// is bounded at the same length as one whose points are Latin letters rather than at half of
    /// it.
    /// </summary>
    [Fact]
    public void TheBoundIsCountedAsAReaderSeesTheCharacters()
    {
        var astral = string.Concat(
            System.Linq.Enumerable.Repeat("\U0001F600", EnvelopeBounds.LongestStringLength));

        Assert.False(Watermark.Confirmed(astral, _evening).IsRefused);
    }

    /// <summary>
    /// A point carrying anything that is not printable text is refused rather than stripped.
    ///
    /// This is the one place a peer value is refused instead of being bounded and cleaned, and
    /// the difference is what the value is for. A peer name with an invisible character taken out
    /// of it is still that name; a point with one character taken out of it is a different point,
    /// which the peer will not recognise, and the exchange that offered it would ask for a full
    /// reconciliation every time for a reason nobody could see.
    /// </summary>
    [Theory]
    [InlineData("page\n4")]
    [InlineData("page\r4")]
    [InlineData("page\u00074")]
    [InlineData("page\u200E4")]
    [InlineData("page\u202E4")]
    [InlineData("page\u20284")]
    [InlineData("page\u20294")]
    public void APointThatIsNotPlainTextIsRefusedRatherThanStripped(string point)
    {
        var reading = Watermark.Confirmed(point, _evening);

        Assert.True(reading.IsRefused);
        Assert.Equal(WatermarkAnswer.NotPlainText, reading.Answer);
    }

    /// <summary>
    /// The fourth condition of #51, in its first half. A restart is the record being read back
    /// out of the store rather than out of memory, and the point comes back with the agreements
    /// it was written beside.
    /// </summary>
    [Fact]
    public void TheWatermarkSurvivesARestart()
    {
        var name = AgreedRecords.DocumentName(_pairing, _user);
        var store = new DocumentStore(Folder(DataPath));

        store.Write(name, _ => Agreed().At(Confirmed("page-4", _evening)).ToDocument());

        var read = AgreedRecords.Read(new DocumentStore(Folder(DataPath)).Read(name)!.Document!);

        Assert.False(read.IsRefused);
        Assert.True(read.Records!.Watermark.IsAt("page-4"));
        Assert.Equal(_evening, read.Records!.Watermark.ConfirmedAt);
        Assert.Equal(1, read.Records!.Count);
    }

    /// <summary>
    /// The fourth condition of #51, in its second half, and the reason the point is stored with
    /// the agreements rather than beside them.
    ///
    /// A restore is the store's own bytes read from somewhere else. Restoring the agreements
    /// restores the point they were agreed at, which is what makes the two impossible to get out
    /// of step: a store restored with a point later than its agreements would ask a peer for
    /// changes after items it never received, and every item in between would be one neither
    /// side mentions again.
    /// </summary>
    [Fact]
    public void TheWatermarkIsRestoredWithTheAgreementsItWasWrittenBeside()
    {
        var name = AgreedRecords.DocumentName(_pairing, _user);
        var folder = Folder(DataPath);

        new DocumentStore(folder)
            .Write(name, _ => Agreed().At(Confirmed("page-4", _evening)).ToDocument());

        var restored = new StoreFolder(PathsFor(RestoredDataPath)).CreateIfAbsent();

        foreach (var file in Directory.GetFiles(folder.CreateIfAbsent()))
        {
            File.Copy(file, Path.Combine(restored, Path.GetFileName(file)));
        }

        var read = AgreedRecords.Read(
            new DocumentStore(Folder(RestoredDataPath)).Read(name)!.Document!);

        Assert.False(read.IsRefused);
        Assert.True(read.Records!.Watermark.IsAt("page-4"));
        Assert.NotNull(read.Records!.For(_film));
    }

    /// <summary>
    /// A record written before there was a watermark reads as one that has confirmed nothing.
    ///
    /// The member is absent rather than empty in that case, and the two states this plugin can
    /// be in when it meets one are the same state: a pairing that has never exchanged, and a
    /// document written by a version that had no point to write. Both ask for everything, which
    /// is the conservative answer and the only one that is true of both.
    /// </summary>
    [Fact]
    public void ARecordWrittenBeforeThereWasAWatermarkAsksForEverything()
    {
        var document = Agreed().ToDocument();

        Assert.False(document.Fields.ContainsKey("watermark"));

        var read = AgreedRecords.Read(document);

        Assert.False(read.IsRefused);
        Assert.True(read.Records!.Watermark.IsNoneYet);
        Assert.Equal(NextExchange.FullReconciliation, read.Records!.Watermark.Asks);
    }

    /// <summary>
    /// A watermark member that is present and is not one refuses the whole document, by the same
    /// rule an unreadable entry does.
    ///
    /// Reading it as a record that has confirmed nothing would be the expensive repair. That
    /// answer is a full reconciliation over a library two servers had already settled, taken
    /// silently, on a document somebody or something damaged.
    /// </summary>
    [Theory]
    [InlineData("point")]
    [InlineData("confirmedAt")]
    public void ADocumentWhoseWatermarkIsMissingAMemberIsNotAnAgreedRecord(string member)
    {
        var document = Agreed().At(Confirmed("page-4", _evening)).ToDocument();

        ((System.Text.Json.Nodes.JsonObject)document.Fields["watermark"]!)
            .Remove(member);

        Assert.True(AgreedRecords.Read(document).IsRefused);
    }

    /// <summary>
    /// A point that this code would refuse from a peer is refused when it comes back off the
    /// disk as well, because a document is an input and the store is not a place a value becomes
    /// trusted by having been written there.
    /// </summary>
    [Fact]
    public void ADocumentWhoseWatermarkIsNotPlainTextIsNotAnAgreedRecord()
    {
        var document = Agreed().At(Confirmed("page-4", _evening)).ToDocument();

        ((System.Text.Json.Nodes.JsonObject)document.Fields["watermark"]!)[
            "point"] =
            System.Text.Json.Nodes.JsonValue.Create("page\u202E4");

        Assert.True(AgreedRecords.Read(document).IsRefused);
    }

    /// <summary>
    /// Agreeing an item leaves the point where it was.
    ///
    /// The two move on different events, and one write carries both. An agreement that advanced
    /// the point would move it in the middle of an exchange rather than at the end of one, which
    /// is the first condition broken through the collection instead of through the rule.
    /// </summary>
    [Fact]
    public void AgreeingAnItemLeavesTheWatermarkWhereItWas()
    {
        var standing = Agreed().At(Confirmed("page-4", _evening));

        var afterOneMore = standing.With(
            new AgreedRecord(
                TransferSubject.From(
                    _user,
                    new Guid("66666666-6666-6666-6666-666666666666"),
                    BaseItemKind.Episode).Value!,
                new SyncedState(false, 0, 42, null),
                _evening.AddHours(1),
                1));

        Assert.True(afterOneMore.Watermark.IsAt("page-4"));
        Assert.Equal(2, afterOneMore.Count);
    }

    /// <summary>
    /// Standing at a point replaces whichever one the record stood at and keeps the agreements.
    /// </summary>
    [Fact]
    public void StandingAtAPointKeepsTheAgreementsAndReplacesTheOldPoint()
    {
        var moved = Agreed()
            .At(Confirmed("page-4", _evening))
            .At(Confirmed("page-9", _evening.AddHours(1)));

        Assert.True(moved.Watermark.IsAt("page-9"));
        Assert.Equal(1, moved.Count);
        Assert.NotNull(moved.For(_film));
    }

    private static Watermark Confirmed(string point, DateTimeOffset at) =>
        Watermark.Confirmed(point, at).Mark!;

    private static AgreedRecords Agreed() =>
        AgreedRecords.NoneYet(_pairing, _user)
            .With(new AgreedRecord(
                TransferSubject.From(_user, _film, BaseItemKind.Movie).Value!,
                new SyncedState(true, 2, 0, new DateTime(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc)),
                _evening,
                1));

    private static IApplicationPaths PathsFor(string dataPath)
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(dataPath);

        return paths.Object;
    }

    private static StoreFolder Folder(string dataPath) => new StoreFolder(PathsFor(dataPath));

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private string RestoredDataPath => Path.Join(_backup.FullPath, "data");
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The record of what this server and a peer last agreed, which is #14.
///
/// Two properties are what this set is written against. The record is bounded by the number of
/// matched items and never by the number of playback events, which is the fourth condition and
/// is the difference between a record and a history of an evening. And a document read back out
/// of the store is the record that was written or nothing, never a subset of it, because an
/// agreement missing from a record is a first exchange for that item rather than an absence
/// somebody notices.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class AgreedRecordsTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _otherUser = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _episode = new("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgreedRecordsTests"/> class, with a directory
    /// of its own standing in for what a server would hand over.
    /// </summary>
    public AgreedRecordsTests()
    {
        _programData = TemporaryDirectory.Create("agreed");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The fifth condition of #14, from the side that reads. An item nothing has been agreed
    /// about answers with nothing rather than with an agreement holding a never-watched state,
    /// because the two say different things about intent and #34 turns on which one a deliberate
    /// unplayed sits behind.
    /// </summary>
    [Fact]
    public void AnItemNothingHasBeenAgreedAboutHasNoAgreement()
    {
        Assert.Null(AgreedRecords.NoneYet(_pairing, _user).For(_film));
    }

    /// <summary>
    /// The fourth condition of #14. One playback is a start, a stream of progress reports and a
    /// finish, and every one of them can be agreed; the record is the size of the work agreed
    /// about rather than the size of the evening.
    ///
    /// The two runs agree the same item at two rates, so what is asserted is that the count does
    /// not follow the number of agreements rather than that it is small on one fixture.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public void AgreeingOneItemAgainAddsNoRow(int secondsBetweenReports)
    {
        var records = AgreedRecords.NoneYet(_pairing, _user);
        var reports = (2 * 60 * 60) / secondsBetweenReports;

        for (var report = 1; report <= reports; report++)
        {
            records = records.With(Agreement(
                _film,
                new SyncedState(false, 0, report * TimeSpan.TicksPerSecond, null),
                _evening.AddSeconds((double)report * secondsBetweenReports)));
        }

        Assert.Equal(1, records.Count);
        Assert.Equal(
            reports * TimeSpan.TicksPerSecond,
            records.For(_film)!.Agreed.PlaybackPositionTicks);
    }

    /// <summary>
    /// The other half of the same condition. The bound is the number of matched items, so the
    /// record does grow with them, and a test that only asserted the collapse would pass over a
    /// record that kept nothing at all.
    /// </summary>
    [Fact]
    public void TheRecordGrowsWithTheItemsItHasAgreed()
    {
        var records = AgreedRecords.NoneYet(_pairing, _user);

        for (var item = 1; item <= 500; item++)
        {
            records = records.With(Agreement(
                new Guid(item, 0, 0, new byte[8]),
                Watched,
                _evening));
        }

        Assert.Equal(500, records.Count);
    }

    /// <summary>
    /// The first condition of #14, over the store the eighth milestone gives this plugin. What is
    /// written is read back as what it was, through the real write path rather than through a
    /// serializer of the test's own.
    ///
    /// Both spellings of the moment a person last watched are in it, because null is the one
    /// member of an agreement that may be absent and a round trip that only carried a value
    /// would say nothing about the case an unwatched item is in.
    /// </summary>
    [Fact]
    public void AWrittenRecordIsReadBackAsTheRecordItWas()
    {
        var store = new DocumentStore(Folder());
        var name = AgreedRecords.DocumentName(_pairing, _user);

        var written = AgreedRecords.NoneYet(_pairing, _user)
            .With(Agreement(_film, Watched, _evening))
            .With(Agreement(
                _episode,
                new SyncedState(false, 0, 42, null),
                _evening.AddHours(1),
                BaseItemKind.Episode));

        store.Write(name, _ => written.ToDocument());

        var reading = AgreedRecords.Read(store.Read(name)!.Document!);

        Assert.False(reading.IsRefused);

        var read = reading.Records!;

        Assert.Equal(_pairing, read.PairingId);
        Assert.Equal(_user, read.MappedUserId);
        Assert.Equal(2, read.Count);

        var film = read.For(_film)!;

        Assert.Equal(BaseItemKind.Movie, film.Subject.Kind);
        Assert.True(film.Agreed.Played);
        Assert.Equal(2, film.Agreed.PlayCount);
        Assert.Equal(Watched.LastPlayedDate, film.Agreed.LastPlayedDate);
        Assert.Equal(_evening, film.AgreedAt);
        Assert.Equal(1, film.EnvelopeVersion);

        var episode = read.For(_episode)!;

        Assert.Equal(BaseItemKind.Episode, episode.Subject.Kind);
        Assert.Null(episode.Agreed.LastPlayedDate);
        Assert.Equal(42, episode.Agreed.PlaybackPositionTicks);
    }

    /// <summary>
    /// The name is derived from what the record is about, and the store composes a path out of it
    /// rather than out of anything a caller chose. A name the store refuses would be a record
    /// that cannot be written at all, and it would be found by an operator rather than here.
    /// </summary>
    [Fact]
    public void TheNameIsMadeOfThePairingAndTheUserAndTheStoreAcceptsIt()
    {
        var name = AgreedRecords.DocumentName(_pairing, _user);

        Assert.Matches(new Regex("^[a-z0-9-]+$", RegexOptions.None, TimeSpan.FromSeconds(1)), name);
        Assert.StartsWith("agreed-", name, StringComparison.Ordinal);
        Assert.NotEqual(name, AgreedRecords.DocumentName(_otherPairing, _user));
        Assert.NotEqual(name, AgreedRecords.DocumentName(_pairing, _otherUser));

        var answer = new DocumentStore(Folder())
            .Write(name, _ => AgreedRecords.NoneYet(_pairing, _user).ToDocument());

        Assert.Equal(DocumentWriteOutcome.Written, answer.Outcome);
    }

    /// <summary>
    /// One entry that is not an agreement refuses the whole document rather than being dropped
    /// out of it.
    ///
    /// This is the direction that costs something and is worth what it costs. A record read
    /// without one of its entries reports that nothing was agreed about that item, which is a
    /// first exchange, and a first exchange is the run allowed to change the most. So a damaged
    /// document is refused and rebuilt rather than partly believed.
    /// </summary>
    [Theory]
    [InlineData("played")]
    [InlineData("playCount")]
    [InlineData("positionTicks")]
    [InlineData("lastPlayed")]
    [InlineData("agreedAt")]
    [InlineData("envelopeVersion")]
    [InlineData("kind")]
    public void ADocumentMissingOneMemberOfOneEntryIsNotAnAgreedRecord(string member)
    {
        var document = AgreedRecords.NoneYet(_pairing, _user)
            .With(Agreement(_film, Watched, _evening))
            .With(Agreement(_episode, Watched, _evening, BaseItemKind.Episode))
            .ToDocument();

        var entry = (JsonObject)document.Fields["items"]![
            _episode.ToString("n", CultureInfo.InvariantCulture)]!;

        entry.Remove(member);

        var reading = AgreedRecords.Read(document);

        Assert.True(reading.IsRefused);
        Assert.Equal(AgreedRecordsAnswer.NotAnAgreedRecord, reading.Answer);
        Assert.Null(reading.Records);
    }

    /// <summary>
    /// The kind goes back through the same rule that refuses one on the way out, so a document
    /// naming a series is not a way past the refusal that keeps an aggregate out of a transfer.
    ///
    /// The failure it stands against is the one in the prior art: a carried series-played has
    /// nowhere to land, so applying it means marking every episode the peer holds under that
    /// series, and one watched series becomes a library of history nobody made.
    /// </summary>
    [Theory]
    [InlineData("Series")]
    [InlineData("Season")]
    [InlineData("Folder")]
    [InlineData("movie")]
    public void ADocumentNamingAKindThatIsNotALeafItemIsNotAnAgreedRecord(string kind) =>
        Assert.True(AgreedRecords.Read(WithKind(kind)).IsRefused);

    /// <summary>
    /// A kind written as the number it happens to sit at is refused, and this is the near-miss
    /// the rule is really for: the parse accepts a number, and thirteen is what a movie is today.
    ///
    /// What it stands against is a document that keeps meaning whatever that position is on the
    /// day it is read. The enumeration is the server's rather than this plugin's, so a member
    /// inserted upstream moves every number after it, and an agreed record that survived the
    /// upgrade would then be about a different kind of thing without a byte of it changing.
    /// </summary>
    [Fact]
    public void ADocumentNamingAKindByItsNumberIsNotAnAgreedRecord()
    {
        Assert.Equal(
            BaseItemKind.Movie,
            (BaseItemKind)13);

        Assert.True(AgreedRecords.Read(WithKind("13")).IsRefused);
    }

    /// <summary>
    /// A document that is not this record at all, which is what a store holding somebody else's
    /// file looks like from here.
    /// </summary>
    [Fact]
    public void ADocumentWithoutThePairingTheUserOrTheItemsIsNotAnAgreedRecord()
    {
        var whole = AgreedRecords.NoneYet(_pairing, _user).ToDocument().Fields;

        foreach (var member in new[] { "pairing", "user", "items" })
        {
            var fields = (JsonObject)whole.DeepClone();

            fields.Remove(member);

            Assert.True(AgreedRecords.Read(Rebuilt(fields)).IsRefused);
        }
    }

    /// <summary>
    /// A record is one user's. An agreement about another one has no entry here it could replace
    /// and would be readable afterwards under a user it was never about, which is one person's
    /// history appearing in another person's account through the record rather than through the
    /// mapping #42 refuses inferring.
    /// </summary>
    [Fact]
    public void AnAgreementAboutAnotherMappedUserIsRefused()
    {
        var records = AgreedRecords.NoneYet(_pairing, _user);
        var elsewhere = new AgreedRecord(
            Subject(_otherUser, _film, BaseItemKind.Movie),
            Watched,
            _evening,
            1);

        Assert.Throws<ArgumentException>(() => records.With(elsewhere));
    }

    /// <summary>
    /// An agreement carries a version of the envelope that reached it, and a whole number above
    /// zero is what a version is. Nothing agreed under version zero was ever carried by anything.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnAgreementUnderAVersionNothingCarriedIsRefused(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgreedRecord(
            Subject(_user, _film, BaseItemKind.Movie),
            Watched,
            _evening,
            version));
    }

    /// <summary>
    /// A record is about one pairing and one mapped user, and an empty identifier is a caller
    /// that asked about nothing rather than about everything.
    /// </summary>
    [Fact]
    public void ARecordOfNoPairingOrNoUserIsRefused()
    {
        Assert.Throws<ArgumentException>(() => AgreedRecords.NoneYet(Guid.Empty, _user));
        Assert.Throws<ArgumentException>(() => AgreedRecords.NoneYet(_pairing, Guid.Empty));
    }

    /// <summary>
    /// The members put back through the only route from bytes to a document, because a document
    /// assembled any other way is not one this store could ever have held.
    /// </summary>
    /// <param name="fields">The members beside the version.</param>
    /// <returns>The document.</returns>
    private static StoredDocument Rebuilt(JsonObject fields)
    {
        var members = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
        };

        foreach (var pair in fields)
        {
            members[pair.Key] = pair.Value?.DeepClone();
        }

        return StoredDocument.Read(members.ToJsonString(), DocumentVersions.Current).Document!;
    }


    /// <summary>
    /// A record read straight out of the document it just built, without the bytes in between.
    ///
    /// This is the case a suite that only ever reads what the store handed back would never
    /// visit, and it was wrong when it was first written. A number that has been parsed out of
    /// bytes converts between widths and one assembled in memory does not, so the reader saw a
    /// whole record as unreadable while every fact about the store stayed green and the document
    /// on disk was byte for byte the same. What that would cost a caller is the worst answer this
    /// type can give: a record that is there and reads as nothing agreed, which is a first
    /// exchange over a library two servers had already settled.
    /// </summary>
    [Fact]
    public void ARecordIsReadableOutOfTheDocumentItBuiltAsWellAsOutOfTheStore()
    {
        var written = AgreedRecords.NoneYet(_pairing, _user)
            .With(Agreement(_film, Watched, _evening));

        var reading = AgreedRecords.Read(written.ToDocument());

        Assert.False(reading.IsRefused);
        Assert.Equal(2, reading.Records!.For(_film)!.Agreed.PlayCount);
        Assert.Equal(1, reading.Records!.For(_film)!.EnvelopeVersion);
    }

    /// <summary>
    /// A record of one agreed film, with the kind of its one entry replaced.
    /// </summary>
    /// <param name="kind">What the entry names as its kind.</param>
    /// <returns>The document.</returns>
    private static StoredDocument WithKind(string kind)
    {
        var document = AgreedRecords.NoneYet(_pairing, _user)
            .With(Agreement(_film, Watched, _evening))
            .ToDocument();

        var entry = (JsonObject)document.Fields["items"]![
            _film.ToString("n", CultureInfo.InvariantCulture)]!;

        entry["kind"] = JsonValue.Create(kind);

        return document;
    }

    private static SyncedState Watched =>
        new SyncedState(true, 2, 0, new DateTime(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc));

    private static TransferSubject Subject(Guid user, Guid item, BaseItemKind kind) =>
        TransferSubject.From(user, item, kind).Value!;

    private static AgreedRecord Agreement(
        Guid item,
        SyncedState agreed,
        DateTimeOffset agreedAt,
        BaseItemKind kind = BaseItemKind.Movie) =>
        new AgreedRecord(Subject(_user, item, kind), agreed, agreedAt, 1);

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }
}

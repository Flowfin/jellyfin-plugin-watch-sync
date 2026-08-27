using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Matching;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The items that did not match, as a document that outlives the pass that found them, which is
/// what #26's second condition needs of a record that is the source for a count on a page.
///
/// The set is written against three properties. A record written before a restart is the record
/// read after it, member by member. A document is the whole record or it is refused, because a
/// list of unmatched items that is quietly short answers "how much of my library is not syncing"
/// with a smaller number, and a smaller number reads as the thing improving. And the record is
/// bounded in both of the ways #26's third condition needs: a pass over a library of items that
/// will never match leaves it the size it was, and a library larger than the cap leaves it at the
/// cap rather than at the size of the library.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class UnmatchedRecordsTests : IDisposable
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
    /// Initializes a new instance of the <see cref="UnmatchedRecordsTests"/> class, with a
    /// directory of its own standing in for what a server would hand over.
    /// </summary>
    public UnmatchedRecordsTests()
    {
        _programData = TemporaryDirectory.Create("unmatched");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// A record written by one store is read by a second store built over the same folder, which
    /// is what a restart is from this plugin's side.
    ///
    /// Every member of every entry is compared rather than the count, because the count is the
    /// one thing this record is read for and a record holding the right number of rows with the
    /// wrong reasons in them sends an operator to repair the wrong thing.
    /// </summary>
    [Fact]
    public void ARecordWrittenBeforeARestartIsTheRecordReadAfterIt()
    {
        var name = UnmatchedRecords.DocumentName(_pairing, _user);

        var written = UnmatchedRecords.NoneYet(_pairing, _user)
            .With(Unmatched(_film, BaseItemKind.Movie, MatchKeyRefusal.NoIdentifierAtAll, null, _evening))
            .With(Unmatched(_episode, BaseItemKind.Episode, MatchKeyRefusal.None, MatchAnswer.NoMatch, _evening.AddHours(1)));

        new DocumentStore(Folder()).Write(name, _ => written.ToDocument());

        var reading = UnmatchedRecords.Read(new DocumentStore(Folder()).Read(name)!.Document!);

        Assert.False(reading.IsRefused);

        var read = reading.Records!;

        Assert.Equal(_pairing, read.PairingId);
        Assert.Equal(_user, read.MappedUserId);
        Assert.Equal(2, read.Count);

        var film = read.For(_film)!;

        Assert.Equal(BaseItemKind.Movie, film.Kind);
        Assert.Equal(MatchKeyRefusal.NoIdentifierAtAll, film.Refusal);
        Assert.Null(film.Answer);
        Assert.Equal(_evening, film.LastAttemptedAt);

        var episode = read.For(_episode)!;

        Assert.Equal(BaseItemKind.Episode, episode.Kind);
        Assert.Equal(MatchKeyRefusal.None, episode.Refusal);
        Assert.Equal(MatchAnswer.NoMatch, episode.Answer);
        Assert.Equal(_evening.AddHours(1), episode.LastAttemptedAt);
    }

    /// <summary>
    /// A pass over a library of items that will never match leaves the record the size it was,
    /// which is the half of #26's third condition the keying is for.
    ///
    /// Ten thousand items are attempted three times each. Keyed on the item those attempts replace
    /// entries; keyed on anything else they would add thirty thousand, and the document would grow
    /// on every pass forever while every fixture of three items stayed green. The last attempt is
    /// what moves, which is what #26's fourth condition reads to tell that a pass has been past.
    /// </summary>
    [Fact]
    public void RepeatedPassesOverALibraryThatWillNeverMatchDoNotGrowTheRecord()
    {
        var items = Enumerable
            .Range(0, 10_000)
            .Select(number => Item(number))
            .ToList();

        var records = UnmatchedRecords.NoneYet(_pairing, _user);

        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var item in items)
            {
                records = records.With(Unmatched(
                    item,
                    BaseItemKind.Movie,
                    MatchKeyRefusal.NoIdentifierAtAll,
                    null,
                    _evening.AddDays(pass)));
            }

            Assert.Equal(UnmatchedRecords.MaximumEntries, records.Count);
        }

        Assert.All(
            records.All,
            entry => Assert.Equal(_evening.AddDays(2), entry.LastAttemptedAt));
    }

    /// <summary>
    /// A library larger than the cap leaves the record at the cap, and what is dropped is what was
    /// attempted longest ago.
    ///
    /// This is the other half of the same condition and it is where the cost is. The record
    /// becomes a sample rather than a census, so the count on a page is the count of what is held
    /// and not the number of unmatched items in the library. Nothing here can repair that, and it
    /// is written into the type rather than left to be discovered by an operator whose hundred
    /// thousand unmatchable items are reported as a thousand.
    /// </summary>
    [Fact]
    public void ALibraryLargerThanTheCapLeavesTheOldestAttemptOut()
    {
        var records = UnmatchedRecords.NoneYet(_pairing, _user);

        for (var number = 0; number <= UnmatchedRecords.MaximumEntries; number++)
        {
            records = records.With(Unmatched(
                Item(number),
                BaseItemKind.Movie,
                MatchKeyRefusal.NoIdentifierAtAll,
                null,
                _evening.AddMinutes(number)));
        }

        Assert.Equal(UnmatchedRecords.MaximumEntries, records.Count);
        Assert.Null(records.For(Item(0)));
        Assert.NotNull(records.For(Item(UnmatchedRecords.MaximumEntries)));
    }

    /// <summary>
    /// An item attempted again replaces its entry rather than adding one, and the reason moves
    /// with it.
    ///
    /// An item whose metadata was repaired between two passes falls out for a different reason or
    /// stops falling out at all, and a record that kept the first reason would send an operator to
    /// repair something they have already repaired.
    /// </summary>
    [Fact]
    public void AnItemAttemptedAgainReplacesItsEntry()
    {
        var records = UnmatchedRecords.NoneYet(_pairing, _user)
            .With(Unmatched(_film, BaseItemKind.Movie, MatchKeyRefusal.NoIdentifierAtAll, null, _evening))
            .With(Unmatched(_film, BaseItemKind.Movie, MatchKeyRefusal.None, MatchAnswer.NoMatch, _evening.AddDays(1)));

        Assert.Equal(1, records.Count);
        Assert.Equal(MatchKeyRefusal.None, records.For(_film)!.Refusal);
        Assert.Equal(MatchAnswer.NoMatch, records.For(_film)!.Answer);
        Assert.Equal(_evening.AddDays(1), records.For(_film)!.LastAttemptedAt);
    }

    /// <summary>
    /// An item that matches later is taken out, and taking out an item that is not there answers
    /// with the same entries.
    ///
    /// The first is what #26's fourth condition needs on the day an item acquires an identifier: a
    /// row left behind is one an operator keeps trying to repair. The second is what lets a pass
    /// that matched everything say so without asking first.
    /// </summary>
    [Fact]
    public void AnItemThatMatchesLaterIsTakenOut()
    {
        var records = UnmatchedRecords.NoneYet(_pairing, _user)
            .With(Unmatched(_film, BaseItemKind.Movie, MatchKeyRefusal.NoIdentifierAtAll, null, _evening))
            .With(Unmatched(_episode, BaseItemKind.Episode, MatchKeyRefusal.NoSeasonNumber, null, _evening));

        var after = records.Without(_film);

        Assert.Equal(1, after.Count);
        Assert.Null(after.For(_film));
        Assert.Equal(1, after.Without(_film).Count);
    }

    /// <summary>
    /// A record about no pairing or no person is refused where it is made.
    /// </summary>
    [Fact]
    public void ARecordAboutNoPairingOrNoPersonIsRefused()
    {
        Assert.Equal(
            "pairingId",
            Assert.Throws<ArgumentException>(
                () => UnmatchedRecords.NoneYet(Guid.Empty, _user)).ParamName);

        Assert.Equal(
            "mappedUserId",
            Assert.Throws<ArgumentException>(
                () => UnmatchedRecords.NoneYet(_pairing, Guid.Empty)).ParamName);
    }

    /// <summary>
    /// A document carrying what the record refuses is refused, in each of the three ways.
    ///
    /// Every entry goes back through the record's own constructor rather than into fields of its
    /// own, so a document written by hand meets the same rules a caller does. The third of them is
    /// the one that matters most on a page: a document naming a match would put an item that
    /// matched into a count of what did not.
    /// </summary>
    [Fact]
    public void ADocumentCannotCarryWhatTheRecordRefuses()
    {
        var both = Entry();
        both["refusal"] = JsonValue.Create(nameof(MatchKeyRefusal.NoIdentifierAtAll));
        both["answer"] = JsonValue.Create(nameof(MatchAnswer.NoMatch));

        Assert.True(UnmatchedRecords.Read(Rebuilt(Fields(both))).IsRefused);

        var neither = Entry();
        neither["refusal"] = JsonValue.Create(nameof(MatchKeyRefusal.None));
        neither["answer"] = null;

        Assert.True(UnmatchedRecords.Read(Rebuilt(Fields(neither))).IsRefused);

        var matched = Entry();
        matched["refusal"] = JsonValue.Create(nameof(MatchKeyRefusal.None));
        matched["answer"] = JsonValue.Create(nameof(MatchAnswer.Matched));

        Assert.True(UnmatchedRecords.Read(Rebuilt(Fields(matched))).IsRefused);
    }

    /// <summary>
    /// A document whose entry has no answer member at all is refused, and one holding null there
    /// is read as an entry whose key was never derived.
    ///
    /// The two are different documents. A missing member is one somebody assembled without it, and
    /// reading it as "no lookup happened" would decide which half of the reason the entry carries
    /// on behalf of whoever wrote it.
    /// </summary>
    [Fact]
    public void AMissingAnswerIsRefusedAndAnEmptyOneIsRead()
    {
        var missing = Entry();
        missing.Remove("answer");

        Assert.True(UnmatchedRecords.Read(Rebuilt(Fields(missing))).IsRefused);

        var reading = UnmatchedRecords.Read(Rebuilt(Fields(Entry())));

        Assert.False(reading.IsRefused);
        Assert.Null(reading.Records!.For(_film)!.Answer);
    }

    /// <summary>
    /// A document naming the reason or the class by number is refused.
    ///
    /// A name written as its position would keep meaning whatever that position happened to be on
    /// the day it was written, and one of the two enumerations is the server's rather than this
    /// plugin's, so its positions move for reasons nothing here controls.
    /// </summary>
    [Fact]
    public void ADocumentNamingAReasonOrAClassByNumberIsRefused()
    {
        var reason = Entry();
        reason["refusal"] = JsonValue.Create("1");

        Assert.True(UnmatchedRecords.Read(Rebuilt(Fields(reason))).IsRefused);

        var kind = Entry();
        kind["kind"] = JsonValue.Create("1");

        Assert.True(UnmatchedRecords.Read(Rebuilt(Fields(kind))).IsRefused);
    }

    /// <summary>
    /// A document that is not this record at all is refused rather than read as an empty one.
    ///
    /// An empty answer and an unreadable document are opposite statements to somebody asking how
    /// much of their library is not syncing: the first says none of it.
    /// </summary>
    [Fact]
    public void ADocumentThatIsNotThisRecordIsRefused()
    {
        Assert.True(UnmatchedRecords
            .Read(Rebuilt(new JsonObject { ["something"] = JsonValue.Create("else") }))
            .IsRefused);

        var noItems = Fields(Entry());
        noItems.Remove("items");

        Assert.True(UnmatchedRecords.Read(Rebuilt(noItems)).IsRefused);

        var notAnIdentifier = Fields(Entry());
        ((JsonObject)notAnIdentifier["items"]!)["not-an-identifier"] = Entry();

        Assert.True(UnmatchedRecords.Read(Rebuilt(notAnIdentifier)).IsRefused);
    }

    /// <summary>
    /// The name is derived from what the record is about, so two pairings and two people never
    /// collide on one document and a walk over the store can place one without opening it.
    /// </summary>
    [Fact]
    public void TheNameSaysWhatTheRecordIsAbout()
    {
        var name = UnmatchedRecords.DocumentName(_pairing, _user);

        Assert.StartsWith("unmatched-", name, StringComparison.Ordinal);
        Assert.NotEqual(name, UnmatchedRecords.DocumentName(_otherPairing, _user));
        Assert.NotEqual(name, UnmatchedRecords.DocumentName(_pairing, _otherUser));
    }

    /// <summary>
    /// Two writes of one record produce the same bytes, so a difference between two documents is a
    /// difference somebody made rather than the order a dictionary happened to be walked in.
    /// </summary>
    [Fact]
    public void TwoWritesOfOneRecordProduceTheSameBytes()
    {
        var records = UnmatchedRecords.NoneYet(_pairing, _user)
            .With(Unmatched(_episode, BaseItemKind.Episode, MatchKeyRefusal.NoSeasonNumber, null, _evening))
            .With(Unmatched(_film, BaseItemKind.Movie, MatchKeyRefusal.None, MatchAnswer.Ambiguous, _evening));

        Assert.Equal(
            records.ToDocument().Fields.ToJsonString(),
            records.ToDocument().Fields.ToJsonString());

        Assert.Equal(
            new[] { _film, _episode }.OrderBy(id => id).ToList(),
            records.All.Select(entry => entry.ItemId).ToList());
    }

    /// <summary>
    /// One entry, as the document carries it: a film whose key could not be derived at all.
    /// </summary>
    /// <returns>The entry.</returns>
    private static JsonObject Entry()
    {
        var written = UnmatchedRecords.NoneYet(_pairing, _user)
            .With(Unmatched(_film, BaseItemKind.Movie, MatchKeyRefusal.NoIdentifierAtAll, null, _evening))
            .ToDocument();

        return (JsonObject)((JsonObject)written.Fields["items"]!)[
            _film.ToString("n", CultureInfo.InvariantCulture)]!.DeepClone();
    }

    /// <summary>
    /// The members of a document holding one entry, keyed on the film.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The members beside the version.</returns>
    private static JsonObject Fields(JsonObject entry) => new JsonObject
    {
        ["pairing"] = JsonValue.Create(_pairing.ToString("n", CultureInfo.InvariantCulture)),
        ["user"] = JsonValue.Create(_user.ToString("n", CultureInfo.InvariantCulture)),
        ["items"] = new JsonObject
        {
            [_film.ToString("n", CultureInfo.InvariantCulture)] = entry,
        },
    };

    /// <summary>
    /// A document rebuilt out of bytes, which is the only way one arrives from the store.
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
    /// One item of a synthetic library, named so that two numbers are never one item.
    /// </summary>
    /// <param name="number">Its number.</param>
    /// <returns>The item.</returns>
    private static Guid Item(int number) =>
        new Guid(number, 0, 0, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7 });

    private static UnmatchedRecord Unmatched(
        Guid item,
        BaseItemKind kind,
        MatchKeyRefusal refusal,
        MatchAnswer? answer,
        DateTimeOffset lastAttemptedAt) =>
        new UnmatchedRecord(item, kind, refusal, answer, lastAttemptedAt);

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }
}

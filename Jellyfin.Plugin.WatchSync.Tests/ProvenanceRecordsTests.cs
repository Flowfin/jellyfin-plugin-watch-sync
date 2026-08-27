using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The provenance of what this plugin wrote, as a document that outlives the run that wrote it,
/// which is what #44's second condition needs and the reason the record is in the store at all.
///
/// A revocation is not an event this plugin schedules. It arrives days or months after the writes
/// it is about, so a record held in memory answers for nothing, and a document read back as a
/// subset of what was written hands an undo a list that looks complete and is not.
///
/// The set is written against four properties. A record written before a restart is the record
/// read after it, member by member. A document is the whole record or it is refused. The order is
/// the order the writes happened in, because an undo walks it newest first and stops at the first
/// entry for a field. And both bounds drop the oldest, which is the direction that walk wants.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class ProvenanceRecordsTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _otherUser = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _episode = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid _peerUser = new("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProvenanceRecordsTests"/> class, with a
    /// directory of its own standing in for what a server would hand over.
    /// </summary>
    public ProvenanceRecordsTests()
    {
        _programData = TemporaryDirectory.Create("provenance");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// A record written by one store is read by a second store built over the same folder, which
    /// is what a restart is from this plugin's side: the process that wrote is gone and nothing of
    /// it is in memory.
    ///
    /// Every member of every entry is compared rather than the count. A record that survives
    /// holding the right number of writes and the wrong values in them is one an undo would act
    /// on, and what it would write back into somebody's record is a value nobody ever had.
    ///
    /// The third write is the one that has to cross the bytes rather than only the type: a peer's
    /// answer that cleared a last played date is written as null and read back as null, and the
    /// entry beside it holding a real number is what says the two are not being collapsed.
    /// </summary>
    [Fact]
    public void ARecordWrittenBeforeARestartIsTheRecordReadAfterIt()
    {
        var name = ProvenanceRecords.DocumentName(_pairing, _user);

        var written = ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, SyncedField.Played, null, 1, _evening))
            .With(Write(_episode, SyncedField.PlaybackPositionTicks, 0, 9_000_000_000, _evening.AddHours(1)))
            .With(Write(_film, SyncedField.LastPlayedDate, _evening.UtcTicks, null, _evening.AddHours(2)));

        new DocumentStore(Folder()).Write(name, _ => written.ToDocument());

        var reading = ProvenanceRecords.Read(new DocumentStore(Folder()).Read(name)!.Document!);

        Assert.False(reading.IsRefused);

        var read = reading.Records!;

        Assert.Equal(_pairing, read.PairingId);
        Assert.Equal(_user, read.MappedUserId);
        Assert.Equal(3, read.Count);

        var first = read.All[0];

        Assert.Equal(_peerUser, first.PeerUserId);
        Assert.Equal(_film, first.ItemId);
        Assert.Equal(SyncedField.Played, first.Field);
        Assert.Null(first.Before);
        Assert.Equal(1, first.Written);
        Assert.Equal(_evening, first.WrittenAt);

        var second = read.All[1];

        Assert.Equal(_peerUser, second.PeerUserId);
        Assert.Equal(_episode, second.ItemId);
        Assert.Equal(SyncedField.PlaybackPositionTicks, second.Field);
        Assert.Equal(0, second.Before);
        Assert.Equal(9_000_000_000, second.Written);
        Assert.Equal(_evening.AddHours(1), second.WrittenAt);

        var third = read.All[2];

        Assert.Equal(_peerUser, third.PeerUserId);
        Assert.Equal(_film, third.ItemId);
        Assert.Equal(SyncedField.LastPlayedDate, third.Field);
        Assert.Equal(_evening.UtcTicks, third.Before);
        Assert.Null(third.Written);
        Assert.Equal(_evening.AddHours(2), third.WrittenAt);
    }

    /// <summary>
    /// Two writes to one field are two entries, and the newer one carries the value the person
    /// held between them.
    ///
    /// This is why the record is a list rather than an object keyed on the item and the field. The
    /// value between two of this plugin's writes is one the person put there themselves, and this
    /// record is the only place it exists. An undo walks newest first and stops at the first entry
    /// for a field, so what it restores is that value and not the one from before the first write,
    /// which the person had already replaced.
    /// </summary>
    [Fact]
    public void TwoWritesToOneFieldKeepTheValueTheHouseholdPutBetweenThem()
    {
        var records = ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, SyncedField.PlayCount, 1, 2, _evening))
            .With(Write(_film, SyncedField.PlayCount, 3, 4, _evening.AddDays(1)));

        var newest = records.All
            .Where(write => write.ItemId == _film && write.Field == SyncedField.PlayCount)
            .Last();

        Assert.Equal(2, records.Count);
        Assert.Equal(3, newest.Before);
        Assert.Equal(4, newest.Written);
    }

    /// <summary>
    /// The cap drops the oldest and keeps the newest, which is the direction the walk above wants.
    ///
    /// What it costs is a write that can no longer be undone, and that cost is real rather than
    /// hypothetical. It falls on the oldest entries because the entry an undo reads for any field
    /// is the newest one, so dropping from the other end would make the record longer and less
    /// useful at the same time.
    /// </summary>
    [Fact]
    public void TheCapDropsTheOldestAndKeepsTheNewest()
    {
        var records = ProvenanceRecords.NoneYet(_pairing, _user);

        for (var made = 0; made <= ProvenanceRecords.MaximumEntries; made++)
        {
            records = records.With(
                Write(_film, SyncedField.PlayCount, made, made + 1, _evening.AddMinutes(made)));
        }

        Assert.Equal(ProvenanceRecords.MaximumEntries, records.Count);
        Assert.Equal(1, records.All[0].Before);
        Assert.Equal(ProvenanceRecords.MaximumEntries, records.All[^1].Before);
    }

    /// <summary>
    /// The retention drops what was written before the boundary and keeps the rest.
    ///
    /// The boundary is a parameter rather than a span subtracted from a clock this type reads,
    /// which is what the sweep in #55 will hand it and what makes the rule testable at all.
    /// </summary>
    [Fact]
    public void TheRetentionDropsWhatWasWrittenBeforeTheBoundary()
    {
        var records = ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, SyncedField.Played, 0, 1, _evening))
            .With(Write(_episode, SyncedField.Played, 0, 1, _evening.AddDays(30)));

        var kept = records.Retaining(_evening.AddDays(1));

        Assert.Equal(1, kept.Count);
        Assert.Equal(_episode, kept.All[0].ItemId);
    }

    /// <summary>
    /// A write that came in on another pairing is refused.
    ///
    /// A document is one pairing's. A write from elsewhere in it would be readable afterwards as
    /// one this plugin made under a pairing it never came in on, and an undo bounded by a revoked
    /// pairing would revert a value that arrived under one that is still live.
    /// </summary>
    [Fact]
    public void AWriteFromAnotherPairingIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => ProvenanceRecords
            .NoneYet(_pairing, _user)
            .With(new ProvenanceRecord(
                _otherPairing,
                _user,
                _peerUser,
                _film,
                SyncedField.Played,
                0,
                1,
                _evening)));

        Assert.Equal("write", refused.ParamName);
    }

    /// <summary>
    /// A write against another mapped user is refused, for the other half of the same reason: the
    /// document is one person's, and a report of what is held about them is driven by its name.
    /// </summary>
    [Fact]
    public void AWriteAgainstAnotherUserIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => ProvenanceRecords
            .NoneYet(_pairing, _user)
            .With(new ProvenanceRecord(
                _pairing,
                _otherUser,
                _peerUser,
                _film,
                SyncedField.Played,
                0,
                1,
                _evening)));

        Assert.Equal("write", refused.ParamName);
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
                () => ProvenanceRecords.NoneYet(Guid.Empty, _user)).ParamName);

        Assert.Equal(
            "mappedUserId",
            Assert.Throws<ArgumentException>(
                () => ProvenanceRecords.NoneYet(_pairing, Guid.Empty)).ParamName);
    }

    /// <summary>
    /// A document whose entry has no replaced value at all is refused, and one holding null there
    /// is read.
    ///
    /// The two are different documents and only the second is a write. A missing member is a
    /// document somebody assembled without the field, and reading it as "this server held nothing"
    /// would invent the half of the record that decides what an undo puts back: it would restore
    /// nothing where a real value was replaced.
    /// </summary>
    [Fact]
    public void AMissingReplacedValueIsRefusedAndAnEmptyOneIsRead()
    {
        var entry = Entry();
        entry.Remove("before");

        Assert.True(ProvenanceRecords.Read(Rebuilt(Fields(entry))).IsRefused);

        var empty = Entry();
        empty["before"] = null;

        var reading = ProvenanceRecords.Read(Rebuilt(Fields(empty)));

        Assert.False(reading.IsRefused);
        Assert.Null(reading.Records!.All[0].Before);
    }

    /// <summary>
    /// A document whose entry has no written value at all is refused, and one holding null there
    /// over a value that was replaced is read as the write that cleared it.
    ///
    /// The two are different documents on this member for the same reason they are on the other
    /// one. A missing member is a document that never said what was written, and reading it as a
    /// clearing would hand an undo a write to reverse that nothing performed. Null beside a
    /// replaced value is the write one of the four moved fields can actually be: a peer's answer
    /// that cleared somebody's last played date, which is the value an undo most needs and the
    /// one this record had nowhere to put until the member took the nullable shape.
    /// </summary>
    [Fact]
    public void AMissingWrittenValueIsRefusedAndAClearingIsRead()
    {
        var missing = Entry();
        missing.Remove("written");

        Assert.True(ProvenanceRecords.Read(Rebuilt(Fields(missing))).IsRefused);

        var cleared = Entry();
        cleared["field"] = JsonValue.Create(SyncedField.LastPlayedDate.ToString());
        cleared["before"] = JsonValue.Create(_evening.UtcTicks);
        cleared["written"] = null;

        var reading = ProvenanceRecords.Read(Rebuilt(Fields(cleared)));

        Assert.False(reading.IsRefused);
        Assert.Equal(_evening.UtcTicks, reading.Records!.All[0].Before);
        Assert.Null(reading.Records!.All[0].Written);
    }

    /// <summary>
    /// A document whose entry holds null in both values is refused.
    ///
    /// It says this plugin wrote nothing over nothing, which is a field it did not change, and it
    /// is refused by the record's own rule about a write that replaced nothing rather than by a
    /// rule the reader holds separately. Taking that refusal out of the constructor reddens this,
    /// because the entry goes back through it rather than into fields of its own.
    /// </summary>
    [Fact]
    public void ADocumentClaimingNothingWrittenOverNothingIsRefused()
    {
        var entry = Entry();
        entry["field"] = JsonValue.Create(SyncedField.LastPlayedDate.ToString());
        entry["before"] = null;
        entry["written"] = null;

        Assert.True(ProvenanceRecords.Read(Rebuilt(Fields(entry))).IsRefused);
    }

    /// <summary>
    /// A document whose entry names no peer user is refused, and so is one claiming a write that
    /// replaced a value with itself.
    ///
    /// A document written by hand meets both refusals, and they are not carried by the same thing,
    /// which is worth knowing before somebody moves one. Taking the constructor's refusal of a
    /// write that changed nothing out reddens this fact, because the entry goes back through that
    /// constructor rather than into fields of its own. Taking its refusal of an empty peer user out
    /// does not: the identifier reader refuses an empty identifier one step earlier, so that arm is
    /// held twice and this fact is standing on the outer of the two.
    /// </summary>
    [Fact]
    public void ADocumentCannotCarryWhatTheRecordRefuses()
    {
        var unattributed = Entry();
        unattributed["peerUser"] =
            JsonValue.Create(Guid.Empty.ToString("n"));

        Assert.True(ProvenanceRecords.Read(Rebuilt(Fields(unattributed))).IsRefused);

        var unchanged = Entry();
        unchanged["before"] = JsonValue.Create(1L);
        unchanged["written"] = JsonValue.Create(1L);

        Assert.True(ProvenanceRecords.Read(Rebuilt(Fields(unchanged))).IsRefused);
    }

    /// <summary>
    /// A document naming the field by number is refused.
    ///
    /// A field written as its position would keep meaning whatever that position happened to be on
    /// the day it was written, and the moved set is a list this plan has already reordered once.
    /// An undo reading such a document would restore a play count into a position.
    /// </summary>
    [Fact]
    public void ADocumentNamingTheFieldByNumberIsRefused()
    {
        var entry = Entry();
        entry["field"] = JsonValue.Create("1");

        Assert.True(ProvenanceRecords.Read(Rebuilt(Fields(entry))).IsRefused);
    }

    /// <summary>
    /// A document that is not this record at all is refused rather than read as an empty one.
    ///
    /// An empty answer and an unreadable document are opposite statements to a revocation: the
    /// first says this plugin wrote nothing for that person, and the second says it does not know
    /// what it wrote.
    /// </summary>
    [Fact]
    public void ADocumentThatIsNotThisRecordIsRefused()
    {
        Assert.True(ProvenanceRecords
            .Read(Rebuilt(new JsonObject { ["something"] = JsonValue.Create("else") }))
            .IsRefused);

        var noWrites = Fields(Entry());
        noWrites.Remove("writes");

        Assert.True(ProvenanceRecords.Read(Rebuilt(noWrites)).IsRefused);
    }

    /// <summary>
    /// A record this type has just built and one the store has just read are the same subject.
    ///
    /// A number parsed out of bytes converts between widths and one assembled in memory does not,
    /// which is the trap #14's reader was repaired for. The values here are on both sides of the
    /// narrower width so a reader asking for one width only fails on one of them.
    /// </summary>
    [Fact]
    public void ANumberIsReadAtEitherWidth()
    {
        var records = ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, SyncedField.PlayCount, 1, 2, _evening))
            .With(Write(_episode, SyncedField.PlaybackPositionTicks, 9_000_000_000, 1, _evening));

        var inMemory = ProvenanceRecords.Read(records.ToDocument());
        var throughBytes = ProvenanceRecords.Read(Rebuilt(Written(records)));

        Assert.False(inMemory.IsRefused);
        Assert.False(throughBytes.IsRefused);

        Assert.Equal(
            inMemory.Records!.All.Select(write => (write.Before, write.Written)).ToList(),
            throughBytes.Records!.All.Select(write => (write.Before, write.Written)).ToList());
    }

    /// <summary>
    /// The name is derived from what the record is about, so two pairings and two people never
    /// collide on one document and a walk over the store can place one without opening it.
    /// </summary>
    [Fact]
    public void TheNameSaysWhatTheRecordIsAbout()
    {
        var name = ProvenanceRecords.DocumentName(_pairing, _user);

        Assert.StartsWith("provenance-", name, StringComparison.Ordinal);
        Assert.NotEqual(name, ProvenanceRecords.DocumentName(_otherPairing, _user));
        Assert.NotEqual(name, ProvenanceRecords.DocumentName(_pairing, _otherUser));
    }

    /// <summary>
    /// The order the writes happened in survives the document, including two at one moment.
    ///
    /// Sorting on the recorded moment would reorder a pair a clock reported at the same instant,
    /// and the undo reads this order as the order it walks backwards.
    /// </summary>
    [Fact]
    public void TheOrderTheWritesHappenedInSurvivesTheDocument()
    {
        var records = ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, SyncedField.Played, 0, 1, _evening))
            .With(Write(_film, SyncedField.PlayCount, 1, 2, _evening))
            .With(Write(_episode, SyncedField.Played, 0, 1, _evening));

        var reading = ProvenanceRecords.Read(Rebuilt(Written(records)));

        Assert.False(reading.IsRefused);
        Assert.Equal(
            new[]
            {
                (_film, SyncedField.Played),
                (_film, SyncedField.PlayCount),
                (_episode, SyncedField.Played),
            },
            reading.Records!.All.Select(write => (write.ItemId, write.Field)).ToList());
    }

    /// <summary>
    /// One write, as the entry a document carries it as.
    /// </summary>
    /// <returns>The entry.</returns>
    private static JsonObject Entry()
    {
        var written = ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, SyncedField.PlayCount, 1, 2, _evening))
            .ToDocument();

        return (JsonObject)((JsonArray)written.Fields["writes"]!)[0]!
            .DeepClone();
    }

    /// <summary>
    /// The members of a document holding one entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The members beside the version.</returns>
    private static JsonObject Fields(JsonObject entry) => new JsonObject
    {
        ["pairing"] = JsonValue.Create(_pairing.ToString("n")),
        ["user"] = JsonValue.Create(_user.ToString("n")),
        ["writes"] = new JsonArray(entry),
    };

    /// <summary>
    /// The members of the document a record writes.
    /// </summary>
    /// <param name="records">The record.</param>
    /// <returns>The members beside the version.</returns>
    private static JsonObject Written(ProvenanceRecords records) =>
        (JsonObject)records.ToDocument().Fields.DeepClone();

    /// <summary>
    /// A document rebuilt out of bytes, which is the only way one arrives from the store.
    ///
    /// A document assembled in memory and one parsed out of a file are different subjects to a
    /// reader that asks for a number of one width, so a fact about a damaged document has to meet
    /// the reader through the bytes rather than around them.
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

    private static ProvenanceRecord Write(
        Guid item,
        SyncedField field,
        long? before,
        long? written,
        DateTimeOffset writtenAt) =>
        new ProvenanceRecord(_pairing, _user, _peerUser, item, field, before, written, writtenAt);

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The conflicts this plugin wrote down, as a document that outlives the run that produced them,
/// which is the fourth condition of #36.
///
/// Three properties are what this set is written against. A record written before a restart is
/// the record read after it, because the question an operator asks it is asked the next day. A
/// document is the record that was written or nothing, never a subset of it, because a list of
/// discarded values that is quietly missing entries is a diagnostic that misleads exactly the
/// person using it to find out what happened. And the record is bounded in both of the ways the
/// issue asks for, by a cap and by a retention, because it is an account of what this plugin did
/// and never an archive of what somebody watched.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class ConflictRecordsTests : IDisposable
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
    /// Initializes a new instance of the <see cref="ConflictRecordsTests"/> class, with a
    /// directory of its own standing in for what a server would hand over.
    /// </summary>
    public ConflictRecordsTests()
    {
        _programData = TemporaryDirectory.Create("conflicts");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The fourth condition of #36, and the one this type exists for. A record written by one
    /// store is read by a second store built over the same folder, which is what a restart is
    /// from this plugin's side: the process that wrote is gone and nothing of it is in memory.
    ///
    /// Every member of every entry is compared rather than the count, because a record that
    /// survives a restart holding the right number of conflicts and the wrong values in them is
    /// the answer an operator would act on.
    /// </summary>
    [Fact]
    public void ARecordWrittenBeforeARestartIsTheRecordReadAfterIt()
    {
        var name = ConflictRecords.DocumentName(_pairing, _user);

        var written = ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening))
            .With(Conflict(_episode, SyncedField.PlayCount, ConflictRule.Reckon, 3, null, ConflictSide.Neither, _evening.AddHours(1)));

        new DocumentStore(Folder()).Write(name, _ => written.ToDocument());

        var reading = ConflictRecords.Read(new DocumentStore(Folder()).Read(name)!.Document!);

        Assert.False(reading.IsRefused);

        var read = reading.Records!;

        Assert.Equal(_pairing, read.PairingId);
        Assert.Equal(_user, read.MappedUserId);
        Assert.Equal(2, read.Count);

        var first = read.All[0];

        Assert.Equal(_film, first.ItemId);
        Assert.Equal(SyncedField.Played, first.Field);
        Assert.Equal(ConflictRule.Ratchet, first.Rule);
        Assert.Equal(1, first.Here);
        Assert.Equal(0, first.AtThePeer);
        Assert.Equal(ConflictSide.AtThePeer, first.Discarded);
        Assert.Equal(_evening, first.RecordedAt);

        var second = read.All[1];

        Assert.Equal(_episode, second.ItemId);
        Assert.Equal(SyncedField.PlayCount, second.Field);
        Assert.Equal(ConflictRule.Reckon, second.Rule);
        Assert.Equal(3, second.Here);
        Assert.Null(second.AtThePeer);
        Assert.Equal(ConflictSide.Neither, second.Discarded);
        Assert.Equal(_evening.AddHours(1), second.RecordedAt);
    }

    /// <summary>
    /// Two conflicts about one item are two entries, which is the one place this record's shape
    /// departs from the agreed record in #14.
    ///
    /// An agreement is a current fact and a later one replaces it. A conflict is an event that
    /// happened, and a record keyed on the item would answer the question an operator most often
    /// has, which is a field being decided the same way against them over and over, with the last
    /// of those decisions and no sign that there were others.
    /// </summary>
    [Fact]
    public void TwoConflictsAboutOneItemAreTwoEntriesInTheOrderTheyWereDecided()
    {
        var records = ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, SyncedField.PlaybackPositionTicks, ConflictRule.Recency, 10, 20, ConflictSide.Here, _evening))
            .With(Conflict(_film, SyncedField.PlaybackPositionTicks, ConflictRule.Recency, 20, 30, ConflictSide.Here, _evening.AddMinutes(5)));

        Assert.Equal(2, records.Count);
        Assert.Equal(_evening, records.All[0].RecordedAt);
        Assert.Equal(_evening.AddMinutes(5), records.All[1].RecordedAt);
    }

    /// <summary>
    /// The cap. A peer that conflicts on every exchange cannot make this document grow without
    /// end between two sweeps, and what is dropped is the oldest rather than whatever the
    /// implementation reached first.
    ///
    /// The count is one past the cap, so the fact says which entry went as well as that one did.
    /// </summary>
    [Fact]
    public void RecordingPastTheCapDropsTheOldestAndNothingElse()
    {
        var records = ConflictRecords.NoneYet(_pairing, _user);

        for (var each = 0; each <= ConflictRecords.MaximumEntries; each++)
        {
            records = records.With(Conflict(
                _film,
                SyncedField.PlayCount,
                ConflictRule.Reckon,
                each,
                null,
                ConflictSide.Neither,
                _evening.AddMinutes(each)));
        }

        Assert.Equal(ConflictRecords.MaximumEntries, records.Count);
        Assert.Equal(_evening.AddMinutes(1), records.All[0].RecordedAt);
        Assert.Equal(1, records.All[0].Here);
        Assert.Equal(ConflictRecords.MaximumEntries, records.All[records.Count - 1].Here);
    }

    /// <summary>
    /// The retention, from the side that decides it. What is older than the boundary goes and
    /// what sits on it stays, because the boundary is the oldest moment kept rather than the
    /// newest moment dropped, and an off-by-one here silently shortens every operator's window by
    /// whatever the sweep's period is.
    /// </summary>
    [Fact]
    public void RetainingDropsWhatIsOlderThanTheBoundaryAndKeepsWhatSitsOnIt()
    {
        var records = ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening.AddSeconds(-1)))
            .With(Conflict(_film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening))
            .With(Conflict(_episode, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening.AddSeconds(1)));

        var kept = records.Retaining(_evening);

        Assert.Equal(2, kept.Count);
        Assert.Equal(_evening, kept.All[0].RecordedAt);
        Assert.Equal(_evening.AddSeconds(1), kept.All[1].RecordedAt);
        Assert.Equal(3, records.Count);
    }

    /// <summary>
    /// The two numbers the retention is written against. The default is short, because the record
    /// is a diagnostic about what somebody watched, and the maximum bounds what an operator can
    /// raise it to. A default above the maximum would be a setting that is invalid the moment it
    /// is left alone.
    /// </summary>
    [Fact]
    public void TheDefaultRetentionIsShortAndSitsInsideTheMaximum()
    {
        Assert.True(ConflictRecords.DefaultRetention > TimeSpan.Zero);
        Assert.True(ConflictRecords.DefaultRetention <= ConflictRecords.MaximumRetention);
        Assert.True(ConflictRecords.MaximumRetention <= TimeSpan.FromDays(90));
    }

    /// <summary>
    /// Every change answers with a new record and changes nothing about the one it was called on.
    ///
    /// This is what the store's write path needs rather than a preference. A change handed to
    /// <c>DocumentStore.Write</c> is computed from the document that was on disk when the attempt
    /// began and is made again where somebody else replaced it in between, so a record that
    /// mutated what it was given would carry the first attempt's entry into the second and write
    /// one conflict down twice.
    /// </summary>
    [Fact]
    public void RecordingAConflictLeavesTheRecordItWasCalledOnAlone()
    {
        var before = ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening));

        var after = before.With(
            Conflict(_episode, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening));

        Assert.Equal(1, before.Count);
        Assert.Equal(2, after.Count);
        Assert.Equal(0, before.Retaining(_evening.AddYears(1)).Count);
        Assert.Equal(1, before.Count);
        Assert.Equal(_film, before.All[0].ItemId);
    }

    /// <summary>
    /// The name is derived from what the record is about, and the store composes a path out of it
    /// rather than out of anything a caller chose. A name the store refuses would be a record
    /// that cannot be written at all, and it would be found by an operator rather than here.
    /// </summary>
    [Fact]
    public void TheNameIsMadeOfThePairingAndTheUserAndTheStoreAcceptsIt()
    {
        var name = ConflictRecords.DocumentName(_pairing, _user);

        Assert.Matches(new Regex("^[a-z0-9-]+$", RegexOptions.None, TimeSpan.FromSeconds(1)), name);
        Assert.StartsWith("conflicts-", name, StringComparison.Ordinal);
        Assert.NotEqual(name, ConflictRecords.DocumentName(_otherPairing, _user));
        Assert.NotEqual(name, ConflictRecords.DocumentName(_pairing, _otherUser));
        Assert.NotEqual(name, Jellyfin.Plugin.WatchSync.Agreement.AgreedRecords.DocumentName(_pairing, _user));

        var answer = new DocumentStore(Folder())
            .Write(name, _ => ConflictRecords.NoneYet(_pairing, _user).ToDocument());

        Assert.Equal(DocumentWriteOutcome.Written, answer.Outcome);
    }

    /// <summary>
    /// A record read straight out of the document it just built, without the bytes in between.
    ///
    /// This is the case a suite that only ever reads what the store handed back would never
    /// visit, and it was wrong in #14's reader when it was first written: a number parsed out of
    /// bytes converts between widths and one assembled in memory does not. Both readings here are
    /// numbers, so this type meets the same trap twice, and what it would cost is a record on
    /// disk that reads as unreadable while the bytes are correct.
    /// </summary>
    [Fact]
    public void ARecordIsReadableOutOfTheDocumentItBuiltAsWellAsOutOfTheStore()
    {
        var written = ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, SyncedField.LastPlayedDate, ConflictRule.Maximum, 638, 639, ConflictSide.Neither, _evening));

        var reading = ConflictRecords.Read(written.ToDocument());

        Assert.False(reading.IsRefused);
        Assert.Equal(638, reading.Records!.All[0].Here);
        Assert.Equal(639, reading.Records!.All[0].AtThePeer);
    }

    /// <summary>
    /// One entry that is not a conflict refuses the whole document rather than being dropped out
    /// of it.
    ///
    /// The cost is higher here than it is for the agreed record, and it is still the right
    /// answer. An agreed record refused is rebuilt by a full reconciliation; this one cannot be
    /// rebuilt by anything, because it is an account of decisions already taken. What a partial
    /// read would hand an operator is a list that looks complete and is not, at the moment they
    /// are using it to work out what this plugin did to somebody's history.
    /// </summary>
    /// <param name="member">The member removed from the one entry.</param>
    [Theory]
    [InlineData("item")]
    [InlineData("field")]
    [InlineData("rule")]
    [InlineData("here")]
    [InlineData("atThePeer")]
    [InlineData("discarded")]
    [InlineData("recordedAt")]
    public void ADocumentMissingOneMemberOfOneEntryIsNotARecordOfConflicts(string member)
    {
        var document = OneConflict();

        ((JsonObject)document.Fields["conflicts"]![0]!).Remove(member);

        Assert.Equal(
            ConflictRecordsAnswer.NotARecordOfConflicts,
            ConflictRecords.Read(Rebuilt(document.Fields)).Answer);
    }

    /// <summary>
    /// A reading that is present and null is a side that held nothing, and a reading that is
    /// absent is a document somebody assembled without the member. Reading the second as the
    /// first would invent the half of a conflict that decides whether a side lost anything.
    /// </summary>
    [Fact]
    public void AReadingWrittenAsNullIsASideThatHeldNothing()
    {
        var document = OneConflict();
        var entry = (JsonObject)document.Fields["conflicts"]![0]!;

        entry["atThePeer"] = null;
        entry["discarded"] = JsonValue.Create(nameof(ConflictSide.Here));

        var reading = ConflictRecords.Read(Rebuilt(document.Fields));

        Assert.False(reading.IsRefused);
        Assert.Null(reading.Records!.All[0].AtThePeer);
        Assert.Equal(ConflictSide.Here, reading.Records!.All[0].Discarded);
    }

    /// <summary>
    /// The refusal the record's own constructor makes is made on the way in as well, so a
    /// document written by hand cannot get around it.
    ///
    /// A side recorded as having lost a reading it never held tells an operator that this plugin
    /// threw away a value that was never there, which is the one thing this record must not be
    /// able to say.
    /// </summary>
    /// <param name="reading">The member set to null.</param>
    /// <param name="side">The side named as having lost.</param>
    [Theory]
    [InlineData("here", nameof(ConflictSide.Here))]
    [InlineData("atThePeer", nameof(ConflictSide.AtThePeer))]
    public void ADocumentNamingASideThatHeldNothingAsTheLoserIsNotARecordOfConflicts(
        string reading,
        string side)
    {
        var document = OneConflict();
        var entry = (JsonObject)document.Fields["conflicts"]![0]!;

        entry[reading] = null;
        entry["discarded"] = JsonValue.Create(side);

        Assert.Equal(
            ConflictRecordsAnswer.NotARecordOfConflicts,
            ConflictRecords.Read(Rebuilt(document.Fields)).Answer);
    }

    /// <summary>
    /// A field, a rule or a side written by its number is refused, because a number keeps meaning
    /// whatever that position happened to be on the day it was written and all three of those
    /// enumerations gain members as this plan lands rows.
    /// </summary>
    /// <param name="member">The member written as a number.</param>
    [Theory]
    [InlineData("field")]
    [InlineData("rule")]
    [InlineData("discarded")]
    public void ADocumentNamingAnEnumerationByItsNumberIsNotARecordOfConflicts(string member)
    {
        var document = OneConflict();

        ((JsonObject)document.Fields["conflicts"]![0]!)[member] = JsonValue.Create(0);

        Assert.Equal(
            ConflictRecordsAnswer.NotARecordOfConflicts,
            ConflictRecords.Read(Rebuilt(document.Fields)).Answer);
    }

    /// <summary>
    /// A name none of the three enumerations carries is refused rather than read as the nearest
    /// member, and so is a case-folded spelling of one that does. A record naming a rule this
    /// code does not have is one whose decision nobody here can explain.
    /// </summary>
    /// <param name="named">What the rule member holds.</param>
    [Theory]
    [InlineData("Nothing")]
    [InlineData("ratchet")]
    [InlineData("")]
    public void ADocumentNamingARuleThisCodeDoesNotHaveIsNotARecordOfConflicts(string named)
    {
        var document = OneConflict();

        ((JsonObject)document.Fields["conflicts"]![0]!)["rule"] = JsonValue.Create(named);

        Assert.Equal(
            ConflictRecordsAnswer.NotARecordOfConflicts,
            ConflictRecords.Read(Rebuilt(document.Fields)).Answer);
    }

    /// <summary>
    /// A document that is not this record at all, in each of the three ways it can fail to be one
    /// before any entry is looked at.
    /// </summary>
    /// <param name="member">The member removed.</param>
    [Theory]
    [InlineData("pairing")]
    [InlineData("user")]
    [InlineData("conflicts")]
    public void ADocumentWithoutThePairingTheUserOrTheConflictsIsNotARecordOfConflicts(
        string member)
    {
        var document = OneConflict();

        document.Fields.Remove(member);

        Assert.Equal(
            ConflictRecordsAnswer.NotARecordOfConflicts,
            ConflictRecords.Read(Rebuilt(document.Fields)).Answer);
    }

    /// <summary>
    /// The conflicts have to be a list. An object under that member is a document of another
    /// shape, and reading it as an empty list would report that this plugin discarded nothing.
    /// </summary>
    [Fact]
    public void ADocumentWhoseConflictsAreNotAListIsNotARecordOfConflicts()
    {
        var document = OneConflict();

        document.Fields["conflicts"] = new JsonObject();

        Assert.Equal(
            ConflictRecordsAnswer.NotARecordOfConflicts,
            ConflictRecords.Read(Rebuilt(document.Fields)).Answer);
    }

    /// <summary>
    /// A record is one pairing's and one user's, so a conflict from elsewhere is refused rather
    /// than filed. A conflict readable afterwards under a pairing and a person it never happened
    /// to is worse than no record at all.
    /// </summary>
    [Fact]
    public void AConflictFromAnotherPairingOrAnotherUserIsRefused()
    {
        var records = ConflictRecords.NoneYet(_pairing, _user);

        Assert.Throws<ArgumentException>(() => records.With(new ConflictRecord(
            _otherPairing, _user, _film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening)));

        Assert.Throws<ArgumentException>(() => records.With(new ConflictRecord(
            _pairing, _otherUser, _film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening)));

        Assert.Throws<ArgumentNullException>(() => records.With(null!));
    }

    /// <summary>
    /// A record about no pairing or no user is refused where it is made, because the document's
    /// name is built out of both and an empty identifier would put every such record on one name.
    /// </summary>
    [Fact]
    public void ARecordOfNoPairingOrNoUserIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ConflictRecords.NoneYet(Guid.Empty, _user));
        Assert.Throws<ArgumentException>(() => ConflictRecords.NoneYet(_pairing, Guid.Empty));
        Assert.Throws<ArgumentNullException>(() => ConflictRecords.Read(null!));
    }

    /// <summary>
    /// A document holding more than the cap is read as it stands and trimmed at the next write,
    /// rather than refused.
    ///
    /// This is the one count this reader does not refuse on, and the direction is deliberate. The
    /// day somebody lowers the cap, refusing would turn every operator's record into an
    /// unreadable document, and this is the one kind in the store no reconciliation can rebuild.
    /// </summary>
    [Fact]
    public void ADocumentPastTheCapIsReadAndTrimmedAtTheNextWrite()
    {
        var document = OneConflict();
        var entries = (JsonArray)document.Fields["conflicts"]!;
        var entry = (JsonObject)entries[0]!;

        for (var each = 0; each < ConflictRecords.MaximumEntries; each++)
        {
            entries.Add(entry.DeepClone());
        }

        var reading = ConflictRecords.Read(Rebuilt(document.Fields));

        Assert.False(reading.IsRefused);
        Assert.Equal(ConflictRecords.MaximumEntries + 1, reading.Records!.Count);

        var trimmed = reading.Records!.With(
            Conflict(_episode, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening));

        Assert.Equal(ConflictRecords.MaximumEntries, trimmed.Count);
        Assert.Equal(_episode, trimmed.All[trimmed.Count - 1].ItemId);
    }

    /// <summary>
    /// Every rule the conflict table declares can be written down and read back, so a row landing
    /// in the resolver never meets a record that cannot name it. The set is read off the
    /// enumeration rather than listed here, so a member added to it joins this fact without
    /// anybody editing it.
    /// </summary>
    [Fact]
    public void EveryRuleAndEveryFieldSurvivesTheDocument()
    {
        var records = ConflictRecords.NoneYet(_pairing, _user);
        var written = new List<(SyncedField Field, ConflictRule Rule)>();

        foreach (var rule in Enum.GetValues<ConflictRule>())
        {
            foreach (var field in Enum.GetValues<SyncedField>())
            {
                written.Add((field, rule));
                records = records.With(
                    Conflict(_film, field, rule, 1, 0, ConflictSide.Neither, _evening));
            }
        }

        var reading = ConflictRecords.Read(records.ToDocument());

        Assert.False(reading.IsRefused);
        Assert.Equal(
            written,
            reading.Records!.All.Select(conflict => (conflict.Field, conflict.Rule)).ToList());
    }

    /// <summary>
    /// A record of one conflict, as the document it writes.
    /// </summary>
    /// <returns>The document.</returns>
    private static StoredDocument OneConflict() =>
        ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, _evening))
            .ToDocument();

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

    private static ConflictRecord Conflict(
        Guid item,
        SyncedField field,
        ConflictRule rule,
        long? here,
        long? atThePeer,
        ConflictSide discarded,
        DateTimeOffset recordedAt) =>
        new ConflictRecord(_pairing, _user, item, field, rule, here, atThePeer, discarded, recordedAt);

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Api;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Matching;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using Jellyfin.Plugin.WatchSync.Transfer;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The status surface, which is #62 as far as the records reach.
///
/// The rule the whole issue turns on is that every number is read from the record the code uses
/// and never counted separately for display, so the facts here write records through the record
/// types' own writers and assert the status answers what those records answer. What is asserted
/// beside that is what the surface adds: that an absent record and an unreadable one are told
/// apart, that a stopped run is prominent, that nothing about another person is answered, and
/// that no title is anywhere in the shape.
///
/// Nothing here starts a server or a request. The controller is constructed with the store and
/// the record the sweep keeps its last run in, and called, and the routing is held by the
/// comparisons in <c>EndpointPolicyTests</c>,
/// <c>EndpointDocumentTests</c> and <c>ConfigurationPageActionsTests</c> against the attributes.
/// </summary>
public sealed class SyncStatusControllerTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _person = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _somebodyElse = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _peer = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _evening = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 9, 3, 20, 0, 0, DateTimeKind.Utc);

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStatusControllerTests"/> class.
    /// </summary>
    public SyncStatusControllerTests()
    {
        _programData = TemporaryDirectory.Create("status");
        Directory.CreateDirectory(DataPath);
    }

    private string DataPath => Path.Combine(_programData.FullPath, "data");

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The first condition. Each item is read from the record that produces it: the count of
    /// unmatched items is the unmatched record's own count and the reasons are grouped out of its
    /// entries, the conflicts are the conflict record's count and newest moment, the last exchange
    /// is the watermark, the stopped run is the plan, and the last sweep is the run the task
    /// recorded, member for member.
    /// </summary>
    [Fact]
    public void EveryNumberIsReadFromTheRecordThatProducesIt()
    {
        var store = Store();
        var sweeps = new SweepRuns();
        var run = SweepRun.Over(_evening.AddHours(3), 4)
            .HavingExamined(1)
            .HavingExamined(0)
            .HavingExamined(2)
            .HavingExamined(0)
            .Ended(_evening.AddHours(3).AddMinutes(2));

        sweeps.Record(run);

        var unmatched = UnmatchedRecords.NoneYet(_pairing, _person)
            .With(Unmatched(Film(1), MatchKeyRefusal.NoIdentifierAtAll, null))
            .With(Unmatched(Film(2), MatchKeyRefusal.NoIdentifierAtAll, null))
            .With(Unmatched(Film(3), MatchKeyRefusal.None, MatchAnswer.Ambiguous))
            .With(Unmatched(Film(4), MatchKeyRefusal.NoSeasonNumber, null))
            .With(Unmatched(Film(5), MatchKeyRefusal.None, MatchAnswer.NoMatch));

        var conflicts = ConflictRecords.NoneYet(_pairing, _person)
            .With(Conflict(Film(1), _evening))
            .With(Conflict(Film(2), _evening.AddHours(2)));

        var agreed = AgreedRecords.NoneYet(_pairing, _person)
            .With(new AgreedRecord(Subject(_person, Film(6)), new SyncedState(true, 1, 0, _watchedAt), _evening, 1))
            .At(Watermark.Confirmed("a-point", _evening.AddHours(1)).Mark!);

        store.Write(UnmatchedRecords.DocumentName(_pairing, _person), _ => unmatched.ToDocument());
        store.Write(ConflictRecords.DocumentName(_pairing, _person), _ => conflicts.ToDocument());
        store.Write(AgreedRecords.DocumentName(_pairing, _person), _ => agreed.ToDocument());
        store.Write(StoppedRun.DocumentName(_pairing, _person), _ => Plan().ToDocument());

        var status = Answer(new SyncStatusController(store, sweeps).Status(_pairing, _person));

        Assert.Equal(_pairing, status.PairingId);
        Assert.Equal(_person, status.MappedUserId);

        Assert.Equal(RecordReading.Read, status.Unmatched.Reading);
        Assert.Equal(unmatched.Count, status.Unmatched.Count);
        Assert.Equal(
            new[] { ("NoIdentifierAtAll", 2), ("Ambiguous", 1), ("NoMatch", 1) },
            status.Unmatched.Reasons.Select(reason => (reason.Reason, reason.Count)).ToArray());

        Assert.Equal(RecordReading.Read, status.Conflicts.Reading);
        Assert.Equal(conflicts.Count, status.Conflicts.Count);
        Assert.Equal(_evening.AddHours(2), status.Conflicts.NewestRecordedAt);

        Assert.Equal(RecordReading.Read, status.LastExchange.Reading);
        Assert.True(status.LastExchange.HasEverExchanged);
        Assert.Equal(_evening.AddHours(1), status.LastExchange.ConfirmedAt);
        Assert.Equal(agreed.Count, status.LastExchange.AgreedItems);

        Assert.Equal(RecordReading.Read, status.StoppedRun.Reading);
        Assert.True(status.StoppedRun.IsStopped);
        Assert.Equal(RunCapAnswer.ExceedsShare, status.StoppedRun.Answer);
        Assert.Equal(3, status.StoppedRun.Changes);
        Assert.Equal(2, status.StoppedRun.Allowed);
        Assert.Equal(20, status.StoppedRun.Matched);
        Assert.Equal(_evening, status.StoppedRun.StoppedAt);

        Assert.True(status.LastSweep.IsRecorded);
        Assert.False(status.LastSweep.StoppedShort);
        Assert.Equal(run.StartedAt, status.LastSweep.StartedAt);
        Assert.Equal(run.EndedAt, status.LastSweep.EndedAt);
        Assert.Equal(SweepRunOutcome.Covered, status.LastSweep.Outcome);
        Assert.Equal(run.Subjects, status.LastSweep.Subjects);
        Assert.Equal(run.Examined, status.LastSweep.Examined);
        Assert.Equal(run.Changed, status.LastSweep.Changed);
        Assert.Equal(3, status.LastSweep.Changed);
    }

    /// <summary>
    /// The last sweep is the server's run rather than the pairing's. The sweep walks the records
    /// the store holds rather than pairs today, so one run is over every pairing and every person
    /// at once, and a status about another pairing and another person answers the same run.
    /// </summary>
    [Fact]
    public void TheLastSweepIsTheServersAndIsAnsweredOnEveryStatus()
    {
        var sweeps = new SweepRuns();
        var run = SweepRun.Over(_evening, 2).HavingExamined(1).HavingExamined(0).Ended(_evening.AddMinutes(1));

        sweeps.Record(run);

        var controller = new SyncStatusController(Store(), sweeps);
        var here = Answer(controller.Status(_pairing, _person));
        var elsewhere = Answer(controller.Status(_otherPairing, _somebodyElse));

        foreach (var status in new[] { here, elsewhere })
        {
            Assert.True(status.LastSweep.IsRecorded);
            Assert.Equal(run.StartedAt, status.LastSweep.StartedAt);
            Assert.Equal(run.EndedAt, status.LastSweep.EndedAt);
            Assert.Equal(run.Subjects, status.LastSweep.Subjects);
            Assert.Equal(run.Examined, status.LastSweep.Examined);
            Assert.Equal(run.Changed, status.LastSweep.Changed);
        }
    }

    /// <summary>
    /// No sweep since the server started is said rather than shown as a run over nothing. The
    /// record is held in memory and a restart loses it, so the absence means the task has not run
    /// to its end since the server started, and zeros would read as a run that examined nothing
    /// and changed nothing.
    /// </summary>
    [Fact]
    public void NoSweepSinceTheServerStartedIsSaidRatherThanShownAsZeros()
    {
        var status = Answer(new SyncStatusController(Store(), new SweepRuns()).Status(_pairing, _person));

        Assert.False(status.LastSweep.IsRecorded);
        Assert.False(status.LastSweep.StoppedShort);
        Assert.Null(status.LastSweep.Outcome);
        Assert.Null(status.LastSweep.StartedAt);
        Assert.Null(status.LastSweep.EndedAt);
        Assert.Null(status.LastSweep.Subjects);
        Assert.Null(status.LastSweep.Examined);
        Assert.Null(status.LastSweep.Changed);
        Assert.False(status.NeedsAttention);
    }

    /// <summary>
    /// The second condition, for the sweep. A run that stopped short needs attention, because its
    /// counts look like a run that finished and what it did not reach was not trimmed; a run that
    /// covered its set does not, and the last run to end is the one that decides.
    /// </summary>
    [Fact]
    public void ASweepThatStoppedShortMakesTheStatusNeedAttentionAndACoveredOneDoesNot()
    {
        var sweeps = new SweepRuns();
        var controller = new SyncStatusController(Store(), sweeps);

        sweeps.Record(SweepRun.Over(_evening, 2).HavingExamined(0).HavingExamined(0).Ended(_evening.AddMinutes(1)));

        var covered = Answer(controller.Status(_pairing, _person));

        Assert.False(covered.NeedsAttention);
        Assert.False(covered.LastSweep.StoppedShort);
        Assert.Equal(SweepRunOutcome.Covered, covered.LastSweep.Outcome);

        sweeps.Record(SweepRun.Over(_evening.AddHours(1), 2).HavingExamined(1).Ended(_evening.AddHours(1).AddMinutes(1)));

        var stopped = Answer(controller.Status(_pairing, _person));

        Assert.True(stopped.NeedsAttention);
        Assert.True(stopped.LastSweep.StoppedShort);
        Assert.Equal(SweepRunOutcome.StoppedShort, stopped.LastSweep.Outcome);
        Assert.Equal(2, stopped.LastSweep.Subjects);
        Assert.Equal(1, stopped.LastSweep.Examined);
        Assert.Equal(RecordReading.Absent, stopped.StoppedRun.Reading);
    }

    /// <summary>
    /// On the page, the banner that shows a stopped run is filled from the sweep that stopped
    /// short as well, so a page cannot show the counts and miss the sweep. It reads that the
    /// script names the member and nothing about what a browser renders.
    /// </summary>
    [Fact]
    public void ThePageFillsTheBannerFromASweepThatStoppedShort()
    {
        var page = File.ReadAllText(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync",
            "Configuration",
            "configPage.html"));

        Assert.Contains("status.LastSweep.StoppedShort", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The top reasons are the top few and not all of them, most frequent first, so the status is
    /// a glance and the export is the list.
    /// </summary>
    [Fact]
    public void TheStatusNamesTheTopReasonsAndTheExportNamesEveryItem()
    {
        var store = Store();

        var unmatched = UnmatchedRecords.NoneYet(_pairing, _person)
            .With(Unmatched(Film(1), MatchKeyRefusal.NoIdentifierAtAll, null))
            .With(Unmatched(Film(2), MatchKeyRefusal.NoSeasonNumber, null))
            .With(Unmatched(Film(3), MatchKeyRefusal.NoEpisodeNumber, null))
            .With(Unmatched(Film(4), MatchKeyRefusal.SpansSeveralEpisodes, null))
            .With(Unmatched(Film(5), MatchKeyRefusal.SpansSeveralEpisodes, null));

        store.Write(UnmatchedRecords.DocumentName(_pairing, _person), _ => unmatched.ToDocument());

        var controller = new SyncStatusController(store, new SweepRuns());
        var status = Answer(controller.Status(_pairing, _person));
        var export = Answer(controller.Unmatched(_pairing, _person));

        Assert.Equal(SyncStatusController.ReasonsShown, status.Unmatched.Reasons.Count);
        Assert.Equal("SpansSeveralEpisodes", status.Unmatched.Reasons[0].Reason);
        Assert.Equal(2, status.Unmatched.Reasons[0].Count);
        Assert.Equal(5, status.Unmatched.Count);

        Assert.Equal(RecordReading.Read, export.Reading);
        Assert.Equal(unmatched.Count, export.Count);
        Assert.Equal(
            unmatched.All.Select(entry => entry.ItemId).ToList(),
            export.Items.Select(entry => entry.ItemId).ToList());
        Assert.All(export.Items, entry => Assert.Equal(nameof(BaseItemKind.Movie), entry.Kind));
        Assert.Equal("SpansSeveralEpisodes", export.Items[4].Refusal);
        Assert.Null(export.Items[4].Answer);
        Assert.All(export.Items, entry => Assert.Equal(_evening, entry.LastAttemptedAt));
    }

    /// <summary>
    /// The second condition. A stopped run is prominent rather than a line among others: the
    /// first member of the status says whether anything needs an operator, and it is derived
    /// from the stopped run so a page cannot show the counts and miss the stop.
    /// </summary>
    [Fact]
    public void AStoppedRunMakesTheStatusNeedAttentionAndNothingStoppedDoesNot()
    {
        var store = Store();

        var quiet = Answer(new SyncStatusController(store, new SweepRuns()).Status(_pairing, _person));

        Assert.False(quiet.NeedsAttention);
        Assert.False(quiet.StoppedRun.IsStopped);

        store.Write(StoppedRun.DocumentName(_pairing, _person), _ => Plan().ToDocument());

        var stopped = Answer(new SyncStatusController(store, new SweepRuns()).Status(_pairing, _person));

        Assert.True(stopped.NeedsAttention);
        Assert.True(stopped.StoppedRun.IsStopped);
    }

    /// <summary>
    /// A record that could not be read is told apart from one that is not there, and it needs
    /// attention, because both would read as zero if the surface counted for itself and zero
    /// reads as fine.
    /// </summary>
    [Fact]
    public void AnUnreadableRecordIsToldApartFromAnAbsentOneAndNeedsAttention()
    {
        var store = Store();

        store.Write(UnmatchedRecords.DocumentName(_pairing, _person), _ => NotARecord());

        var controller = new SyncStatusController(store, new SweepRuns());
        var status = Answer(controller.Status(_pairing, _person));
        var export = Answer(controller.Unmatched(_pairing, _person));

        Assert.Equal(RecordReading.Unreadable, status.Unmatched.Reading);
        Assert.Equal(0, status.Unmatched.Count);
        Assert.Equal(RecordReading.Absent, status.Conflicts.Reading);
        Assert.Equal(RecordReading.Absent, status.LastExchange.Reading);
        Assert.Equal(RecordReading.Absent, status.StoppedRun.Reading);
        Assert.True(status.NeedsAttention);

        Assert.Equal(RecordReading.Unreadable, export.Reading);
        Assert.Empty(export.Items);
    }

    /// <summary>
    /// Every section tells an unreadable document apart from an absent one, not only the one the
    /// fact above happens to write. A document that is a document and is not the kind the name
    /// says is the shape somebody produces by hand.
    /// </summary>
    [Fact]
    public void EverySectionTellsAnUnreadableDocumentApart()
    {
        var store = Store();

        store.Write(StoppedRun.DocumentName(_pairing, _person), _ => NotARecord());
        store.Write(AgreedRecords.DocumentName(_pairing, _person), _ => NotARecord());
        store.Write(ConflictRecords.DocumentName(_pairing, _person), _ => NotARecord());

        var status = Answer(new SyncStatusController(store, new SweepRuns()).Status(_pairing, _person));

        Assert.Equal(RecordReading.Unreadable, status.StoppedRun.Reading);
        Assert.False(status.StoppedRun.IsStopped);
        Assert.Null(status.StoppedRun.Answer);
        Assert.Equal(RecordReading.Unreadable, status.LastExchange.Reading);
        Assert.False(status.LastExchange.HasEverExchanged);
        Assert.Equal(RecordReading.Unreadable, status.Conflicts.Reading);
        Assert.Null(status.Conflicts.NewestRecordedAt);
        Assert.True(status.NeedsAttention);
    }

    /// <summary>
    /// A document a newer version of this plugin wrote is unreadable rather than absent. A status
    /// answering nothing for it would tell an operator a sync has never run on a pairing that is
    /// running under the newer plugin on the other server.
    /// </summary>
    [Fact]
    public void ADocumentFromTheFutureIsUnreadableRatherThanAbsent()
    {
        var store = Store();

        store.Write(
            ConflictRecords.DocumentName(_pairing, _person),
            _ => StoredDocument.Read(
                new JsonObject { ["version"] = DocumentVersions.Current + 1 }.ToJsonString(),
                DocumentVersions.Current + 2).Document!);

        var status = Answer(new SyncStatusController(store, new SweepRuns()).Status(_pairing, _person));

        Assert.Equal(RecordReading.Unreadable, status.Conflicts.Reading);
        Assert.True(status.NeedsAttention);
    }

    /// <summary>
    /// A pairing that has never exchanged and a person nothing is recorded about answer with every
    /// record absent, and it is the same answer for a pairing that does not exist, which is the
    /// deliberate sameness <c>docs/endpoints.md</c> declares: this plugin cannot tell them apart
    /// without asking the pairing plugin, and an answer that did would say which pairings exist.
    /// </summary>
    [Fact]
    public void APairingNeverExchangedOnAndAPairingThatDoesNotExistAreAnsweredTheSame()
    {
        var store = Store();

        store.Write(UnmatchedRecords.DocumentName(_pairing, _person), _ => UnmatchedRecords.NoneYet(_pairing, _person).ToDocument());

        var controller = new SyncStatusController(store, new SweepRuns());
        var never = Answer(controller.Status(_otherPairing, _person));
        var none = Answer(controller.Status(new Guid("99999999-9999-9999-9999-999999999999"), _person));

        foreach (var status in new[] { never, none })
        {
            Assert.False(status.NeedsAttention);
            Assert.Equal(RecordReading.Absent, status.StoppedRun.Reading);
            Assert.Equal(RecordReading.Absent, status.LastExchange.Reading);
            Assert.Equal(RecordReading.Absent, status.Unmatched.Reading);
            Assert.Equal(RecordReading.Absent, status.Conflicts.Reading);
            Assert.False(status.LastExchange.HasEverExchanged);
            Assert.Null(status.LastExchange.ConfirmedAt);
        }
    }

    /// <summary>
    /// The fourth condition, kept where the data is fetched. Records about somebody else under the
    /// same pairing, and about this person under another pairing, are not in this person's
    /// status or export, because the reads are by the name that carries both identifiers.
    /// </summary>
    [Fact]
    public void NothingAboutAnotherPersonOrAnotherPairingIsAnswered()
    {
        var store = Store();

        var theirs = UnmatchedRecords.NoneYet(_pairing, _somebodyElse)
            .With(Unmatched(Film(7), MatchKeyRefusal.NoIdentifierAtAll, null));
        var elsewhere = UnmatchedRecords.NoneYet(_otherPairing, _person)
            .With(Unmatched(Film(8), MatchKeyRefusal.NoIdentifierAtAll, null));

        store.Write(UnmatchedRecords.DocumentName(_pairing, _somebodyElse), _ => theirs.ToDocument());
        store.Write(UnmatchedRecords.DocumentName(_otherPairing, _person), _ => elsewhere.ToDocument());
        store.Write(StoppedRun.DocumentName(_pairing, _somebodyElse), _ => Plan(_somebodyElse).ToDocument());

        var controller = new SyncStatusController(store, new SweepRuns());
        var status = Answer(controller.Status(_pairing, _person));
        var export = Answer(controller.Unmatched(_pairing, _person));

        Assert.False(status.NeedsAttention);
        Assert.Equal(RecordReading.Absent, status.Unmatched.Reading);
        Assert.Equal(RecordReading.Absent, status.StoppedRun.Reading);
        Assert.Equal(RecordReading.Absent, export.Reading);
        Assert.Empty(export.Items);
    }

    /// <summary>
    /// The other half of the fourth condition: there is no title anywhere in the shape to
    /// withhold. Every string in the answer is the name of an enumeration member, read by
    /// reflection over the answer types rather than by trusting the fact above to have looked.
    /// </summary>
    [Fact]
    public void NoAnswerTypeCarriesATitleAPathOrAnyStringButAnEnumerationsName()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Reason", "Kind", "Refusal", "Answer" };
        var refused = new[] { "Title", "Name", "Path", "Text", "Message" };

        var findings = AnswerTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (Type: type, Property: property)))
            .Where(each =>
                refused.Any(word => each.Property.Name.Contains(word, StringComparison.Ordinal))
                || (each.Property.PropertyType == typeof(string) && !allowed.Contains(each.Property.Name)))
            .Select(each => $"{each.Type.Name}.{each.Property.Name} is a string the status could carry a title in.")
            .ToList();

        Assert.Empty(findings);
        Assert.NotEmpty(AnswerTypes());
    }

    /// <summary>
    /// An identifier naming nobody, or no pairing, is refused by both endpoints before the store
    /// is read. The all-zero identifier is what an empty field on a page sends.
    /// </summary>
    [Fact]
    public void AnIdentifierNamingNobodyOrNoPairingIsRefusedByBoth()
    {
        var controller = new SyncStatusController(Store(), new SweepRuns());

        Assert.IsType<BadRequestResult>(controller.Status(Guid.Empty, _person).Result);
        Assert.IsType<BadRequestResult>(controller.Status(_pairing, Guid.Empty).Result);
        Assert.IsType<BadRequestResult>(controller.Unmatched(Guid.Empty, _person).Result);
        Assert.IsType<BadRequestResult>(controller.Unmatched(_pairing, Guid.Empty).Result);
    }

    /// <summary>
    /// On the page, the stopped run is shown above the rest of the status rather than as a line
    /// among the counts, which is the half of the second condition a reading of the markup can
    /// see. It reads the order of two identifiers and nothing about what a browser renders.
    /// </summary>
    [Fact]
    public void ThePageShowsTheStoppedRunAboveTheRestOfTheStatus()
    {
        var page = File.ReadAllText(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync",
            "Configuration",
            "configPage.html"));

        var banner = page.IndexOf("id=\"WatchSyncStoppedRun\"", StringComparison.Ordinal);
        var output = page.IndexOf("id=\"WatchSyncStatusOutput\"", StringComparison.Ordinal);

        Assert.True(banner >= 0, "the page has no element for a stopped run");
        Assert.True(output >= 0, "the page has no element for the status");
        Assert.True(banner < output, "the stopped run is shown below the status rather than above it");
    }

    /// <summary>
    /// Every type an answer is made of, found by reflection over what the two endpoints return
    /// rather than listed here.
    /// </summary>
    /// <returns>The types.</returns>
    private static IReadOnlyList<Type> AnswerTypes()
    {
        var found = new List<Type>();
        var pending = new Queue<Type>(new[] { typeof(SyncStatus), typeof(UnmatchedExport) });

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();

            if (found.Contains(type))
            {
                continue;
            }

            found.Add(type);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propertyType = property.PropertyType;
                var element = propertyType.IsGenericType ? propertyType.GetGenericArguments()[0] : propertyType;

                if (element.Namespace == typeof(SyncStatus).Namespace && !element.IsEnum)
                {
                    pending.Enqueue(element);
                }
            }
        }

        return found;
    }

    private static T Answer<T>(ActionResult<T> answer)
    {
        Assert.Null(answer.Result);
        Assert.NotNull(answer.Value);

        return answer.Value!;
    }

    private static Guid Film(int index) => new Guid($"66666666-0000-0000-0000-{index:D12}");

    private static TransferSubject Subject(Guid person, Guid itemId)
    {
        var reading = TransferSubject.From(person, itemId, BaseItemKind.Movie);

        Assert.True(reading.IsSubject);

        return reading.Value!;
    }

    private static UnmatchedRecord Unmatched(Guid itemId, MatchKeyRefusal refusal, MatchAnswer? answer) =>
        new UnmatchedRecord(itemId, BaseItemKind.Movie, refusal, answer, _evening);

    private static ConflictRecord Conflict(Guid itemId, DateTimeOffset recordedAt) =>
        new ConflictRecord(
            _pairing,
            _person,
            itemId,
            SyncedField.Played,
            ConflictRule.Ratchet,
            1,
            0,
            ConflictSide.AtThePeer,
            recordedAt);

    private static StoppedRun Plan() => Plan(_person);

    private static StoppedRun Plan(Guid person)
    {
        var verdict = RunCap.Judge(changes: 3, matched: 20, maximumChanges: 100, maximumShare: 0.10);

        return StoppedRun.Of(
            _pairing,
            person,
            _peer,
            1,
            verdict,
            20,
            Enumerable.Range(1, 3)
                .Select(index => StoppedRunItem.Read(
                    Subject(person, Film(index)),
                    new SyncedState(true, 1, 0, _watchedAt),
                    null))
                .ToList(),
            _evening);
    }

    private static StoredDocument NotARecord()
    {
        var fields = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
            ["who"] = JsonValue.Create("somebody assembled this by hand"),
        };

        return StoredDocument.Read(fields.ToJsonString(), DocumentVersions.Current).Document!;
    }

    private DocumentStore Store()
    {
        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new DocumentStore(new StoreFolder(paths.Object));
    }
}

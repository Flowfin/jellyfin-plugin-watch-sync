using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Matching;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using Jellyfin.Plugin.WatchSync.Transfer;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The scheduled sweep as a task the server runs, which is #55's first condition, and the run it
/// records, which is the fifth condition of the same issue met by the plugin rather than by the
/// rule alone.
///
/// The server finds a task by scanning the types it loaded and constructs it against its own
/// service provider, so the two halves of appearing in the dashboard are that the type is one
/// the scan finds and that the constructor can be satisfied from what the registrator hands out.
/// Both are held here without a server, the second by doing exactly what the server does.
///
/// What a run does is held against a store on a temporary folder with a clock this suite moves:
/// what is past its retention leaves, what is not stays, a store with nothing to trim is not
/// written to, a cancelled run is recorded as one that stopped short, and a configuration the
/// rules refuse runs nothing and says which setting.
///
/// The run also rebuilds the match index, which is #29's first condition arriving through the
/// sweep: an item the library gained that no event carried to the index is found after a run,
/// and the task runs at server start so the index is built there rather than by whichever
/// lookup comes first.
/// </summary>
public sealed class ScheduledSweepTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _otherUser = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _peerUser = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _episode = new("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset _night = new(2026, 9, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledSweepTests"/> class.
    /// </summary>
    public ScheduledSweepTests()
    {
        _programData = TemporaryDirectory.Create("sweep");
        Directory.CreateDirectory(DataPath);
    }

    private string DataPath => Path.Join(_programData.FullPath, "data");

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The half of appearing in the dashboard that is about the type. The server adds every
    /// concrete exported type implementing the task interface, so this reads the assembly the
    /// same way and asks that the sweep is among them and is the only one, because a second
    /// task arriving unnoticed would be a second thing running on its own that the privacy note
    /// does not describe.
    /// </summary>
    [Fact]
    public void TheSweepIsATaskTheServerFindsByScanningTheAssembly()
    {
        var tasks = typeof(Plugin).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IScheduledTask).IsAssignableFrom(type))
            .ToList();

        Assert.Equal(new[] { typeof(ScheduledSweep) }, tasks);

        var sweep = Sweep(Configured(), Store(), new SweepRuns());

        Assert.Equal(ScheduledSweep.TaskKey, sweep.Key);
        Assert.False(string.IsNullOrWhiteSpace(sweep.Name));
        Assert.False(string.IsNullOrWhiteSpace(sweep.Description));
        Assert.False(string.IsNullOrWhiteSpace(sweep.Category));
    }

    /// <summary>
    /// The other half, and the one that fails harder. The server constructs a task against its
    /// own provider, and a constructor asking for anything nothing registered fails the whole
    /// assembly rather than the task. This builds the provider the registrator fills, stands a
    /// fake in for every interface the server would hand over, and constructs the sweep the way
    /// the server does.
    /// </summary>
    [Fact]
    public void TheServerCanConstructTheSweepFromWhatTheRegistratorHandsOut()
    {
        var services = new ServiceCollection();

        new ServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        foreach (var collaborator in services
            .ToList()
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null)
            .SelectMany(type => type!.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(IsAServerCollaborator)
            .Distinct())
        {
            services.AddSingleton(collaborator, Fake(collaborator));
        }

        using var provider = services.BuildServiceProvider();

        var sweep = ActivatorUtilities.CreateInstance<ScheduledSweep>(provider);

        Assert.Equal(ScheduledSweep.TaskKey, sweep.Key);
    }

    /// <summary>
    /// The schedule is the setting, which is #55's second condition reaching the task that
    /// reads it. One interval trigger, at the interval the configuration carries, and it is the
    /// only interval trigger, because two intervals would be two schedules for one number.
    /// </summary>
    [Fact]
    public void TheDefaultTriggerIsTheConfiguredInterval()
    {
        var sweep = Sweep(Configured(configuration => configuration.SweepIntervalMinutes = 45), Store(), new SweepRuns());

        var trigger = Assert.Single(sweep.GetDefaultTriggers(), each => each.Type == TaskTriggerInfoType.IntervalTrigger);

        Assert.Equal(TimeSpan.FromMinutes(45).Ticks, trigger.IntervalTicks);
    }

    /// <summary>
    /// The task runs at server start, which is how the index is built on start rather than by
    /// whichever lookup comes first, and how what the events missed while the server was down
    /// is picked up before the first interval elapses. One startup trigger beside the interval,
    /// and nothing else: a third trigger would be a schedule no setting describes.
    /// </summary>
    [Fact]
    public void TheSweepRunsAtServerStartAndThenAtTheInterval()
    {
        var sweep = Sweep(Configured(), Store(), new SweepRuns());

        var triggers = sweep.GetDefaultTriggers().ToList();

        Assert.Equal(2, triggers.Count);
        Assert.Single(triggers, each => each.Type == TaskTriggerInfoType.StartupTrigger);
        Assert.Single(triggers, each => each.Type == TaskTriggerInfoType.IntervalTrigger);
    }

    /// <summary>
    /// A configuration the rules refuse still has to schedule something, because a task with
    /// no trigger never runs and the refusal is then shown to nobody. It schedules the rule's
    /// own default, and the run that fires is what carries the refusal to the dashboard.
    /// </summary>
    [Fact]
    public void ARefusedConfigurationSchedulesTheRulesOwnDefault()
    {
        var sweep = Sweep(Configured(configuration => configuration.SweepIntervalMinutes = 0), Store(), new SweepRuns());

        var trigger = Assert.Single(sweep.GetDefaultTriggers(), each => each.Type == TaskTriggerInfoType.IntervalTrigger);

        Assert.Equal(SweepSchedule.DefaultInterval.Ticks, trigger.IntervalTicks);
    }

    /// <summary>
    /// The whole of what the rebuild is for. The index was built while the library held one
    /// film, a second film arrived and no event carried it to the index, and after a run the
    /// index answers the second film's key with the item. Nothing else in this suite touches
    /// the index between the two lookups, so the run is the only thing that could have moved
    /// the answer. The second run rebuilds again rather than finding the index built and
    /// stopping, which is what "rebuilt by the sweep" means as against "built once".
    /// </summary>
    [Fact]
    public async Task ARunRebuildsTheIndexSoAnItemNoEventReachedIsFoundAfterIt()
    {
        var library = new Library(new KeyedItem(_film, MatchKey.Of(Identifier(1))));
        var index = new MatchIndex(library);
        var runs = new SweepRuns();

        Assert.False(index.Lookup(MatchKey.Of(Identifier(2))).IsMatched);

        library.Add(new KeyedItem(_episode, MatchKey.Of(Identifier(2))));

        Assert.False(index.Lookup(MatchKey.Of(Identifier(2))).IsMatched);

        var walksBefore = library.Walks;

        await Sweep(Configured(), Store(), runs, index).ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(walksBefore + 1, library.Walks);
        Assert.Equal(_episode, index.Lookup(MatchKey.Of(Identifier(2))).Item);
        Assert.Equal(SweepRunOutcome.Covered, runs.Last!.Outcome);

        await Sweep(Configured(), Store(), runs, index).ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(walksBefore + 2, library.Walks);
    }

    /// <summary>
    /// The whole point of what a run converges today. Three conflicts, one past a thirty day
    /// retention; two writes of provenance, one past a ninety day retention and one inside it
    /// that a thirty day retention would have taken, so a sweep reading the wrong setting for
    /// the record shows. After the run each record holds what is inside its retention and
    /// nothing else, and the run says it was over two subjects, examined both, changed two
    /// entries and covered its set. A document of another kind in the same store is not a
    /// subject.
    /// </summary>
    [Fact]
    public async Task ARunTrimsWhatIsPastItsRetentionAndKeepsTheRest()
    {
        var store = Store();
        var runs = new SweepRuns();

        store.Write(ConflictRecords.DocumentName(_pairing, _user), _ => ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, _night.AddDays(-40)))
            .With(Conflict(_episode, _night.AddDays(-20)))
            .With(Conflict(_film, _night.AddDays(-1)))
            .ToDocument());
        store.Write(ProvenanceRecords.DocumentName(_pairing, _user), _ => ProvenanceRecords.NoneYet(_pairing, _user)
            .With(Write(_film, _night.AddDays(-100)))
            .With(Write(_episode, _night.AddDays(-60)))
            .ToDocument());
        store.Write(UnmatchedRecords.DocumentName(_pairing, _user), _ => UnmatchedRecords.NoneYet(_pairing, _user).ToDocument());

        var sweep = Sweep(
            Configured(configuration =>
            {
                configuration.ConflictRetentionDays = 30;
                configuration.ProvenanceRetentionDays = 90;
            }),
            store,
            runs);

        await sweep.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        var conflicts = ConflictRecords.Read(store.Read(ConflictRecords.DocumentName(_pairing, _user))!.Document!).Records!;
        var provenance = ProvenanceRecords.Read(store.Read(ProvenanceRecords.DocumentName(_pairing, _user))!.Document!).Records!;

        Assert.Equal(new[] { _night.AddDays(-20), _night.AddDays(-1) }, conflicts.All.Select(conflict => conflict.RecordedAt));
        Assert.Equal(new[] { _night.AddDays(-60) }, provenance.All.Select(write => write.WrittenAt));

        var run = runs.Last!;

        Assert.Equal(SweepRunOutcome.Covered, run.Outcome);
        Assert.Equal(2, run.Subjects);
        Assert.Equal(2, run.Examined);
        Assert.Equal(2, run.Changed);
    }

    /// <summary>
    /// A record with nothing past its retention is not rewritten. A write moves the document's
    /// generation and stales every other writer's attempt, so a sweep that rewrote every record
    /// every interval would be paying that for no change. The count of files opened for writing
    /// is what says so, because a rewrite with the same bytes leaves nothing else to read.
    /// </summary>
    [Fact]
    public async Task ARunWithNothingPastRetentionWritesNothing()
    {
        var opened = 0;
        var store = Store(path =>
        {
            opened++;

            return File.Create(path);
        });
        var runs = new SweepRuns();

        store.Write(ConflictRecords.DocumentName(_pairing, _user), _ => ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, _night.AddDays(-1)))
            .ToDocument());
        store.Write(ProvenanceRecords.DocumentName(_pairing, _otherUser), _ => ProvenanceRecords.NoneYet(_pairing, _otherUser)
            .With(Write(_episode, _night.AddDays(-2), _otherUser))
            .ToDocument());

        var written = opened;

        await Sweep(Configured(), store, runs).ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(written, opened);
        Assert.Equal(SweepRunOutcome.Covered, runs.Last!.Outcome);
        Assert.Equal(2, runs.Last.Examined);
        Assert.Equal(0, runs.Last.Changed);
    }

    /// <summary>
    /// A store holding nothing is a run over nothing, covered, with its two moments from the
    /// clock the sweep was handed, read once each. Both moments are asserted because a task
    /// reading any other clock would record one this suite did not set, and a task taking its
    /// end from its start would record the first twice.
    /// </summary>
    [Fact]
    public async Task ARunOverAnEmptyStoreIsCoveredOverNothingAndTakesItsMomentsFromTheClock()
    {
        var clock = new Clock(_night);
        var runs = new SweepRuns();
        var sweep = new ScheduledSweep(clock, Store(), Configured(), runs, new MatchIndex(new Library()));
        var reported = new List<double>();

        await sweep.ExecuteAsync(new Reporter(reported), CancellationToken.None);

        var run = runs.Last!;

        Assert.Equal(SweepRunOutcome.Covered, run.Outcome);
        Assert.Equal(0, run.Subjects);
        Assert.Equal(_night, run.StartedAt);
        Assert.Equal(_night.AddMinutes(1), run.EndedAt);
        Assert.Equal(100.0, reported.Last());
    }

    /// <summary>
    /// A cancellation from the dashboard ends the run where it stands, and what is recorded is a
    /// run that stopped short. The cancellation arrives through the progress the run reports
    /// after its first subject, so the run is over two, examined one, and the second record is
    /// left as it was.
    /// </summary>
    [Fact]
    public async Task ACancelledRunIsRecordedAsOneThatStoppedShort()
    {
        var store = Store();
        var runs = new SweepRuns();

        store.Write(ConflictRecords.DocumentName(_pairing, _user), _ => ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, _night.AddDays(-40)))
            .ToDocument());
        store.Write(ConflictRecords.DocumentName(_pairing, _otherUser), _ => ConflictRecords.NoneYet(_pairing, _otherUser)
            .With(Conflict(_film, _night.AddDays(-40), _otherUser))
            .ToDocument());

        using var cancellation = new CancellationTokenSource();

        await Sweep(Configured(), store, runs)
            .ExecuteAsync(new Reporter(new List<double>(), cancellation), cancellation.Token);

        var run = runs.Last!;

        Assert.Equal(SweepRunOutcome.StoppedShort, run.Outcome);
        Assert.Equal(2, run.Subjects);
        Assert.Equal(1, run.Examined);
        Assert.Equal(1, run.Changed);

        var kept = store.Names()
            .Select(name => ConflictRecords.Read(store.Read(name)!.Document!).Records!.Count)
            .OrderBy(count => count)
            .ToArray();

        Assert.Equal(new[] { 0, 1 }, kept);
    }

    /// <summary>
    /// A configuration the rules refuse runs nothing. The task fails with the refusal, naming
    /// the setting, what was found and what it had to satisfy, and the record it would have
    /// trimmed is untouched. No run is recorded, because none happened, and the library is not
    /// walked either: nothing means nothing, and a rebuild before the refusal would be a run
    /// that did half its work and then reported it did none.
    /// </summary>
    [Fact]
    public async Task ARefusedConfigurationRunsNothingAndNamesTheSetting()
    {
        var store = Store();
        var runs = new SweepRuns();
        var library = new Library();

        store.Write(ConflictRecords.DocumentName(_pairing, _user), _ => ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, _night.AddDays(-400)))
            .ToDocument());

        var sweep = Sweep(Configured(configuration => configuration.ConflictRetentionDays = 0), store, runs, new MatchIndex(library));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sweep.ExecuteAsync(new Progress<double>(), CancellationToken.None));

        Assert.Contains(nameof(PluginConfiguration.ConflictRetentionDays), refused.Message, StringComparison.Ordinal);
        Assert.Contains(" is 0 ", refused.Message, StringComparison.Ordinal);
        Assert.Null(runs.Last);
        Assert.Equal(0, library.Walks);
        Assert.Equal(1, ConflictRecords.Read(store.Read(ConflictRecords.DocumentName(_pairing, _user))!.Document!).Records!.Count);
    }

    /// <summary>
    /// A document the sweep cannot read is examined, left alone and counted as no change: one
    /// under a conflict name that is not a record of conflicts, and one from a version this
    /// code does not know. Each has a reader elsewhere that says what is wrong with it, and a
    /// sweep that rewrote either would be destroying the evidence.
    /// </summary>
    [Fact]
    public async Task AnUnreadableRecordIsExaminedAndLeftAlone()
    {
        var opened = 0;
        var store = Store(path =>
        {
            opened++;

            return File.Create(path);
        });
        var runs = new SweepRuns();

        store.Write(ConflictRecords.DocumentName(_pairing, _user), _ => StoredDocument.Read(
            new JsonObject
            {
                ["version"] = DocumentVersions.Current,
                ["nothing"] = "a record of conflicts would carry",
            }.ToJsonString(),
            DocumentVersions.Current).Document!);
        store.Write(ProvenanceRecords.DocumentName(_pairing, _user), _ => StoredDocument.Read(
            new JsonObject { ["version"] = DocumentVersions.Current + 1 }.ToJsonString(),
            DocumentVersions.Current + 2).Document!);

        var written = opened;

        await Sweep(Configured(), store, runs).ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(written, opened);
        Assert.Equal(SweepRunOutcome.Covered, runs.Last!.Outcome);
        Assert.Equal(2, runs.Last.Examined);
        Assert.Equal(0, runs.Last.Changed);
    }

    /// <summary>
    /// A write the store refuses stops the run rather than counting the subject as examined.
    /// The run record has one word for a subject examined with no change and no word for a
    /// subject whose trim was refused, and reporting the second as the first is the reading
    /// that record exists to refuse. The failure reaches the dashboard as the task failing, and
    /// no run is recorded.
    /// </summary>
    [Fact]
    public async Task AWriteTheStoreRefusesStopsTheRunRatherThanCountingTheSubject()
    {
        var refuse = false;
        var store = Store(path => refuse ? throw new IOException("the disk is full") : File.Create(path));
        var runs = new SweepRuns();

        store.Write(ConflictRecords.DocumentName(_pairing, _user), _ => ConflictRecords.NoneYet(_pairing, _user)
            .With(Conflict(_film, _night.AddDays(-400)))
            .ToDocument());

        refuse = true;

        var sweep = Sweep(Configured(), store, runs);

        var stopped = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sweep.ExecuteAsync(new Progress<double>(), CancellationToken.None));

        Assert.Contains(nameof(DocumentWriteOutcome.RefusedByTheFilesystem), stopped.Message, StringComparison.Ordinal);
        Assert.Null(runs.Last);
    }

    private static ConflictRecord Conflict(Guid item, DateTimeOffset recordedAt, Guid? user = null) =>
        new ConflictRecord(_pairing, user ?? _user, item, SyncedField.Played, ConflictRule.Ratchet, 1, 0, ConflictSide.AtThePeer, recordedAt);

    private static ProvenanceRecord Write(Guid item, DateTimeOffset writtenAt, Guid? user = null) =>
        new ProvenanceRecord(_pairing, user ?? _user, _peerUser, item, SyncedField.Played, 0, 1, writtenAt);

    private static ScheduledSweep Sweep(StoredSettings settings, DocumentStore store, SweepRuns runs, MatchIndex? index = null) =>
        new ScheduledSweep(new Clock(_night), store, settings, runs, index ?? new MatchIndex(new Library()));

    /// <summary>
    /// An identifier the numbering of a test can produce, in the one spelling a key compares.
    /// </summary>
    /// <param name="number">The number, which is never zero because no provider allocates it.</param>
    /// <returns>The identifier.</returns>
    private static ProviderIdentifier Identifier(int number)
    {
        var reading = ProviderIdentifier.Normalise(
            IdentifierProvider.Tmdb,
            number.ToString(CultureInfo.InvariantCulture));

        Assert.True(reading.IsUsable);

        return reading.Identifier!;
    }

    /// <summary>
    /// The settings as a server would hold them, on a plugin instance the server's manager hands
    /// over, with the defaults unless a change is asked for.
    /// </summary>
    /// <param name="change">What to change about the defaults.</param>
    /// <returns>The settings.</returns>
    private static StoredSettings Configured(Action<PluginConfiguration>? change = null)
    {
        var configuration = new PluginConfiguration();

        change?.Invoke(configuration);

        return new StoredSettings(StoredSettingsTests.ManagerHolding(configuration));
    }

    private static object Fake(Type type) =>
        ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object;

    private static bool IsAServerCollaborator(Type type)
    {
        var assembly = type.Assembly.GetName().Name ?? string.Empty;

        return type.IsInterface
            && type.Assembly != typeof(Plugin).Assembly
            && (assembly.StartsWith("Jellyfin.", StringComparison.Ordinal)
                || assembly.StartsWith("MediaBrowser.", StringComparison.Ordinal));
    }

    private DocumentStore Store() => new DocumentStore(Folder());

    private DocumentStore Store(Func<string, Stream> openForWriting) => new DocumentStore(Folder(), openForWriting);

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }

    /// <summary>
    /// A library the index reads, counting the walks the index makes over it. A walk is a read
    /// of its first page, because that is the one read every walk makes whatever the library
    /// holds, so the count says how many times the index was rebuilt and not how large the
    /// library was.
    /// </summary>
    private sealed class Library : IMatchIndexSource
    {
        private readonly List<KeyedItem> _items;

        internal Library(params KeyedItem[] items)
        {
            _items = items.ToList();
        }

        internal int Walks { get; private set; }

        internal void Add(KeyedItem item) => _items.Add(item);

        public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count)
        {
            if (startIndex == 0)
            {
                Walks++;
            }

            return _items.Skip(startIndex).Take(count).ToList();
        }
    }

    /// <summary>
    /// A clock this suite sets, which is what the sweep is handed in place of the runtime's own.
    /// It moves a minute on every read, so that the end of a run is a moment its start is not
    /// and a run that took its end from anywhere but the clock is told apart.
    /// </summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now;

        internal Clock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            var read = _now;

            _now = _now.AddMinutes(1);

            return read;
        }
    }

    /// <summary>
    /// What the run reports its progress to, synchronously so that a fact reads the list after
    /// the run rather than after a scheduler got round to it, and optionally cancelling on the
    /// first report so that a cancellation lands between two subjects.
    /// </summary>
    private sealed class Reporter : IProgress<double>
    {
        private readonly List<double> _reported;
        private readonly CancellationTokenSource? _cancelOnFirstReport;

        internal Reporter(List<double> reported, CancellationTokenSource? cancelOnFirstReport = null)
        {
            _reported = reported;
            _cancelOnFirstReport = cancelOnFirstReport;
        }

        public void Report(double value)
        {
            _reported.Add(value);
            _cancelOnFirstReport?.Cancel();
        }
    }
}

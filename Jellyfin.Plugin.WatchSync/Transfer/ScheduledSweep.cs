using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Matching;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// The scheduled sweep, as a task the server runs, which is #55's first condition.
///
/// The server finds a task by scanning the types it loaded, so this appears in the dashboard's
/// task list without being registered, and it constructs one against its own service provider,
/// so everything the constructor asks for is something the registrator hands out. A constructor
/// asking for anything else does not merely leave the task out of the list: the server fails
/// the whole assembly and the operator sees a plugin that is there and does nothing.
///
/// <para>
/// What a run converges today is what can be converged without a peer. There is no pairing
/// adapter yet, which is #40, so no exchange starts here and no watch state moves; a run walks
/// the conflict and provenance records this plugin keeps and trims what is older than the
/// retention an operator set. Until this task those two settings were read by nothing, so the
/// number an operator typed was the number the rule would take and no rule ran. The exchange
/// arrives with the adapter and takes its place in the same walk, under the same record.
/// </para>
///
/// <para>
/// Before the records are walked, the match index is rebuilt from the library, which is #29's
/// first condition: the index is built on start and rebuilt by the sweep, so a change to the
/// library that no event reached is in the index by the next run rather than for ever absent
/// from it. The rebuild walks the library one page at a time and swaps a finished map in whole,
/// so a lookup during it is answered from the map before rather than from an empty one, and
/// the task runs at server start as well as at the interval, which is what makes the first half
/// of that condition true through the one thing in this plugin the server runs unasked.
/// </para>
///
/// <para>
/// The run is recorded as <see cref="SweepRun"/> says it must be: the subjects are declared
/// before the walk starts, one result is taken per subject, and whether the run covered them
/// is derived from the counts when it ends. A cancellation from the dashboard ends the run
/// where it stands, so what is recorded is a run that stopped short rather than a run that
/// finished. The two moments come from the injected clock and nothing here reads another.
/// </para>
///
/// <para>
/// A configuration the rules refuse runs nothing. The task fails with the refusal, which the
/// dashboard shows against the task and the log carries, and no record is trimmed against a
/// retention nobody chose. That is #61's rule met on the first path that runs on its own.
/// </para>
/// </summary>
public sealed class ScheduledSweep : IScheduledTask
{
    /// <summary>
    /// The key the server files this task's triggers and history under. It is a constant so
    /// that a rename of the class does not orphan an operator's saved schedule.
    /// </summary>
    public const string TaskKey = "WatchSyncSweep";

    private readonly TimeProvider _clock;
    private readonly DocumentStore _store;
    private readonly StoredSettings _settings;
    private readonly SweepRuns _runs;
    private readonly MatchIndex _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledSweep"/> class.
    /// </summary>
    /// <param name="clock">The injected clock, which is the one the composition root hands out.</param>
    /// <param name="store">The store this plugin's records are in.</param>
    /// <param name="settings">The settings as the server holds them.</param>
    /// <param name="runs">Where the last run is kept for a reader other than this task.</param>
    /// <param name="index">The match index this run rebuilds from the library.</param>
    public ScheduledSweep(TimeProvider clock, DocumentStore store, StoredSettings settings, SweepRuns runs, MatchIndex index)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(index);

        _clock = clock;
        _store = store;
        _settings = settings;
        _runs = runs;
        _index = index;
    }

    /// <inheritdoc />
    public string Name => "Watch Sync sweep";

    /// <inheritdoc />
    public string Key => TaskKey;

    /// <inheritdoc />
    public string Description =>
        "Rebuilds the match index from the library, trims this plugin's conflict and provenance records past their retention, and records what the run examined. Nothing is exchanged with a peer yet.";

    /// <inheritdoc />
    public string Category => "Watch Sync";

    /// <summary>
    /// The schedule the server files for this task where an operator has not set one.
    ///
    /// Two triggers. One run at server start, because the index has to be built on start and
    /// what the events missed while the server was down is exactly what a sweep is for, and
    /// then one at the interval. The interval is the setting `docs/configuration.md` carries,
    /// read at the moment the server asks, and the rule's own default where the configuration
    /// is refused. What the server does with the pair is the bound to read carefully: it asks
    /// once, when it first meets the task, and keeps what an operator later sets in the
    /// dashboard in preference. So a change to the setting reaches the schedule at the next
    /// server start, and not at all on a server whose operator edited the schedule in the
    /// dashboard, which is then the home of the interval for that server, and an operator who
    /// removes the startup run there has removed it. #55 decided that this is the answer rather
    /// than a state to repair: the operator's edit wins, nothing here writes the server's saved
    /// schedule back, and `schedule-not-rewritten` refuses the write that would.
    /// </summary>
    /// <returns>A startup trigger and one interval trigger.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var reading = _settings.Read();

        var interval = reading.IsRead ? reading.SweepInterval!.Value : SweepSchedule.DefaultInterval;

        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger,
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = interval.Ticks,
            },
        };
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var settings = _settings.Read();

        if (!settings.IsRead)
        {
            throw new InvalidOperationException(RefusedConfiguration(settings.Refusals));
        }

        // The index first, so that a lookup made by anything the walk below will one day do is
        // answered from a map that has seen the library as it is now rather than as it was at
        // the last event. The rebuild swaps a finished map in whole, so nothing reading the
        // index while it runs sees an empty one.
        _index.Rebuild();

        var subjects = Subjects();
        var run = SweepRun.Over(_clock.GetUtcNow(), subjects.Count);

        foreach (var subject in subjects)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            run = run.HavingExamined(Trimmed(subject, settings));

            progress.Report(100.0 * run.Examined / run.Subjects);
        }

        _runs.Record(run.Ended(_clock.GetUtcNow()));

        progress.Report(100.0);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The documents a run is over: every conflict record and every provenance record the store
    /// holds. The set is read once, before the walk, so the run's denominator is fixed and a
    /// document written during the walk is the next run's.
    /// </summary>
    /// <returns>The document names.</returns>
    private List<string> Subjects()
    {
        var subjects = new List<string>();

        foreach (var name in _store.Names())
        {
            if (name.StartsWith(ConflictRecords.NamePrefix, StringComparison.Ordinal)
                || name.StartsWith(ProvenanceRecords.NamePrefix, StringComparison.Ordinal))
            {
                subjects.Add(name);
            }
        }

        return subjects;
    }

    /// <summary>
    /// Trims one document to its retention, and answers how many entries left it.
    ///
    /// The document is read first and written only where the read found something past the
    /// retention, so a sweep over records with nothing to trim writes nothing: a write moves
    /// the document's generation, and a sweep that rewrote every record every interval would be
    /// stale-ing every other writer's attempt for no change. The trim is computed again inside
    /// the write from what the store holds at that moment, so an entry recorded between the
    /// read and the write is kept rather than dropped with the ones the read decided on.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <param name="settings">The settings, read and accepted.</param>
    /// <returns>How many entries were removed.</returns>
    /// <exception cref="InvalidOperationException">
    /// The store refused the write, or the document went away or stopped being readable between
    /// the read and the write. The run fails rather than counting the subject as examined,
    /// because a subject reported as examined with no change and a subject whose trim was
    /// refused are the two facts the run record exists to keep apart, and it has no third
    /// word for the second.
    /// </exception>
    private int Trimmed(string name, ServerWideSettingsReading settings)
    {
        var reading = _store.Read(name);

        if (reading is null || reading.Answer != DocumentAnswer.Current)
        {
            // Absent, from the future, not a document, or older than this code: none of those
            // is this walk's to touch, and each has its own reader elsewhere. Examined, no
            // change.
            return 0;
        }

        var from = RetainedFrom(name, settings);
        var pastRetention = PastRetention(name, reading.Document!, from);

        if (pastRetention == 0)
        {
            return 0;
        }

        var removed = 0;

        var answer = _store.Write(name, current =>
        {
            if (current is null || current.Answer != DocumentAnswer.Current)
            {
                throw new InvalidOperationException(
                    "The document went away or stopped being readable between the sweep's read and its write, and the sweep writes nothing in its place rather than putting back what something else removed.");
            }

            var trimmed = Trimmed(name, current.Document!, from, out removed);

            return trimmed;
        });

        if (answer.IsRefused)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The store refused the write that would have trimmed a record: {answer.Outcome}. The run stops here rather than reporting the record as examined."));
        }

        return removed;
    }

    /// <summary>
    /// The moment before which an entry of this document is past its retention.
    /// </summary>
    /// <param name="name">The document's name, which says which retention applies.</param>
    /// <param name="settings">The settings, read and accepted.</param>
    /// <returns>The moment.</returns>
    private DateTimeOffset RetainedFrom(string name, ServerWideSettingsReading settings)
    {
        var retention = name.StartsWith(ConflictRecords.NamePrefix, StringComparison.Ordinal)
            ? settings.ConflictRetention!.Value
            : settings.ProvenanceRetention!.Value;

        return _clock.GetUtcNow() - retention;
    }

    /// <summary>
    /// How many entries of a document are past its retention, without writing anything.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <param name="document">The document as read.</param>
    /// <param name="from">The moment before which an entry is past its retention.</param>
    /// <returns>The count, which is zero for a document the record's reader refuses.</returns>
    private static int PastRetention(string name, StoredDocument document, DateTimeOffset from)
    {
        if (name.StartsWith(ConflictRecords.NamePrefix, StringComparison.Ordinal))
        {
            var conflicts = ConflictRecords.Read(document);

            return conflicts.IsRefused
                ? 0
                : conflicts.Records!.Count - conflicts.Records.Retaining(from).Count;
        }

        var provenance = ProvenanceRecords.Read(document);

        return provenance.IsRefused
            ? 0
            : provenance.Records!.Count - provenance.Records.Retaining(from).Count;
    }

    /// <summary>
    /// The document trimmed to its retention, computed from what the store holds at the moment
    /// of the write.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <param name="document">The document as the store holds it now.</param>
    /// <param name="from">The moment before which an entry is past its retention.</param>
    /// <param name="removed">How many entries the trim removed.</param>
    /// <returns>The trimmed document, or the document as it was where its reader refuses it.</returns>
    private static StoredDocument Trimmed(string name, StoredDocument document, DateTimeOffset from, out int removed)
    {
        if (name.StartsWith(ConflictRecords.NamePrefix, StringComparison.Ordinal))
        {
            var conflicts = ConflictRecords.Read(document);

            if (conflicts.IsRefused)
            {
                removed = 0;

                return document;
            }

            var retained = conflicts.Records!.Retaining(from);

            removed = conflicts.Records.Count - retained.Count;

            return retained.ToDocument();
        }

        var provenance = ProvenanceRecords.Read(document);

        if (provenance.IsRefused)
        {
            removed = 0;

            return document;
        }

        var kept = provenance.Records!.Retaining(from);

        removed = provenance.Records.Count - kept.Count;

        return kept.ToDocument();
    }

    /// <summary>
    /// What a run says when it refuses to start on a configuration the rules refuse. It names
    /// every refused setting with what was found and what it had to satisfy, which is what the
    /// page says about the same values, and it carries nothing about anybody's viewing.
    /// </summary>
    /// <param name="refusals">The refusals.</param>
    /// <returns>The sentence.</returns>
    private static string RefusedConfiguration(IReadOnlyList<SettingRefusal> refusals)
    {
        var named = new List<string>();

        foreach (var refusal in refusals)
        {
            named.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{refusal.Setting} is {refusal.Found} and has to be {refusal.Bound}"));
        }

        return "The sweep did not run, because the configuration is refused and a sweep on a retention nobody chose would trim records against it: "
            + string.Join("; ", named)
            + ".";
    }
}

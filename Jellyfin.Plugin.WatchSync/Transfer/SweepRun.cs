using System;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// What one run of the scheduled sweep did, which is #55's fifth condition: its start, its end,
/// what it examined and what it changed, so that a run which covered less than everything cannot
/// be read as one that covered it all.
///
/// The failure this is written against is not a run that goes wrong. It is a run that stops -
/// cancelled from the dashboard, cut off by a shutdown, abandoned because a peer went away - and
/// leaves behind a record indistinguishable from a run that finished. An operator reading counts
/// alone concludes that everything was looked at and that the two servers agree about the rest,
/// and the drift the sweep exists to remove goes on being invisible with a green run beside it.
///
/// <para>
/// So the run is over a set that is declared before the walk starts, and coverage is derived from
/// how much of that set was reached rather than asserted by whoever ends the run. A caller cannot
/// hand this record a verdict: it hands over subjects, then one result per subject, then an end,
/// and the answer follows. That is the whole of the design decision here, and it is why
/// <see cref="Over"/> takes a count that <see cref="HavingExamined(int)"/> is then held under
/// rather than a walk reporting its own total at the end.
/// </para>
///
/// <para>
/// It reads no clock. Both moments arrive as parameters, which is the shape every rule in this
/// plugin takes and the reason the suite can drive a run of any length without waiting for one.
/// </para>
///
/// <para>
/// Nothing calls this yet. The task that would produce one is the first condition of the same
/// issue, and what a sweep would converge needs the exchange <c>docs/transfer.md</c> fixes and
/// the pairing adapter in #40. This is the record that run will be written into rather than a
/// claim that a sweep runs.
/// </para>
/// </summary>
public sealed class SweepRun
{
    private SweepRun(
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        int subjects,
        int examined,
        int changed,
        SweepRunOutcome outcome)
    {
        StartedAt = startedAt;
        EndedAt = endedAt;
        Subjects = subjects;
        Examined = examined;
        Changed = changed;
        Outcome = outcome;
    }

    /// <summary>
    /// Gets the moment the run started, as its caller was given it.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets the moment the run ended, or nothing where it has not ended.
    ///
    /// The absence is an answer rather than a missing field, and <see cref="Outcome"/> carries
    /// the same fact in the form a reader is more likely to ask for.
    /// </summary>
    public DateTimeOffset? EndedAt { get; }

    /// <summary>
    /// Gets how many subjects the run set out over.
    ///
    /// This is the denominator the fifth condition is about. It is fixed when the run starts,
    /// because a total collected as the walk goes is the same number as what was examined and
    /// says nothing about what was left.
    /// </summary>
    public int Subjects { get; }

    /// <summary>
    /// Gets how many of those subjects the run has examined.
    /// </summary>
    public int Examined { get; }

    /// <summary>
    /// Gets how many changes the run made across the subjects it examined.
    /// </summary>
    public int Changed { get; }

    /// <summary>
    /// Gets what the run came to.
    /// </summary>
    public SweepRunOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the run ended having reached every subject it was over.
    ///
    /// It is derived rather than stored, so there is no second place for the answer to disagree
    /// with the counts it is drawn from.
    /// </summary>
    public bool IsCovered => Outcome == SweepRunOutcome.Covered;

    /// <summary>
    /// Starts a run over a declared set of subjects.
    /// </summary>
    /// <param name="startedAt">The moment the run started.</param>
    /// <param name="subjects">
    /// How many subjects the run is over. A subject is one pairing and one mapped user, which is
    /// the pair <c>docs/transfer.md</c> holds an exchange to and this does not restate.
    /// </param>
    /// <returns>The run, before anything has been examined.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The count of subjects is below zero.</exception>
    public static SweepRun Over(DateTimeOffset startedAt, int subjects)
    {
        if (subjects < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjects),
                subjects,
                "A run cannot be over fewer than no subjects.");
        }

        return new SweepRun(startedAt, null, subjects, 0, 0, SweepRunOutcome.Running);
    }

    /// <summary>
    /// Takes in one subject the run has examined, and what examining it changed.
    ///
    /// A subject examined that changed nothing is reported the same way as one that changed
    /// something, with a count of no changes, because the two are different facts and a walk
    /// reporting only the second would leave a quiet subject and an unreached one identical.
    /// </summary>
    /// <param name="changed">How many changes examining that subject made.</param>
    /// <returns>The run with that subject counted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The count of changes is below zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The run has already ended, or this subject is one more than the run was over.
    /// </exception>
    public SweepRun HavingExamined(int changed)
    {
        if (changed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(changed),
                changed,
                "Examining a subject cannot have made fewer than no changes.");
        }

        if (Outcome != SweepRunOutcome.Running)
        {
            throw new InvalidOperationException(
                "The run has ended, and a subject examined after the end is a caller reporting one walk as two.");
        }

        if (Examined == Subjects)
        {
            throw new InvalidOperationException(
                "The run is over fewer subjects than have been examined. Coverage is derived from the set the run declared, so a walk finding more of them is a walk over a different set rather than a run that covered this one.");
        }

        return new SweepRun(
            StartedAt,
            null,
            Subjects,
            Examined + 1,
            Changed + changed,
            SweepRunOutcome.Running);
    }

    /// <summary>
    /// Ends the run.
    ///
    /// Whether it covered its subjects is decided here out of what was examined, and the caller
    /// says only when it stopped. A run cut off part way ends exactly the same way as one that
    /// finished, which is deliberate: the caller that was cancelled is the caller least able to
    /// describe its own coverage, and asking it to would put the answer in the hands of the case
    /// this record exists for.
    /// </summary>
    /// <param name="endedAt">The moment the run ended.</param>
    /// <returns>The ended run.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The end is before the start.</exception>
    /// <exception cref="InvalidOperationException">The run has already ended.</exception>
    public SweepRun Ended(DateTimeOffset endedAt)
    {
        if (Outcome != SweepRunOutcome.Running)
        {
            throw new InvalidOperationException(
                "The run has already ended, and a second end would move a moment a record already carries.");
        }

        if (endedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAt),
                endedAt,
                "The run ended before it started. Both moments come from the one injected clock in one process, so this is a caller mixing two sources rather than a run that took no time.");
        }

        return new SweepRun(
            StartedAt,
            endedAt,
            Subjects,
            Examined,
            Changed,
            Examined == Subjects ? SweepRunOutcome.Covered : SweepRunOutcome.StoppedShort);
    }
}

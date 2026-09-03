namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What reading one document as the record of a run the cap stopped came back with.
/// </summary>
public sealed class StoppedRunReading
{
    private StoppedRunReading(StoppedRunAnswer answer, StoppedRun? run)
    {
        Answer = answer;
        Run = run;
    }

    /// <summary>
    /// Gets what the document turned out to be.
    /// </summary>
    public StoppedRunAnswer Answer { get; }

    /// <summary>
    /// Gets the run, where the document is one.
    /// </summary>
    public StoppedRun? Run { get; }

    /// <summary>
    /// Gets a value indicating whether the document was refused.
    /// </summary>
    public bool IsRefused => Answer is not StoppedRunAnswer.Readable;

    /// <summary>
    /// A document that is a stopped run.
    /// </summary>
    /// <param name="run">What it holds.</param>
    /// <returns>The reading.</returns>
    internal static StoppedRunReading Readable(StoppedRun run) =>
        new StoppedRunReading(StoppedRunAnswer.Readable, run);

    /// <summary>
    /// A document that is not a stopped run.
    /// </summary>
    /// <returns>The reading.</returns>
    internal static StoppedRunReading NotAStoppedRun() =>
        new StoppedRunReading(StoppedRunAnswer.NotAStoppedRun, null);
}

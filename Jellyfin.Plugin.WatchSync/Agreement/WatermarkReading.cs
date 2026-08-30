namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What reading a point offered by the far side as a watermark came back with.
/// </summary>
public sealed class WatermarkReading
{
    private WatermarkReading(WatermarkAnswer answer, Watermark? watermark)
    {
        Answer = answer;
        Mark = watermark;
    }

    /// <summary>
    /// Gets what the point turned out to be.
    /// </summary>
    public WatermarkAnswer Answer { get; }

    /// <summary>
    /// Gets the watermark, where the point is one this record may carry.
    /// </summary>
    public Watermark? Mark { get; }

    /// <summary>
    /// Gets a value indicating whether the point was refused.
    /// </summary>
    public bool IsRefused => Answer is not WatermarkAnswer.Readable;

    /// <summary>
    /// A point this record may carry.
    /// </summary>
    /// <param name="watermark">The watermark it makes.</param>
    /// <returns>The reading.</returns>
    internal static WatermarkReading Readable(Watermark watermark) =>
        new WatermarkReading(WatermarkAnswer.Readable, watermark);

    /// <summary>
    /// A point this record may not carry.
    /// </summary>
    /// <param name="answer">Which of the three it is.</param>
    /// <returns>The reading.</returns>
    internal static WatermarkReading Refused(WatermarkAnswer answer) =>
        new WatermarkReading(answer, null);
}

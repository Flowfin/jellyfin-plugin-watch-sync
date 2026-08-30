using System;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// The point up to which this server and one peer have agreed, for one pairing and one mapped
/// user, so that a server that was off for a week asks for what changed rather than for
/// everything.
///
/// It is an opaque value the far side produced and never a moment this server read. Two clocks
/// that disagree turn a timestamp watermark into one of two permanent faults: a gap, where this
/// server asks from a point later than changes it never received, or a re-send of the same
/// window every time. Neither announces itself, and both are avoided by never comparing the
/// point to anything but itself. Nothing here parses it, orders two of them, or takes a
/// substring of one.
///
/// The moment beside it is this server's own and is a parameter rather than a clock this type
/// reads, which the injected-clock invariant refuses a departure from. It is here because an
/// operator looking at a pairing that has stopped moving needs to know when the point was last
/// confirmed, and that question cannot be answered out of an opaque value.
///
/// It advances in one place. The transfer document fixes that as the last step of an exchange,
/// after the agreed record is written, and every other way an exchange can end leaves it where
/// it was. <see cref="After"/> is that rule, and the case it exists against is the one this
/// issue leads with: a watermark moved when a send was made rather than when the far side
/// confirmed, which loses every change in between and loses it silently.
///
/// The watermark lives in <see cref="AgreedRecords"/>, which is the document of one pairing and
/// one mapped user, so a restore of the agreed record restores the point it was agreed at. Kept
/// apart, a store restored from a backup could offer a peer a point later than the agreements it
/// holds, and every item between the two would be one neither side ever mentions again.
/// </summary>
public sealed class Watermark
{
    private Watermark(string point, DateTimeOffset confirmedAt)
    {
        Point = point;
        ConfirmedAt = confirmedAt;
    }

    /// <summary>
    /// Gets the watermark of a pairing and a user that have confirmed no point.
    ///
    /// A pairing and a user that have never exchanged, and a pairing whose point the peer no
    /// longer recognises, are both here. It is a value rather than a null so a caller that
    /// forgot the case cannot reach a rule holding nothing at all.
    /// </summary>
    public static Watermark NoneYet { get; } = new Watermark(string.Empty, default);

    /// <summary>
    /// Gets the point, as the far side wrote it. Empty where none has been confirmed.
    /// </summary>
    public string Point { get; }

    /// <summary>
    /// Gets when this server confirmed the point, by this server's own clock.
    /// </summary>
    public DateTimeOffset ConfirmedAt { get; }

    /// <summary>
    /// Gets a value indicating whether no point has been confirmed.
    /// </summary>
    public bool IsNoneYet => Point.Length == 0;

    /// <summary>
    /// Gets what the next exchange for this pairing and this mapped user asks the peer for.
    /// </summary>
    public NextExchange Asks =>
        IsNoneYet ? NextExchange.FullReconciliation : NextExchange.SinceTheWatermark;

    /// <summary>
    /// The watermark a point the far side named makes, or the reason it is not one.
    /// </summary>
    /// <param name="point">The point, as the far side wrote it.</param>
    /// <param name="confirmedAt">When this server confirmed it, by this server's own clock.</param>
    /// <returns>The reading.</returns>
    public static WatermarkReading Confirmed(string? point, DateTimeOffset confirmedAt)
    {
        if (string.IsNullOrWhiteSpace(point))
        {
            return WatermarkReading.Refused(WatermarkAnswer.NoPointAtAll);
        }

        if (CountedAsAReaderSeesThem(point) > EnvelopeBounds.LongestStringLength)
        {
            return WatermarkReading.Refused(WatermarkAnswer.TooLong);
        }

        foreach (var rune in point.EnumerateRunes())
        {
            if (IsNotPlainText(rune))
            {
                return WatermarkReading.Refused(WatermarkAnswer.NotPlainText);
            }
        }

        return WatermarkReading.Readable(new Watermark(point, confirmedAt));
    }

    /// <summary>
    /// Whether this watermark stands at one particular point.
    ///
    /// Ordinal equality over the whole value and nothing else. A comparison that ignored case,
    /// normalised the string or trimmed it would answer that two points are one when the far
    /// side, which produced them, says they are two.
    /// </summary>
    /// <param name="point">The point to compare against.</param>
    /// <returns>True where this watermark stands at exactly that point.</returns>
    public bool IsAt(string? point) =>
        !IsNoneYet && string.Equals(Point, point, StringComparison.Ordinal);

    /// <summary>
    /// The watermark this record carries after an exchange ended.
    /// </summary>
    /// <param name="end">How the exchange ended.</param>
    /// <param name="confirmed">
    /// The point the far side confirmed, read before it is handed over. It is ignored on every
    /// ending but <see cref="ExchangeEnd.ConfirmedTo"/>, because those endings confirm nothing
    /// and a value carried past them would be a point advanced on a failure.
    /// </param>
    /// <returns>The watermark to write.</returns>
    /// <exception cref="ArgumentNullException">The confirmed watermark is null.</exception>
    /// <exception cref="ArgumentException">
    /// The exchange is said to have confirmed a point and no point is handed over. That is a
    /// caller assembling an ending rather than reporting one, and answering it by leaving the
    /// watermark where it was would make an exchange that confirmed nothing indistinguishable
    /// from one that did.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The ending is not one of the three.</exception>
    public Watermark After(ExchangeEnd end, Watermark confirmed)
    {
        ArgumentNullException.ThrowIfNull(confirmed);

        switch (end)
        {
            case ExchangeEnd.ConfirmedTo when confirmed.IsNoneYet:
                throw new ArgumentException(
                    "An exchange that confirmed a point has to name it, and this one named none.",
                    nameof(confirmed));

            case ExchangeEnd.ConfirmedTo:
                return confirmed;

            case ExchangeEnd.PointNotRecognised:
                return NoneYet;

            case ExchangeEnd.NotConfirmed:
                return this;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(end),
                    end,
                    "An exchange ends in one of the three ways this type knows.");
        }
    }

    /// <summary>
    /// How many characters a reader sees, so a point of astral characters is bounded the way one
    /// of Latin letters is rather than at half the length.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The count.</returns>
    private static int CountedAsAReaderSeesThem(string point)
    {
        var count = 0;

        foreach (var rune in point.EnumerateRunes())
        {
            _ = rune.Value;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Whether one character may not appear in a point.
    /// </summary>
    /// <param name="rune">The character.</param>
    /// <returns>True where the point carrying it is refused.</returns>
    private static bool IsNotPlainText(Rune rune) =>
        Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.LineSeparator => true,
            UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
}

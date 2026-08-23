using System;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What one progress report is worth carrying, which is #17.
///
/// The server saves a progress report every few seconds for as long as something plays, and
/// <c>docs/sync-model.md</c> gives that reason the treatment <c>thresholded</c> rather than
/// <c>enqueued</c> for it. This is that treatment. A report the rule answers as not yet a
/// change is counted and carried no further, and what leaves is the position the playback
/// stopped at rather than one report in the middle of it.
///
/// Three of the four rules the issue fixes decide against carrying a position and one decides
/// to carry something else. That is the shape to keep in mind reading it: the rule exists to
/// refuse, and the one report per playback that has to survive it is the stop.
///
/// It is a rule over one report and its predecessor rather than over a stream. A stream is a
/// thing something has to hold between calls, and the handler in #15 holds nothing: it reads
/// the event, asks this, and returns. What stands in for the stream is the position last
/// carried, which comes out of the record of what the two sides last agreed in #14 and arrives
/// here as a parameter.
///
/// <c>docs/sync-model.md</c> holds the three numbers and the reason for each under
/// <c>## The position thresholds</c>, and <c>PositionThresholdDocumentTests</c> refuses that
/// section and <c>PositionThresholds</c> disagreeing.
/// </summary>
public sealed class PositionThreshold
{
    private PositionThreshold(
        PositionThresholdAnswer answer,
        long? position,
        bool theRuntimeWasNotKnown)
    {
        Answer = answer;
        Position = position;
        TheRuntimeWasNotKnown = theRuntimeWasNotKnown;
    }

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public PositionThresholdAnswer Answer { get; }

    /// <summary>
    /// Gets the position to carry, or null where the answer carries none.
    ///
    /// It is null for the finish as well as for the two refusals, and that is the finish rule
    /// rather than an omission: what the finish carries is that the person watched the work,
    /// and a resolution carrying both a played and a position would hand the receiving side the
    /// pair the ratchet in #31 exists to settle, invented on this side for no reason.
    /// </summary>
    public long? Position { get; }

    /// <summary>
    /// Gets a value indicating whether the item reached this rule with no runtime.
    ///
    /// A server that has not analysed an item yet holds no runtime for it, and two of the three
    /// rules here are about the length of the work: whether it is too short to carry a position
    /// at all, and whether this position is close enough to the end to be a finish. Neither can
    /// be asked without the number, so neither was asked, and the report was judged by the move
    /// and the stop alone.
    ///
    /// The fact is carried out rather than folded into the answer because the answer is what a
    /// caller acts on and this is what an operator needs told. A position carried without the
    /// finish rule having run is still bounded on the receiving side, where #28 refuses a
    /// position whose two runtimes are not within a minute of each other, so the failure this
    /// leaves open is a position near the end of a long item carried as a position. #62 is the
    /// surface this is read from.
    /// </summary>
    public bool TheRuntimeWasNotKnown { get; }

    /// <summary>
    /// Gets a value indicating whether the work is carried as watched by this answer.
    /// </summary>
    public bool CarriesPlayed => Answer == PositionThresholdAnswer.TheFinishIsCarriedAsPlayed;

    /// <summary>
    /// Judges one progress report against the position last carried for the same mapped user
    /// and the same leaf item.
    ///
    /// The order the three refusals are asked in is a decision rather than a convenience, and
    /// it is the order below. The length of the item is asked first, because an item nobody
    /// resumes carries no position whatever the report says, including a stop. The finish is
    /// asked next, because a stop at the end of a work is a finish and not a resume point, and
    /// a rule that asked the stop first would carry the end of every film as a position. The
    /// stop is asked before the move, because the whole reason the move threshold can be as
    /// coarse as it is that the stop always survives it.
    ///
    /// The two boundaries are drawn in opposite directions and a later reader should not tidy
    /// them into one. A position exactly the finish distance from the end is a finish, because
    /// the distance is the widest gap that still counts as the end. A move of exactly the
    /// threshold is not yet a change, because the threshold is the largest move that is still
    /// too small to carry. They are two questions about two numbers rather than one question
    /// asked twice.
    ///
    /// What this rule cannot see is stated here rather than left to be found. It reads two
    /// positions and a length and never a clock, so a report that arrived an hour late is
    /// judged exactly like one that arrived at once, and whether this report is newer than the
    /// position it is compared against is #32 on the receiving side. It also cannot tell a
    /// playback that stopped from one whose server was restarted: nothing arrives for the
    /// second, so the last carried position is where the peer resumes and the residual is the
    /// move threshold, which is written into <see cref="PositionThresholds.DefaultMove"/>.
    /// </summary>
    /// <param name="positionTicks">Where the person is now, in ticks.</param>
    /// <param name="positionLastCarriedTicks">
    /// The position last carried for this pair, in ticks, which is zero where none has been.
    /// It comes out of the record of what the two sides last agreed in #14.
    /// </param>
    /// <param name="thePlaybackStopped">
    /// Whether this report is the last of a session. It is the difference between the reason
    /// <c>PlaybackFinished</c> and the reason <c>PlaybackProgress</c> in the table under
    /// <c>## The reason the server gives when it saves</c>, read by the handler in #15 and
    /// handed here rather than re-derived.
    /// </param>
    /// <param name="runtime">
    /// How long the item is, or null where this server has not analysed it yet.
    /// </param>
    /// <param name="thresholds">The three numbers this rule is bounded by.</param>
    /// <returns>What the report is worth carrying.</returns>
    /// <exception cref="ArgumentNullException">The thresholds are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A position below zero, which the server does not produce and which reaches this rule
    /// only from a caller that computed one. A runtime at or below zero, which is not a length:
    /// an item the server has not analysed carries no runtime rather than a runtime of nothing,
    /// and the two are told apart here so that a caller passing zero for absent is refused
    /// instead of having every item read as too short to resume.
    /// </exception>
    public static PositionThreshold Judge(
        long positionTicks,
        long positionLastCarriedTicks,
        bool thePlaybackStopped,
        TimeSpan? runtime,
        PositionThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentOutOfRangeException.ThrowIfNegative(positionTicks, nameof(positionTicks));
        ArgumentOutOfRangeException.ThrowIfNegative(
            positionLastCarriedTicks,
            nameof(positionLastCarriedTicks));

        if (runtime is TimeSpan length && length <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtime),
                length,
                "An item this server has not analysed carries no runtime rather than a runtime of nothing, so absent is null here and never zero.");
        }

        var theRuntimeWasNotKnown = runtime is null;

        if (runtime is TimeSpan known)
        {
            if (known < thresholds.ShortestItem)
            {
                return new PositionThreshold(
                    PositionThresholdAnswer.TheItemIsTooShortToResume,
                    null,
                    false);
            }

            if (known.Ticks - positionTicks <= thresholds.Finish.Ticks)
            {
                return new PositionThreshold(
                    PositionThresholdAnswer.TheFinishIsCarriedAsPlayed,
                    null,
                    false);
            }
        }

        if (thePlaybackStopped)
        {
            return new PositionThreshold(
                PositionThresholdAnswer.TheStopIsCarried,
                positionTicks,
                theRuntimeWasNotKnown);
        }

        var moved = positionTicks > positionLastCarriedTicks
            ? positionTicks - positionLastCarriedTicks
            : positionLastCarriedTicks - positionTicks;

        if (moved > thresholds.Move.Ticks)
        {
            return new PositionThreshold(
                PositionThresholdAnswer.TheMoveIsCarried,
                positionTicks,
                theRuntimeWasNotKnown);
        }

        return new PositionThreshold(
            PositionThresholdAnswer.TheMoveIsNotYetAChange,
            null,
            theRuntimeWasNotKnown);
    }
}

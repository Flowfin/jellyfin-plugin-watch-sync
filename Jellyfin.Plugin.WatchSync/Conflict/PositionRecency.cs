using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// Where the person stopped, settled by the later play and bounded by the tolerated clock
/// skew, which is #32.
///
/// Two sides hold a position for one mapped user and one leaf item, neither holds the work
/// played, and the two positions disagree. The later play is the one the person was actually
/// doing, so it wins. That answer rests on comparing two moments recorded by two machines, and
/// two home servers can disagree by seconds with nothing wrong with either of them, or by
/// minutes when one has no time source at all.
///
/// So the comparison is bounded rather than trusted. A difference smaller than the tolerated
/// skew is not a comparison at all, and the tie rule applies instead: the greater position,
/// because it is the one the person is further into, written down as a choice rather than left
/// to whichever side the resolver happens to hold first.
///
/// The rule reads the last played date the server stores and never the moment an envelope
/// arrived. An arrival time measures the network and the queue, so a change that waited an hour
/// behind an offline peer would arrive as the newest thing either side had ever seen.
///
/// <c>docs/conflicts.md</c> holds the table this row belongs to and the reason each rule is the
/// rule. This type points at that document rather than restating it.
/// </summary>
public sealed class PositionRecency
{
    private PositionRecency(
        PositionAnswer answer,
        long? position,
        long? positionDiscardedHere,
        long? positionDiscardedAtThePeer)
    {
        Answer = answer;
        Position = position;
        PositionDiscardedHere = positionDiscardedHere;
        PositionDiscardedAtThePeer = positionDiscardedAtThePeer;
    }

    /// <summary>
    /// Gets the tolerated clock skew this rule is bounded by where an operator has chosen none.
    ///
    /// A minute. It is a choice with a reason rather than a measurement, and the reason is the
    /// shape of what it separates. A server whose time comes from a time source agrees with its
    /// peer to well inside a second, so a minute never swallows a genuine ordering on a pair of
    /// working machines. A server whose clock was set by hand, or that has been up for months
    /// without one, is out by minutes or by hours, and no tolerance narrow enough to be useful
    /// rescues that pair: it is a machine to repair rather than a reading to compare against.
    ///
    /// Nothing reads it yet. The setting an operator changes it with is #58, and this is the
    /// value that setting defaults to.
    /// </summary>
    public static TimeSpan DefaultToleratedSkew => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets the widest tolerated clock skew this rule accepts, above which it refuses rather
    /// than settling.
    ///
    /// A quarter of an hour, and it is a bound on the rule rather than advice to whoever sets
    /// the setting. Everything inside the tolerance is answered by the tie rule, so a tolerance
    /// wide enough to hold a real viewing session turns the tie rule into the rule and recency
    /// into the exception: a person reaches a materially different position inside a quarter of
    /// an hour, and two such plays would stop being compared at all.
    ///
    /// The refusal is here rather than only in the document, because a number a document
    /// declares and no code refuses is one a later caller passes straight through.
    /// </summary>
    public static TimeSpan MaximumToleratedSkew => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public PositionAnswer Answer { get; }

    /// <summary>
    /// Gets the position that is the answer for both sides, or null where nothing was decided.
    ///
    /// It is null for a completion, which is the ratchet's pair, and for a peer clock outside
    /// the tolerance, which is a reading this rule will not compare against.
    /// </summary>
    public long? Position { get; }

    /// <summary>
    /// Gets the position this server held that the answer discarded, or null where it discarded
    /// none.
    ///
    /// It is null rather than zero for a side that had not started, for the same reason the
    /// ratchet carries none: a position of zero is not something a person did, so recording it
    /// as a discarded value would fill #36's record with rows that name no loss.
    /// </summary>
    public long? PositionDiscardedHere { get; }

    /// <summary>
    /// Gets the position the peer held that the answer discarded, or null where it discarded
    /// none.
    /// </summary>
    public long? PositionDiscardedAtThePeer { get; }

    /// <summary>
    /// Gets a value indicating whether this resolution discarded a position somebody reached.
    ///
    /// This is the conflict #36 records with its loser, and the loser is whichever of the two
    /// discarded positions is not null. Two sides already at the same position disagree about
    /// nothing, so an answer over that pair is not a conflict and there is nothing to record.
    /// </summary>
    public bool IsAResolvedConflict =>
        PositionDiscardedHere is not null || PositionDiscardedAtThePeer is not null;

    /// <summary>
    /// Settles two positions against each other.
    ///
    /// The two boundaries this rule draws are opposite on purpose and a later reader should not
    /// tidy them into one. A difference of exactly the tolerated skew is a comparison, because
    /// the tolerance is the smallest difference that counts as real. A peer date exactly the
    /// tolerated skew ahead of the present moment is not outside it, because the tolerance is
    /// the largest offset that counts as ordinary. They are two questions about one number
    /// rather than one question asked twice.
    ///
    /// What this rule cannot see is stated here rather than left to be found. It reads the
    /// peer's date against the present moment in one direction only: a play recorded after the
    /// present moment cannot have happened, while a peer whose clock is behind produces an
    /// ordinary old reading that nothing here separates from an old play. And it compares the
    /// two dates as the instants the server stores, so a date that lost its offset somewhere
    /// earlier arrives here as a wrong instant and is compared as if it were right. That is
    /// what the envelope in #18 and the adapter in #20 are between this rule and a peer for.
    /// </summary>
    /// <param name="here">The state this server holds for the mapped user and the item.</param>
    /// <param name="atThePeer">The state the peer holds for the same pair.</param>
    /// <param name="toleratedSkew">
    /// How far apart two moments may be and still not be a comparison. The setting an operator
    /// chooses it with is #58, and <see cref="DefaultToleratedSkew"/> is what it defaults to.
    /// </param>
    /// <param name="now">
    /// This server's present moment, in UTC, taken from the injected clock. It is a parameter
    /// rather than a reading taken inside this rule, so that a test can put the boundary
    /// anywhere, and the invariant register refuses a source in this plugin that reaches for a
    /// clock instead.
    /// </param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Either side is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A position below zero, which the server does not produce and which reaches this rule only
    /// out of an envelope, where #19 bounds what one may carry. A tolerated skew below zero or
    /// above <see cref="MaximumToleratedSkew"/>. A present moment that is not UTC, which is the
    /// local zone read somewhere upstream: two servers agreeing on the instant to the second
    /// disagree by an hour once one of them has been through a local zone, and this rule would
    /// then name the peer's clock for a mistake made on this side.
    /// </exception>
    public static PositionRecency Settle(
        SyncedState here,
        SyncedState atThePeer,
        TimeSpan toleratedSkew,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(atThePeer);
        ArgumentOutOfRangeException.ThrowIfNegative(here.PlaybackPositionTicks, nameof(here));
        ArgumentOutOfRangeException.ThrowIfNegative(
            atThePeer.PlaybackPositionTicks,
            nameof(atThePeer));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            toleratedSkew,
            TimeSpan.Zero,
            nameof(toleratedSkew));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            toleratedSkew,
            MaximumToleratedSkew,
            nameof(toleratedSkew));

        if (now.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now.Kind,
                "The present moment reaches this rule in UTC, because the dates it is compared against are the instants the server stores.");
        }

        if (here.Played || atThePeer.Played)
        {
            return new PositionRecency(PositionAnswer.ACompletionIsHeld, null, null, null);
        }

        if (atThePeer.LastPlayedDate is DateTime atThePeerPlayed
            && atThePeerPlayed - now > toleratedSkew)
        {
            return new PositionRecency(PositionAnswer.PeerClockOutsideTolerance, null, null, null);
        }

        if (here.LastPlayedDate is not DateTime playedHere
            || atThePeer.LastPlayedDate is not DateTime playedAtThePeer
            || Apart(playedHere, playedAtThePeer) < toleratedSkew)
        {
            return Answered(
                PositionAnswer.TheGreaterPositionStands,
                here,
                atThePeer,
                here.PlaybackPositionTicks >= atThePeer.PlaybackPositionTicks);
        }

        return Answered(
            PositionAnswer.LaterPlayStands,
            here,
            atThePeer,
            playedHere > playedAtThePeer);
    }

    /// <summary>
    /// How far apart two moments are, whichever way round they were passed.
    /// </summary>
    /// <param name="one">One moment.</param>
    /// <param name="other">The other.</param>
    /// <returns>The distance between them.</returns>
    private static TimeSpan Apart(DateTime one, DateTime other) =>
        one > other ? one - other : other - one;

    /// <summary>
    /// One answer, with the losing side's position carried out where it lost something.
    ///
    /// The loser is attributed to the side it was on, because #36 records which server the
    /// discarded value came from, and an answer carrying only the number would leave that to be
    /// worked out a second time from the two states.
    ///
    /// Two sides at the same position lose nothing. That is the tie rule's ordinary case rather
    /// than an edge of it: both servers already agree where the person stopped, so there is an
    /// answer and no discarded value, and the pair reaches #36 as nothing rather than as a
    /// conflict whose loser is the same number as its winner.
    /// </summary>
    /// <param name="answer">What the rule answered.</param>
    /// <param name="here">The state this server holds.</param>
    /// <param name="atThePeer">The state the peer holds.</param>
    /// <param name="hereWon">Whether this server's position is the answer.</param>
    /// <returns>The resolution.</returns>
    private static PositionRecency Answered(
        PositionAnswer answer,
        SyncedState here,
        SyncedState atThePeer,
        bool hereWon)
    {
        var winner = hereWon ? here : atThePeer;
        var loser = hereWon ? atThePeer : here;

        var discarded =
            loser.PlaybackPositionTicks == 0
            || loser.PlaybackPositionTicks == winner.PlaybackPositionTicks
                ? (long?)null
                : loser.PlaybackPositionTicks;

        return new PositionRecency(
            answer,
            winner.PlaybackPositionTicks,
            hereWon ? null : discarded,
            hereWon ? discarded : null);
    }
}

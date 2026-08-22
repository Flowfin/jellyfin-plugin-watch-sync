using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Versions;

/// <summary>
/// Where a position that arrived from a peer lands when one work is held in several versions,
/// which is #28.
///
/// A leaf item can be one work the server holds as several files, and the server presents those
/// as one item, so a change arrives about the work rather than about a file. Three of the four
/// moved fields are properties of the work and apply to it: somebody who watched the extended
/// cut watched the film. The position is not. A tick counts from the start of one particular
/// file, and the same number names a different moment in a version of another length.
///
/// So the position is applied to the version this server would resume, and only where that
/// version's runtime and the runtime the peer sent for its own are close enough that the tick
/// means the same scene. Where they are not, the position is dropped and the rest of the change
/// is applied unchanged.
///
/// Which runtime is this server's is answered differently on the two supported lines, and that
/// difference is not this type's. The newer line names the version that drives the resume point
/// and this plugin reads the runtime of the item that identifier names; the older line has no
/// version to name and the item's own runtime is the answer. Both arrive here as one number, and
/// the adapter in #20 is where the two lines are made to answer the same question.
///
/// <c>docs/sync-model.md</c> holds the rule, the tolerance and the reason for its value. This
/// type points at that document rather than restating it.
/// </summary>
public sealed class VersionLanding
{
    private VersionLanding(
        VersionLandingAnswer answer,
        SyncedState incoming,
        long? positionToApply,
        long? runtimeHereTicks,
        long? runtimeAtThePeerTicks)
    {
        Answer = answer;
        PlayedToApply = incoming.Played;
        PlayCountToApply = incoming.PlayCount;
        LastPlayedDateToApply = incoming.LastPlayedDate;
        PositionToApply = positionToApply;
        RuntimeHereTicks = runtimeHereTicks;
        RuntimeAtThePeerTicks = runtimeAtThePeerTicks;
    }

    /// <summary>
    /// Gets the widest difference between two runtimes that still lets a position across.
    ///
    /// A minute, and it is fixed here rather than offered as a setting. An operator cannot see
    /// the two runtimes side by side at the moment the question is asked, so it is not a number
    /// they are in a position to judge, and a setting always left at its default is a default
    /// with a support burden attached. #58 is where that would be revisited if a real library
    /// argued against it.
    ///
    /// Under a minute the difference is packaging: a distributor logo, a few seconds of black, a
    /// container that padded the end. Over it, the difference is an edit or a speed conversion,
    /// and both move the whole timeline rather than one end of it. The number sits at the small
    /// end of that boundary deliberately, because the two mistakes do not cost the same. A
    /// position refused where it would have been fine costs the person the few seconds it takes
    /// to find their place, and they can see why. A position applied where it should not have
    /// been drops them into a scene they had not reached, which is the one failure here nobody
    /// can take back.
    /// </summary>
    public static TimeSpan WidestRuntimeDifference => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public VersionLandingAnswer Answer { get; }

    /// <summary>
    /// Gets a value indicating whether the person watched the work, which is applied whatever
    /// the position did.
    /// </summary>
    public bool PlayedToApply { get; }

    /// <summary>
    /// Gets how often the person watched the work, which is applied whatever the position did.
    /// </summary>
    public int PlayCountToApply { get; }

    /// <summary>
    /// Gets when the person last watched the work, which is applied whatever the position did.
    /// </summary>
    public DateTime? LastPlayedDateToApply { get; }

    /// <summary>
    /// Gets the position to write, or null where it was dropped.
    ///
    /// Null means the local position is left where it is rather than set to anything. Dropping a
    /// position is declining to move it, and a rule that wrote a zero instead would take the
    /// person back to the start of a work they were part way through, which is a worse outcome
    /// than the one the drop exists to avoid.
    ///
    /// It is the only nullable field of the four the answer carries, and that is the structural
    /// half of this issue's fourth condition: a caller cannot drop the played state along with
    /// the position, because there is nothing here that says it was dropped.
    /// </summary>
    public long? PositionToApply { get; }

    /// <summary>
    /// Gets the runtime of the version this server would resume, or null where the item carries
    /// none.
    /// </summary>
    public long? RuntimeHereTicks { get; }

    /// <summary>
    /// Gets the runtime the peer sent for the version its position was measured against, or null
    /// where it sent none.
    /// </summary>
    public long? RuntimeAtThePeerTicks { get; }

    /// <summary>
    /// Gets a value indicating whether a position was dropped, which is what #62 shows and what
    /// is recorded against the item with both runtimes.
    ///
    /// A dropped position that nothing records is indistinguishable from a position that never
    /// moved, and the second is what an operator assumes.
    /// </summary>
    public bool ThePositionWasDropped => Answer != VersionLandingAnswer.ThePositionLands;

    /// <summary>
    /// Decides where an incoming position lands.
    ///
    /// The boundary is stated rather than left to a reader of the comparison: a difference of
    /// exactly the tolerance lets the position across, because the tolerance is the largest
    /// difference that still counts as packaging.
    ///
    /// What this rule cannot see is stated here rather than left to be found. It compares two
    /// lengths and never two timelines, so two versions of the same length whose extra material
    /// sits in different places pass it and displace the tick by as much as that material. And
    /// it is handed one runtime for this side, so which version that runtime belongs to is the
    /// adapter's answer rather than this rule's: a caller that hands in the wrong version's
    /// runtime gets a confident answer about the wrong file.
    /// </summary>
    /// <param name="incoming">
    /// The state that arrived from the peer, for one mapped user and one leaf item.
    /// </param>
    /// <param name="runtimeHereTicks">
    /// The runtime of the version this server would resume, or null where the item carries none,
    /// which is what an item the server has not analysed yet looks like.
    /// </param>
    /// <param name="runtimeAtThePeerTicks">
    /// The runtime the peer sent for the version its position was measured against, or null
    /// where it sent none.
    /// </param>
    /// <returns>What to apply.</returns>
    /// <exception cref="ArgumentNullException">The incoming state is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A position or a runtime below zero. The server produces neither, so both reach this rule
    /// only out of an envelope, where #19 bounds what one may carry.
    /// </exception>
    public static VersionLanding Decide(
        SyncedState incoming,
        long? runtimeHereTicks,
        long? runtimeAtThePeerTicks)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentOutOfRangeException.ThrowIfNegative(
            incoming.PlaybackPositionTicks,
            nameof(incoming));

        if (runtimeHereTicks is long declaredHere)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(declaredHere, nameof(runtimeHereTicks));
        }

        if (runtimeAtThePeerTicks is long declaredAtThePeer)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(
                declaredAtThePeer,
                nameof(runtimeAtThePeerTicks));
        }

        if (runtimeHereTicks is not long ours || runtimeAtThePeerTicks is not long theirs)
        {
            return new VersionLanding(
                VersionLandingAnswer.ARuntimeIsMissing,
                incoming,
                null,
                runtimeHereTicks,
                runtimeAtThePeerTicks);
        }

        if (Math.Abs(ours - theirs) > WidestRuntimeDifference.Ticks)
        {
            return new VersionLanding(
                VersionLandingAnswer.TheRuntimesAreTooFarApart,
                incoming,
                null,
                runtimeHereTicks,
                runtimeAtThePeerTicks);
        }

        return new VersionLanding(
            VersionLandingAnswer.ThePositionLands,
            incoming,
            incoming.PlaybackPositionTicks,
            runtimeHereTicks,
            runtimeAtThePeerTicks);
    }
}

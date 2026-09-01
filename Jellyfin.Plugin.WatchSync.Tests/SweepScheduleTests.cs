using Jellyfin.Plugin.WatchSync.Peer;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The sweep's interval against the backoff it is written next to, which is #55's second
/// condition held rather than asserted.
///
/// The condition asks that the schedule be a setting with a default and a stated reason for its
/// value. The setting and the default are closed against the sources by
/// <c>ConfigurationSettingsTests</c> and <c>ConfigurationDocumentTests</c> along with every other
/// setting. What neither of those reaches is the reason, because a reason is prose, and the two
/// facts below are the part of this one that is not: both numbers are chosen against
/// <c>BoundedBackoff</c> rather than against a feel for what is reasonable, and a relation
/// written in two comments is one that drifts the first time either number moves.
/// </summary>
public class SweepScheduleTests
{
    /// <summary>
    /// The sweep runs at least as often as a peer that has been failing for hours is asked.
    ///
    /// <see cref="BoundedBackoff.DefaultCeiling"/> says of itself that it is not shorter because
    /// this pass asks anyway, and that a ceiling below the sweep's interval buys nothing and asks
    /// more often. Both halves stop being true the moment the sweep is rarer than that ceiling,
    /// and neither the comment there nor the comment here would say so.
    /// </summary>
    [Fact]
    public void TheSweepRunsAtLeastAsOftenAsAFailingPeerIsAsked()
    {
        Assert.True(
            SweepSchedule.DefaultInterval <= BoundedBackoff.DefaultCeiling,
            $"The sweep's default interval is {SweepSchedule.DefaultInterval} and the backoff ceiling is {BoundedBackoff.DefaultCeiling}. The ceiling's own reason is that the sweep asks anyway, so a sweep rarer than the ceiling leaves that number chosen against something that is no longer the case.");
    }

    /// <summary>
    /// The widest interval an operator may choose is the same value as the backoff's own longest
    /// ceiling.
    ///
    /// Past that value a pairing that is working is reached more slowly than one that is failing,
    /// which is the absurdity the bound exists against. Equality is asserted rather than an
    /// inequality, because the two numbers stand for one relation and an inequality would go on
    /// passing while they drifted apart in the direction that makes this bound quietly stricter
    /// than the argument for it.
    ///
    /// What this cannot see is which of the two moved. It says they disagree and names both, and
    /// deciding which one is wrong is a reading of the argument at either site.
    /// </summary>
    [Fact]
    public void TheWidestIntervalIsTheSameValueAsTheBackoffsOwnLongestCeiling()
    {
        Assert.Equal(BoundedBackoff.LongestCeiling, SweepSchedule.LongestInterval);
    }

    /// <summary>
    /// The default is inside the bound, which is the one relation an operator can be handed a
    /// broken plugin by.
    ///
    /// A default above its own bound is refused by <c>ServerWideSettings</c> on a document nobody
    /// has edited, so a server would read its own untouched configuration and run no rules at
    /// all. It is arithmetic on two numbers in one file today and it is exactly the pair that
    /// stops being obvious once either of them is moved to satisfy the fact above it.
    /// </summary>
    [Fact]
    public void TheDefaultIntervalIsInsideTheBoundSoAnUntouchedDocumentIsReadable()
    {
        Assert.InRange(
            SweepSchedule.DefaultInterval,
            System.TimeSpan.FromMinutes(1),
            SweepSchedule.LongestInterval);
    }
}

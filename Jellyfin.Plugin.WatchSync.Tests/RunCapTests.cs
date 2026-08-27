using System;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The bound on how much one run may change, which is #38's first condition.
///
/// The facts here are about the rule and not about a run, because there is no run. What they
/// hold is the part that decides whether the rest of #38 is safe when it arrives: that both
/// bounds bite, that each bites on the library the other is blind on, that the count is judged
/// first, and that neither can be set to a value that makes it stop being a cap.
///
/// The two bounds are asserted apart rather than through one fixture that crosses both, because
/// a rule where one arm never fires passes every test written against the other one, and the
/// arm that never fires is the one protecting the library nobody in this household has.
/// </summary>
public class RunCapTests
{
    /// <summary>
    /// A run under both bounds proceeds. This is the case every ordinary exchange is, and a cap
    /// that costs it anything is one an operator turns off.
    /// </summary>
    [Fact]
    public void ARunUnderBothBoundsProceeds()
    {
        var verdict = RunCap.Judge(changes: 10, matched: 1000, maximumChanges: 100, maximumShare: 0.10);

        Assert.Equal(RunCapAnswer.Within, verdict.Answer);
        Assert.Equal(10, verdict.Changes);
    }

    /// <summary>
    /// The count stops a run on a library large enough that the share never would.
    ///
    /// Ten thousand matched items at a tenth allows a thousand changes, so nothing about the
    /// share is what refuses this run. It is the case the count exists for.
    /// </summary>
    [Fact]
    public void TheCountStopsARunTheShareWouldHaveAllowed()
    {
        var verdict = RunCap.Judge(changes: 500, matched: 10000, maximumChanges: 100, maximumShare: 0.10);

        Assert.Equal(RunCapAnswer.ExceedsCount, verdict.Answer);
        Assert.Equal(100, verdict.Allowed);
    }

    /// <summary>
    /// The share stops a run on a library too small for the count to see.
    ///
    /// Eighty changes against two hundred matched items is forty per cent of somebody's history
    /// and is comfortably under a count of a hundred. This is the case the share exists for, and
    /// a rule carrying only the count would let it through.
    /// </summary>
    [Fact]
    public void TheShareStopsARunTheCountWouldHaveAllowed()
    {
        var verdict = RunCap.Judge(changes: 80, matched: 200, maximumChanges: 100, maximumShare: 0.10);

        Assert.Equal(RunCapAnswer.ExceedsShare, verdict.Answer);
        Assert.Equal(20, verdict.Allowed);
    }

    /// <summary>
    /// Where both bounds are crossed the count is the one reported, and this is the ordering
    /// rule rather than a preference about messages.
    ///
    /// The share is computed against the matched count, which the matcher produces, and a
    /// matcher that has gone wrong in the direction that inflates it also softens the share. So
    /// the bound that reads none of the matcher's output is the one that answers when the
    /// matcher is what went wrong.
    /// </summary>
    [Fact]
    public void WhereBothAreCrossedTheCountIsTheOneReported()
    {
        var verdict = RunCap.Judge(changes: 500, matched: 600, maximumChanges: 100, maximumShare: 0.10);

        Assert.Equal(RunCapAnswer.ExceedsCount, verdict.Answer);
        Assert.Equal(100, verdict.Allowed);
    }

    /// <summary>
    /// Both boundaries are where the rule says they are: the bound itself passes and one more
    /// than it does not.
    ///
    /// This is the one-character mistake somebody makes, and it is the whole difference between
    /// a cap and a cap that is off by one on the day it matters.
    /// </summary>
    [Theory]
    [InlineData(100, 10000, RunCapAnswer.Within)]
    [InlineData(101, 10000, RunCapAnswer.ExceedsCount)]
    [InlineData(20, 200, RunCapAnswer.Within)]
    [InlineData(21, 200, RunCapAnswer.ExceedsShare)]
    public void TheTwoBoundariesAreWhereTheRuleSaysTheyAre(int changes, int matched, RunCapAnswer expected)
    {
        Assert.Equal(
            expected,
            RunCap.Judge(changes, matched, maximumChanges: 100, maximumShare: 0.10).Answer);
    }

    /// <summary>
    /// A person with nothing matched has a share bound of nothing, so any change stops the run.
    ///
    /// That is the right answer rather than a hole. A run proposing changes for somebody whose
    /// items none matched is proposing to write against a resolution nothing produced, which is
    /// the shape of the failure this cap is written against rather than a small run.
    /// </summary>
    [Fact]
    public void NothingMatchedAllowsNoChange()
    {
        Assert.Equal(
            RunCapAnswer.ExceedsShare,
            RunCap.Judge(changes: 1, matched: 0, maximumChanges: 100, maximumShare: 0.10).Answer);

        Assert.Equal(
            RunCapAnswer.Within,
            RunCap.Judge(changes: 0, matched: 0, maximumChanges: 100, maximumShare: 0.10).Answer);
    }

    /// <summary>
    /// The share allows a whole number of items and never a fraction of one, and the fraction is
    /// dropped rather than rounded.
    ///
    /// A tenth of twenty five items is two and a half. Rounding up would allow three, which is
    /// more than the setting says, and a cap that quietly allows more than it declares is one
    /// nobody can reason about from the page.
    /// </summary>
    [Fact]
    public void TheShareAllowsWholeItemsAndDropsTheFraction()
    {
        var verdict = RunCap.Judge(changes: 3, matched: 25, maximumChanges: 100, maximumShare: 0.10);

        Assert.Equal(RunCapAnswer.ExceedsShare, verdict.Answer);
        Assert.Equal(2, verdict.Allowed);
    }

    /// <summary>
    /// A setting outside what the rule accepts is refused rather than clamped.
    ///
    /// Clamping is the failure this refuses: an operator who set a count of a million would be
    /// running under a cap of ten thousand and reading a million on their own page, and the
    /// first thing they would learn about the difference is a run that stopped when they were
    /// told it would not.
    /// </summary>
    /// <param name="maximumChanges">The count bound to offer the rule.</param>
    /// <param name="maximumShare">The share bound to offer the rule.</param>
    [Theory]
    [InlineData(0, 0.10)]
    [InlineData(-1, 0.10)]
    [InlineData(10001, 0.10)]
    [InlineData(100, 0.0)]
    [InlineData(100, -0.10)]
    [InlineData(100, 0.51)]
    [InlineData(100, 1.0)]
    [InlineData(100, double.NaN)]
    public void ASettingOutsideWhatTheRuleAcceptsIsRefused(int maximumChanges, double maximumShare)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RunCap.Judge(changes: 1, matched: 100, maximumChanges: maximumChanges, maximumShare: maximumShare));
    }

    /// <summary>
    /// A count the caller computed wrongly is refused rather than answered.
    ///
    /// Negative counts do not arise from a small run; they arise from a subtraction somewhere
    /// upstream, and answering one would hand back a verdict about a run nobody can describe.
    /// </summary>
    /// <param name="changes">The change count to offer the rule.</param>
    /// <param name="matched">The matched count to offer the rule.</param>
    [Theory]
    [InlineData(-1, 100)]
    [InlineData(1, -1)]
    public void ACountThatCannotHaveBeenCountedIsRefused(int changes, int matched)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RunCap.Judge(changes, matched, maximumChanges: 100, maximumShare: 0.10));
    }

    /// <summary>
    /// Both defaults sit inside the bounds the rule accepts, so the rule the plugin ships with
    /// is one the rule itself would take from an operator.
    ///
    /// A default outside its own bound is the shape where the shipped behaviour and the
    /// configurable behaviour are two different rules, and the one nobody tested is the one
    /// everybody runs.
    /// </summary>
    [Fact]
    public void TheDefaultsAreSettingsThisRuleWouldAccept()
    {
        var verdict = RunCap.Judge(
            changes: 0,
            matched: 1000,
            maximumChanges: RunCap.DefaultMaximumChanges,
            maximumShare: RunCap.DefaultMaximumShare);

        Assert.Equal(RunCapAnswer.Within, verdict.Answer);
    }

    /// <summary>
    /// The cap cannot be set to something that never fires, which is what "on by default" is
    /// worth once a settings page exists.
    ///
    /// An operator who finds the cap inconvenient reaches for the largest value the page will
    /// take. That value still stops a run that would change more than half of one person's
    /// matched items, and still stops one of ten thousand and one changes.
    /// </summary>
    [Fact]
    public void TheLoosestSettingTheRuleAcceptsIsStillACap()
    {
        Assert.Equal(
            RunCapAnswer.ExceedsShare,
            RunCap.Judge(
                changes: 501,
                matched: 1000,
                maximumChanges: RunCap.MaximumConfigurableChanges,
                maximumShare: RunCap.MaximumConfigurableShare).Answer);

        Assert.Equal(
            RunCapAnswer.ExceedsCount,
            RunCap.Judge(
                changes: RunCap.MaximumConfigurableChanges + 1,
                matched: int.MaxValue,
                maximumChanges: RunCap.MaximumConfigurableChanges,
                maximumShare: RunCap.MaximumConfigurableShare).Answer);
    }
}

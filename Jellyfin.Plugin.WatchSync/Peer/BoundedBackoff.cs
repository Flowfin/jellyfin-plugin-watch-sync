using System;

namespace Jellyfin.Plugin.WatchSync.Peer;

/// <summary>
/// How long to wait before asking a peer again, after a run of attempts that failed, which is the
/// second rule of #53.
///
/// <para>
/// The wait doubles from the first one and stops at a ceiling, and the ceiling is REACHED rather
/// than exceeded. Both halves of that sentence are load-bearing and they fail in opposite
/// directions. A wait that grows without a ceiling reaches a day within a dozen failures, so a
/// peer that was off for an evening is not asked again until the next evening and an operator
/// sees a pairing that never recovered. A wait that stops growing before the ceiling, which is
/// what an implementation that abandons the doubling as soon as the next one would pass the
/// ceiling produces, leaves a failing peer being asked at whatever interval happened to be the
/// last one under it.
/// </para>
///
/// <para>
/// What the wait is FOR is the layer below rather than the peer's feelings about being asked. The
/// pairing plane admits a bounded number of arrivals per pairing identifier and refuses the rest
/// before it verifies anything, and it counts every request type against the same allowance. A
/// retry loop fast enough to matter for one pairing is fast enough to spend that allowance, and
/// what an operator then sees is the plane's undistinguished refusal on traffic that has nothing
/// to do with syncing. <see cref="Model.EnvelopeBounds.TransportArrivalsPerPairing"/> and
/// <see cref="Model.EnvelopeBounds.TransportArrivalWindowSeconds"/> carry that reading and the
/// commit it was taken at, and <c>BoundedBackoffTests</c> holds the default first wait under it.
/// </para>
///
/// <para>
/// WHICH FAILURES ARE RETRIED AT ALL IS NOT DECIDED HERE, and that is a boundary rather than an
/// omission. The plane answers a refusal with a code, and four of those codes are different
/// problems for anything retrying: one is the refusal the same request succeeds under later, one
/// is the two clocks disagreeing and is not retryable at any interval, and the settled ones are
/// requests the peer has already answered. Reading a code is the adapter's, #40, and
/// <c>docs/transfer.md</c> names the shape rather than copying the list. What this rule answers is
/// the interval for a caller that has already decided this failure is one to retry.
/// </para>
///
/// <para>
/// Nothing here reads a clock and nothing here waits. The run of failures arrives as a count and
/// the answer is a span the caller schedules against the injected clock, which is the
/// <c>waiting-is-on-the-injected-clock</c> invariant and the headless rule the suite is held to. A
/// backoff proven by sleeping is the test that gets deleted the first time it is flaky.
/// </para>
///
/// <para>
/// It is deliberately not jittered. Jitter is worth its cost where many callers back off against
/// one server at once, and here one pairing is one caller against one peer, so what jitter would
/// buy is nothing and what it would cost is a rule whose answer cannot be stated. A second pairing
/// to the same peer is the case that would change that, and it is not one this plan has.
/// </para>
/// </summary>
public sealed class BoundedBackoff
{
    private BoundedBackoff(BackoffAnswer answer, TimeSpan wait)
    {
        Answer = answer;
        Wait = wait;
    }

    /// <summary>
    /// Gets how long to wait after the first failure, where an operator has chosen nothing.
    ///
    /// Thirty seconds. What decides it is the allowance the layer below admits rather than the
    /// peer being slow: at thirty seconds one pairing spends two arrivals inside the plane's
    /// published sixty-second window against the sixty it admits there, which is a thirtieth of
    /// it, so a peer that is down cannot make this plugin the reason its other traffic is refused.
    ///
    /// The direction that costs something is the short one, and it costs it quietly. A first wait
    /// of a second or two looks attentive and is the value at which a pairing that is failing
    /// spends its own allowance, so the refusal an operator then meets is the plane's
    /// undistinguished one and says nothing about a peer being down.
    /// </summary>
    public static TimeSpan DefaultFirstWait => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the wait a run of failures settles at, where an operator has chosen nothing.
    ///
    /// Thirty minutes. It is reached at the seventh consecutive failure from
    /// <see cref="DefaultFirstWait"/>, which is a little over half an hour of a peer being
    /// unreachable, and it is the interval a peer that stays down is asked at afterwards.
    ///
    /// What the number is chosen against is how long a peer that came back stays unnoticed.
    /// Thirty minutes is the worst case for that, and it is short enough that a server somebody
    /// rebooted is syncing again before they have finished watching what they started. The reason
    /// it is not shorter is the sweep: the scheduled convergence in #55 asks anyway, so a peer
    /// that came back is picked up by whichever of the two happens first, and a ceiling below the
    /// sweep's own interval buys nothing and asks more often.
    /// </summary>
    public static TimeSpan DefaultCeiling => TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets the shortest first wait this rule accepts.
    ///
    /// One second, and it is a bound on the rule rather than advice to whoever picks a number.
    /// Below it a retry stops being a retry: the attempts arrive faster than the plane's own
    /// arrival window is counted over, so the failure a caller meets is one it manufactured, and
    /// the first thing to go is the pairing's other traffic rather than anything about syncing.
    /// </summary>
    public static TimeSpan ShortestFirstWait => TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the longest ceiling this rule accepts.
    ///
    /// Six hours. Past it the ceiling stops being the interval a failing peer is asked at and
    /// becomes the reason a working one is not: a peer that came back a minute after the wait
    /// began waits the rest of it, and a ceiling of a day means a household's evening syncs the
    /// next evening. It is the same failure the ceiling exists against, arrived at through the
    /// ceiling being set too high rather than through there being none.
    /// </summary>
    public static TimeSpan LongestCeiling => TimeSpan.FromHours(6);

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public BackoffAnswer Answer { get; }

    /// <summary>
    /// Gets how long to wait before the next attempt.
    ///
    /// It is <see cref="TimeSpan.Zero"/>, and only zero, where nothing has failed.
    /// </summary>
    public TimeSpan Wait { get; }

    /// <summary>
    /// Gets a value indicating whether the wait has stopped growing.
    ///
    /// The fact is carried out rather than inferred by comparing <see cref="Wait"/> against a
    /// ceiling the caller would have to hold its own copy of. A caller deciding whether a peer has
    /// settled is the status surface in #62 asking one question, and a caller that has to
    /// re-derive the ceiling to ask it is a second place the ceiling lives.
    /// </summary>
    public bool AtTheCeiling => Answer == BackoffAnswer.AtTheCeiling;

    /// <summary>
    /// Answers how long to wait before the next attempt on one pairing.
    /// </summary>
    /// <param name="consecutiveFailures">
    /// How many attempts have failed in a row since the last one that succeeded. Zero is a peer
    /// with nothing behind it, which is the state a success puts it back into: this rule holds
    /// nothing between calls and the reset is the caller passing zero rather than an operation
    /// here.
    /// </param>
    /// <param name="firstWait">How long to wait after the first failure.</param>
    /// <param name="ceiling">The wait a run of failures settles at.</param>
    /// <returns>The wait, and whether it has settled.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A negative count, which is a caller that has subtracted rather than reset. A first wait
    /// below <see cref="ShortestFirstWait"/> or a ceiling above <see cref="LongestCeiling"/>. Or a
    /// ceiling below the first wait, which is a pair of numbers that cannot both be meant: the
    /// first failure would already be at a ceiling the rule never grows towards, so the doubling
    /// this rule is would have no effect an operator could observe.
    /// </exception>
    public static BoundedBackoff After(int consecutiveFailures, TimeSpan firstWait, TimeSpan ceiling)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstWait, ShortestFirstWait, nameof(firstWait));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ceiling, LongestCeiling, nameof(ceiling));
        ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, firstWait, nameof(ceiling));

        if (consecutiveFailures == 0)
        {
            return new BoundedBackoff(BackoffAnswer.NothingHasFailed, TimeSpan.Zero);
        }

        // The doubling is written against the ceiling rather than against the count, so a count
        // large enough to overflow the arithmetic never reaches it. Every double happens only
        // where the result is still strictly below the ceiling, and the ceiling is a TimeSpan, so
        // neither the multiplication nor the subtraction that guards it can leave the range. A
        // shift by the count, which is the shorter spelling, wraps at the sixty-fourth failure and
        // answers a peer that has been down for a week with a wait of no time at all.
        var ticks = firstWait.Ticks;
        var ceilingTicks = ceiling.Ticks;

        for (var doubled = 1; doubled < consecutiveFailures; doubled++)
        {
            if (ticks >= ceilingTicks - ticks)
            {
                ticks = ceilingTicks;
                break;
            }

            ticks *= 2;
        }

        // The wait is read off the one place that clamps rather than being clamped a second
        // time here. Two clamps agree with each other, so neither is proven: a change removing
        // one leaves the other answering correctly and the suite green, which is the state this
        // rule was in when its guards were first measured.
        return new BoundedBackoff(
            ticks >= ceilingTicks ? BackoffAnswer.AtTheCeiling : BackoffAnswer.Growing,
            TimeSpan.FromTicks(ticks));
    }
}

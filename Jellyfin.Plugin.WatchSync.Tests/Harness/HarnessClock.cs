using System;

namespace Jellyfin.Plugin.WatchSync.Tests.Harness;

/// <summary>
/// One side's clock, which starts where it is told to and moves only when it is told to.
///
/// The headless rule refuses a test that reads the machine clock, and much of what this plugin
/// decides is about time: a position resolved by the newer play, a skew that is tolerated, a
/// backoff, a retention. A case that took the machine clock would be a case that fails at a date
/// boundary on somebody else's machine, and one that slept for a backoff is the case that gets
/// deleted the first time it is flaky.
///
/// It is one clock per side rather than one for the harness, because skew is the thing several
/// of the rules are about: two servers whose clocks disagree is the ordinary case rather than a
/// fault, and a harness with a single clock could not put the two sides into it.
///
/// Nothing here is thread safe. A case drives both sides from its own thread, and a clock that
/// took a lock would invite a case to start something that moves time from somewhere else, which
/// is the real wait this rule exists against wearing a different hat.
/// </summary>
internal sealed class HarnessClock
{
    private DateTimeOffset _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessClock"/> class.
    /// </summary>
    /// <param name="startsAt">The moment this side believes it is when the case begins.</param>
    internal HarnessClock(DateTimeOffset startsAt)
    {
        _now = startsAt;
    }

    /// <summary>
    /// Gets the moment this side believes it is.
    /// </summary>
    internal DateTimeOffset Now => _now;

    /// <summary>
    /// Moves this side's clock forward.
    /// </summary>
    /// <param name="by">How far forward.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A negative span. A clock that can be wound back would let a case produce a pair of
    /// moments no run of this plugin can observe on one side, and the case would then be about
    /// a state the rules never meet.
    /// </exception>
    internal void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(by),
                by,
                "A side's clock moves forward. Skew between the two sides is set by starting them apart or by advancing one of them, never by winding one back.");
        }

        _now = _now.Add(by);
    }
}

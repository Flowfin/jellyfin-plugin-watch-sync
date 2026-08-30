using System;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What keeps two exchanges for one pairing and one mapped user from running at once, which is
/// the fourth condition of #55 and the section of <c>docs/transfer.md</c> that condition points
/// at.
///
/// Two properties are what this set is written against. The exclusion is over the pairing and
/// the mapped user together, so two people of one household exchange at the same time and one
/// person does not exchange with themselves twice. And a start that meets one in progress is
/// refused rather than held, so nothing waits for conditions it read before the wait.
///
/// Nothing here reads a clock and nothing here sleeps, which is the rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class OneExchangeAtATimeTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _otherUser = new("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// The fourth condition of #55. A second start on the pair an exchange is already running on
    /// is refused, and it is refused rather than queued: the answer comes back at once and the
    /// caller is not holding anything.
    /// </summary>
    [Fact]
    public void ASecondExchangeOnOnePairingAndOneUserIsRefused()
    {
        var exclusion = new OneExchangeAtATime();

        using var first = exclusion.Admit(_pairing, _user);
        using var second = exclusion.Admit(_pairing, _user);

        Assert.True(first.IsAdmitted);
        Assert.False(second.IsAdmitted);
        Assert.Equal(ExchangeAdmissionAnswer.AlreadyRunning, second.Answer);
        Assert.Equal(1, exclusion.Running);
    }

    /// <summary>
    /// A refusal is not a queue, so releasing the exchange that was running does not turn the
    /// refused start into a running one. The refused caller asks again on its next interval and
    /// is admitted then.
    ///
    /// This is the difference the fourth condition names and it is the one an implementation
    /// built on a wait would lose while every other fact here stayed green.
    /// </summary>
    [Fact]
    public void ARefusedStartIsNotAdmittedByTheOneInProgressFinishing()
    {
        var exclusion = new OneExchangeAtATime();

        var first = exclusion.Admit(_pairing, _user);
        var refused = exclusion.Admit(_pairing, _user);

        first.Dispose();

        Assert.False(refused.IsAdmitted);
        Assert.Equal(0, exclusion.Running);

        using var later = exclusion.Admit(_pairing, _user);

        Assert.True(later.IsAdmitted);
    }

    /// <summary>
    /// The exclusion is over the pair and not over the pairing, which is what
    /// <c>docs/transfer.md</c> fixes and is the half a coarser rule gets wrong in the direction
    /// that refuses honest work.
    ///
    /// Two mapped users of one pairing share no agreed record and no watermark, so nothing they
    /// write can collide. An exclusion taken over the pairing alone would serialise every
    /// household with more than one person in it, and would do it silently: the refusals look
    /// exactly like the refusals this rule is meant to produce.
    /// </summary>
    [Fact]
    public void TwoMappedUsersOfOnePairingExchangeAtTheSameTime()
    {
        var exclusion = new OneExchangeAtATime();

        using var one = exclusion.Admit(_pairing, _user);
        using var other = exclusion.Admit(_pairing, _otherUser);

        Assert.True(one.IsAdmitted);
        Assert.True(other.IsAdmitted);
        Assert.Equal(2, exclusion.Running);
    }

    /// <summary>
    /// The other direction of the same rule. One person mapped on two pairings exchanges on both
    /// at once, because those are two agreed records and two watermarks.
    /// </summary>
    [Fact]
    public void OneMappedUserOnTwoPairingsExchangesOnBothAtOnce()
    {
        var exclusion = new OneExchangeAtATime();

        using var one = exclusion.Admit(_pairing, _user);
        using var other = exclusion.Admit(_otherPairing, _user);

        Assert.True(one.IsAdmitted);
        Assert.True(other.IsAdmitted);
        Assert.Equal(2, exclusion.Running);
    }

    /// <summary>
    /// The place is given back on the path where the exchange threw as well as on the one where
    /// it finished.
    ///
    /// This is the failure the disposable shape exists against. An exchange that released its
    /// place on its own last line is correct until it fails on the line before, and what that
    /// leaves is a pairing and a mapped user that never exchange again until the server is
    /// restarted.
    /// </summary>
    [Fact]
    public void ThePlaceIsGivenBackWhenTheExchangeThrows()
    {
        var exclusion = new OneExchangeAtATime();

        Action anExchangeThatFails = () =>
        {
            using var admitted = exclusion.Admit(_pairing, _user);

            throw new InvalidOperationException("what an exchange failing looks like");
        };

        Assert.Throws<InvalidOperationException>(anExchangeThatFails);

        Assert.Equal(0, exclusion.Running);

        using var afterwards = exclusion.Admit(_pairing, _user);

        Assert.True(afterwards.IsAdmitted);
    }

    /// <summary>
    /// Releasing twice releases once.
    ///
    /// The second release would give away a place a later start is holding, and the two
    /// exchanges this rule exists to keep apart would then run together, on the pairing where
    /// somebody wrote one using block too many. It is asserted through a later start rather than
    /// through the count, because the count going to zero is what the defect looks like and not
    /// what it costs.
    /// </summary>
    [Fact]
    public void ReleasingAPlaceTwiceDoesNotGiveAwayTheOneSomebodyElseIsHolding()
    {
        var exclusion = new OneExchangeAtATime();

        var first = exclusion.Admit(_pairing, _user);

        first.Dispose();

        using var later = exclusion.Admit(_pairing, _user);

        first.Dispose();

        Assert.True(later.IsAdmitted);
        Assert.Equal(1, exclusion.Running);
        Assert.False(exclusion.Admit(_pairing, _user).IsAdmitted);
    }

    /// <summary>
    /// A refused start gives nothing back when it is disposed, so a caller may write one using
    /// block over both answers instead of a branch that only disposes on one of them.
    /// </summary>
    [Fact]
    public void DisposingARefusedStartGivesNothingBack()
    {
        var exclusion = new OneExchangeAtATime();

        using var running = exclusion.Admit(_pairing, _user);

        exclusion.Admit(_pairing, _user).Dispose();

        Assert.Equal(1, exclusion.Running);
        Assert.False(exclusion.Admit(_pairing, _user).IsAdmitted);
    }

    /// <summary>
    /// A start naming no pairing or no user is refused as a caller's mistake rather than
    /// admitted.
    ///
    /// Admitting it would give one place to every caller that forgot to name one, and the
    /// exclusion would then refuse two exchanges that have nothing to do with each other.
    /// </summary>
    [Fact]
    public void AStartNamingNoPairingOrNoUserIsRefused()
    {
        var exclusion = new OneExchangeAtATime();

        Assert.Throws<ArgumentException>(() => exclusion.Admit(Guid.Empty, _user));
        Assert.Throws<ArgumentException>(() => exclusion.Admit(_pairing, Guid.Empty));
        Assert.Equal(0, exclusion.Running);
    }
}

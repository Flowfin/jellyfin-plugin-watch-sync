using System;
using Jellyfin.Plugin.WatchSync.Conflict;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The play count reconciled against what the two sides last agreed, which is #33.
///
/// The first four facts here are the four conditions of that issue, in its order, and each is
/// driven through <see cref="Pair"/> rather than through the rule directly, because three of
/// them are about a pair over a sequence of exchanges rather than about one answer. The two
/// that follow are the rules the issue names as the wrong ones, asserted as the answers this
/// rule does not give: a reconciliation that started adding the two counts, or taking the
/// larger side and calling it the newer one, would pass every one of the first four and fail
/// those two.
/// </summary>
public class PlayCountReconciliationTests
{
    /// <summary>
    /// Two sides holding a count each, and the count they last agreed.
    ///
    /// It is three integers and no transport. What an exchange consists of is
    /// <c>docs/transfer.md</c> and the harness that runs two servers is #77; neither is stood
    /// in for here, because the conditions this fixture serves are about what the count is
    /// after an exchange rather than about how one is carried out.
    ///
    /// A pair starts from an agreement. Where two sides have never agreed a count there is
    /// nothing to reckon against and #37 decides what the first exchange does, which is the
    /// last fact in this file rather than a case this fixture models.
    /// </summary>
    private sealed class Pair
    {
        private Pair(int agreed)
        {
            Agreed = agreed;
            Here = agreed;
            AtThePeer = agreed;
        }

        public int Agreed { get; private set; }

        public int Here { get; private set; }

        public int AtThePeer { get; private set; }

        public static Pair AgreedAt(int count) => new Pair(count);

        public Pair PlayHere()
        {
            Here++;

            return this;
        }

        public Pair PlayAtThePeer()
        {
            AtThePeer++;

            return this;
        }

        /// <summary>
        /// Reconciles, applies the answer to both sides, and agrees it.
        ///
        /// Both sides land on the same number and that number becomes the agreement, which is
        /// what makes the next exchange an ordinary one. A fixture that reconciled without
        /// agreeing would show a count climbing on every run and would be measuring itself.
        /// </summary>
        /// <returns>What the rule answered.</returns>
        public PlayCountReconciliation Sync()
        {
            var reconciliation = PlayCountReconciliation.Reconcile(Agreed, Here, AtThePeer);

            var count = Assert.IsType<long>(reconciliation.Count);

            Agreed = checked((int)count);
            Here = Agreed;
            AtThePeer = Agreed;

            return reconciliation;
        }
    }

    /// <summary>
    /// A play on one side reaches the other and lands as one play, not as two and not as none.
    /// </summary>
    [Fact]
    public void APlayOnOneSideLeavesBothSidesOneHigher()
    {
        var pair = Pair.AgreedAt(1).PlayHere();

        var reconciliation = pair.Sync();

        Assert.Equal(PlayCountAnswer.Reconciled, reconciliation.Answer);
        Assert.Equal(2, pair.Here);
        Assert.Equal(2, pair.AtThePeer);
    }

    /// <summary>
    /// Exchanges with nothing in between change nothing.
    ///
    /// This is the failure that makes an adding rule visible only after a while: a pair that
    /// never watches anything again still climbs, once per run, for as long as it is paired.
    /// </summary>
    [Fact]
    public void ExchangingRepeatedlyWithNoPlayInBetweenNeverMovesTheCount()
    {
        var pair = Pair.AgreedAt(4);

        for (var run = 0; run < 5; run++)
        {
            pair.Sync();
        }

        Assert.Equal(4, pair.Here);
        Assert.Equal(4, pair.AtThePeer);
    }

    /// <summary>
    /// A play on each side between two exchanges adds two.
    ///
    /// Not one, which is what taking the larger of the two counts gives, and not four, which is
    /// what adding them gives at an agreement of two.
    /// </summary>
    [Fact]
    public void APlayOnEachSideBetweenTwoExchangesAddsTwo()
    {
        var pair = Pair.AgreedAt(2).PlayHere().PlayAtThePeer();

        pair.Sync();

        Assert.Equal(4, pair.Here);
        Assert.Equal(4, pair.AtThePeer);
    }

    /// <summary>
    /// A side below the agreement is a conflict, and it takes nothing off the other side.
    ///
    /// This is what a store restored from a backup taken before the agreement looks like. The
    /// count that comes out is the one that does not lower the peer, and the distance the side
    /// fell is carried out so that #36 can record which side it was and by how much. Nothing
    /// here writes a conflict record, because there is none in the tree to write.
    /// </summary>
    [Fact]
    public void ACountBelowTheAgreementIsAConflictAndLowersNothing()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(agreed: 3, here: 0, atThePeer: 3);

        Assert.Equal(PlayCountAnswer.BelowTheAgreement, reconciliation.Answer);
        Assert.True(reconciliation.IsBelowTheAgreement);
        Assert.Equal(3L, reconciliation.Count);
        Assert.Equal(3, reconciliation.ShortfallHere);
        Assert.Equal(0, reconciliation.ShortfallAtThePeer);
    }

    /// <summary>
    /// A side below the agreement does not cost the other side its own plays either.
    ///
    /// The restored side is short by three and the peer has watched the work once more since
    /// the agreement. Subtracting the shortfall would answer one, which is fewer plays than
    /// either side has ever recorded.
    /// </summary>
    [Fact]
    public void APlayOnTheOtherSideSurvivesAShortfall()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(agreed: 3, here: 0, atThePeer: 4);

        Assert.Equal(PlayCountAnswer.BelowTheAgreement, reconciliation.Answer);
        Assert.Equal(4L, reconciliation.Count);
        Assert.Equal(3, reconciliation.ShortfallHere);
    }

    /// <summary>
    /// Both sides short is one answer and two distances.
    ///
    /// Two restores, or one server rebuilt and one library entry replaced. The agreement is
    /// what both sides are known to have reached, so it is what comes out, and neither
    /// shortfall is subtracted from it.
    /// </summary>
    [Fact]
    public void BothSidesBelowTheAgreementKeepTheAgreedCount()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(agreed: 5, here: 1, atThePeer: 2);

        Assert.Equal(PlayCountAnswer.BelowTheAgreement, reconciliation.Answer);
        Assert.Equal(5L, reconciliation.Count);
        Assert.Equal(4, reconciliation.ShortfallHere);
        Assert.Equal(3, reconciliation.ShortfallAtThePeer);
    }

    /// <summary>
    /// The first wrong rule, asserted as the answer this one does not give.
    ///
    /// Adding the two counts double counts everything that already moved. At an agreement of
    /// four with nothing new on either side it answers eight, and every exchange after that
    /// doubles again.
    /// </summary>
    [Fact]
    public void TwoSidesAtTheAgreementAreNotAdded()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(agreed: 4, here: 4, atThePeer: 4);

        Assert.Equal(4L, reconciliation.Count);
    }

    /// <summary>
    /// The second wrong rule, asserted the same way.
    ///
    /// A rewatch on one side against a peer that has not moved. Taking the peer's reading,
    /// which is as new as any other and disagrees, throws the rewatch away, and the field
    /// carries no per-play time for anything to tell the two readings apart by.
    /// </summary>
    [Fact]
    public void ARewatchIsNotOverwrittenByASideThatDidNothing()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(agreed: 2, here: 3, atThePeer: 2);

        Assert.Equal(PlayCountAnswer.Reconciled, reconciliation.Answer);
        Assert.Equal(3L, reconciliation.Count);
    }

    /// <summary>
    /// Two histories that never met can sum past the field they came out of.
    ///
    /// The server holds a count in a 32 bit field, so the total of two of them need not fit in
    /// one. Reconciling in that width would wrap to a negative count and hand it on as an
    /// ordinary answer. What is done about a total that does not fit is a decision about
    /// writing and is #50's; what is asserted here is that the rule does not lose it.
    /// </summary>
    [Fact]
    public void ATotalWiderThanTheFieldItCameFromIsNotWrapped()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(
            agreed: 0,
            here: int.MaxValue,
            atThePeer: int.MaxValue);

        Assert.Equal(2L * int.MaxValue, reconciliation.Count);
    }

    /// <summary>
    /// No agreement yet is not a count of zero and is not a guess.
    ///
    /// Two sides each holding one play may be one play that already moved between them or two
    /// separate watchings that never met, and nothing in the two numbers separates those. #37
    /// fixes what the first exchange does, and this rule refuses to answer ahead of it.
    /// </summary>
    [Fact]
    public void WithNoAgreementThereIsNoCount()
    {
        var reconciliation = PlayCountReconciliation.Reconcile(agreed: null, here: 1, atThePeer: 1);

        Assert.Equal(PlayCountAnswer.NoAgreement, reconciliation.Answer);
        Assert.Null(reconciliation.Count);
        Assert.Equal(0, reconciliation.ShortfallHere);
        Assert.Equal(0, reconciliation.ShortfallAtThePeer);
    }

    /// <summary>
    /// A count below zero is refused rather than read as a shortfall.
    ///
    /// The server does not produce one. It reaches this rule out of an envelope, and an
    /// envelope's values are bounded and refused by #19; read as an ordinary count here it
    /// would be a side that is short by more than the agreement and would raise the total.
    /// </summary>
    /// <param name="agreed">The agreed count.</param>
    /// <param name="here">The count this server holds.</param>
    /// <param name="atThePeer">The count the peer holds.</param>
    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(-1, 0, 0)]
    public void ACountBelowZeroIsRefused(int agreed, int here, int atThePeer)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlayCountReconciliation.Reconcile(agreed, here, atThePeer));
    }
}

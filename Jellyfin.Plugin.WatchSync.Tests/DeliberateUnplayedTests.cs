using System;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// An unmark held against a completion, decided by what the two sides last agreed, which is
/// #34.
///
/// The facts are arranged around the two directions this rule can fail in, because they are
/// not equally bad and a file that only proved the pleasant one would read as coverage. A rule
/// that lost every unmark is the annoyance #34 is written to remove. A rule that let any
/// unplayed beat any played is how a fresh or restored server wipes an established one on
/// first contact, and that is the failure the fixtures without an agreement are for.
///
/// Every fact but <see cref="AnUnmarkWithNoAgreementBehindItCarriesNothing"/> gives the
/// unmarking side the same play count and the same last played date as the agreement, so a
/// rule that had stopped asking whether the completion moved would still answer them. The
/// movement cases are held on their own, for the same reason the ratchet's file holds the
/// margin on one fact rather than on all of them.
/// </summary>
public class DeliberateUnplayedTests
{
    private static readonly DateTime _agreedAt =
        new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime _later =
        new DateTime(2026, 4, 1, 20, 0, 0, DateTimeKind.Utc);

    private const long TwentyMinutes = 20 * 60 * TimeSpan.TicksPerSecond;

    /// <summary>
    /// An unmark of a completion the two sides agreed carries, in both directions.
    ///
    /// Both are here because the rule is one server's answer about a pair rather than a
    /// preference for the local side. A rule that carried only a local unmark would leave a
    /// person who unmarked an episode on the other server watching it as watched here forever,
    /// which is the same complaint from the other end of the pair.
    /// </summary>
    [Fact]
    public void AnUnmarkAfterAnAgreementCarriesInBothDirections()
    {
        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromHere,
            DeliberateUnplayed.Reconcile(Unmarked(), Finished(_agreedAt), Finished(_agreedAt)));

        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromThePeer,
            DeliberateUnplayed.Reconcile(Finished(_agreedAt), Unmarked(), Finished(_agreedAt)));
    }

    /// <summary>
    /// An unplayed with no agreement behind it carries nothing, and this is the fact the
    /// dangerous direction rests on.
    ///
    /// The fresh server holds the work unplayed, no plays and no date, and has agreed nothing.
    /// A rule reading unplayed as intent hands it the win and the established server's history
    /// is gone. Both directions are asserted because a wipe is as bad whichever of the two
    /// servers is the empty one, and the answer hands the pair to the ratchet, which is what
    /// keeps the completion.
    /// </summary>
    [Fact]
    public void AnUnmarkWithNoAgreementBehindItCarriesNothing()
    {
        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(NeverTouched(), Finished(_agreedAt), null));

        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(Finished(_agreedAt), NeverTouched(), null));
    }

    /// <summary>
    /// An agreement that was not a completion is not something an unplayed side turned off.
    ///
    /// Two sides that agreed the work unplayed, one of which has since watched it, are an
    /// ordinary play. Reading the side that still holds unplayed as an intent would make every
    /// first watch of a work reversible by the server that had not seen it.
    /// </summary>
    [Fact]
    public void AnAgreementThatWasNotACompletionCarriesNoUnmark()
    {
        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(NeverTouched(), Finished(_later), NeverTouched()));
    }

    /// <summary>
    /// Two sides that say the same thing are not a conflict this rule answers.
    ///
    /// Both still holding the agreed completion is nothing to decide. Both having turned it
    /// off is a decision the two sides already share, and the ratchet finds no completion to
    /// hold, so an answer here would be a second rule deciding a pair nobody disagrees about.
    /// </summary>
    [Fact]
    public void TwoSidesThatAgreeCarryNoUnmark()
    {
        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(Finished(_agreedAt), Finished(_agreedAt), Finished(_agreedAt)));

        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(Unmarked(), Unmarked(), Finished(_agreedAt)));
    }

    /// <summary>
    /// A completion the other side has watched again since the agreement outranks the unmark.
    ///
    /// The count moved, so the person made a play after the two sides settled. The unmark
    /// carries no moment, because the server stores none for turning a completion off, so
    /// there is nothing to order the two intents by and the plan's direction everywhere else
    /// is to keep the play somebody actually made. Both directions are asserted, because the
    /// side that rewatched is not always the peer.
    /// </summary>
    [Fact]
    public void ARewatchSinceTheAgreementOutranksTheUnmark()
    {
        var rewatched = new SyncedState(true, 2, 0, _later);

        Assert.Equal(
            UnplayedAnswer.TheCompletionMovedSinceTheAgreement,
            DeliberateUnplayed.Reconcile(Unmarked(), rewatched, Finished(_agreedAt)));

        Assert.Equal(
            UnplayedAnswer.TheCompletionMovedSinceTheAgreement,
            DeliberateUnplayed.Reconcile(rewatched, Unmarked(), Finished(_agreedAt)));
    }

    /// <summary>
    /// A later last played date is movement on its own, with the count unchanged.
    ///
    /// It is read as well as the count because a play that resumed and finished the work again
    /// moves the date on both supported lines while the count is what a line decides to
    /// increment, and a rule reading only the count would hand that rewatch to the unmark.
    /// </summary>
    [Fact]
    public void ALaterDateAloneIsMovementSinceTheAgreement()
    {
        Assert.Equal(
            UnplayedAnswer.TheCompletionMovedSinceTheAgreement,
            DeliberateUnplayed.Reconcile(Unmarked(), Finished(_later), Finished(_agreedAt)));
    }

    /// <summary>
    /// A date where the agreement carried none is movement, and a missing date where the
    /// agreement carried one is not.
    ///
    /// The two halves are asymmetric on purpose. The agreement is a state both sides settled
    /// on, so a date that had been there would have been part of it, and a side carrying one
    /// now has played the work since. A side that has lost the reading has lost a reading
    /// rather than gained a play, and reading absence as an earlier moment would hand the
    /// unmark a loss on a server whose metadata was rebuilt.
    /// </summary>
    [Fact]
    public void AnAbsentDateIsReadAsNoReadingRatherThanAsTheBeginningOfTime()
    {
        Assert.Equal(
            UnplayedAnswer.TheCompletionMovedSinceTheAgreement,
            DeliberateUnplayed.Reconcile(Unmarked(), Finished(_agreedAt), new SyncedState(true, 1, 0, null)));

        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromHere,
            DeliberateUnplayed.Reconcile(Unmarked(), new SyncedState(true, 1, 0, null), Finished(_agreedAt)));
    }

    /// <summary>
    /// A count below the agreed one is a shortfall rather than a rewatch.
    ///
    /// That is the side restored from an older backup, which is exactly the case #34's
    /// dangerous direction is about, and reading it as movement would hand the unmark a loss
    /// for the one reason it should win. #33 is where a shortfall is recorded.
    /// </summary>
    [Fact]
    public void ACountBelowTheAgreedOneIsNotARewatch()
    {
        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromHere,
            DeliberateUnplayed.Reconcile(
                Unmarked(),
                new SyncedState(true, 1, 0, _agreedAt),
                new SyncedState(true, 4, 0, _agreedAt)));
    }

    /// <summary>
    /// The unmark that carries is not required to have kept the agreed count or date.
    ///
    /// Only the side still holding the completion is asked whether it moved. A person who
    /// rewatched a work here and then unmarked it is making the unmark last, and asking the
    /// unmarking side about its own movement would refuse the sequence this issue's third
    /// condition walks.
    /// </summary>
    [Fact]
    public void MovementOnTheUnmarkingSideDoesNotCostItTheAnswer()
    {
        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromHere,
            DeliberateUnplayed.Reconcile(
                new SyncedState(false, 3, TwentyMinutes, _later),
                Finished(_agreedAt),
                Finished(_agreedAt)));
    }

    /// <summary>
    /// The pair walked as a sequence, which is what the two rules together have to survive.
    ///
    /// Play, agree, unmark, agree, play again, with the answer read after each step. It is one
    /// fact rather than five because the sequence is the claim: each step is only interesting
    /// as the state the next one starts from, and the step that would break silently is the
    /// fourth, where agreeing the unmark is what stops the peer's old completion coming back.
    /// </summary>
    [Fact]
    public void TheSequenceThePairHasToSurvive()
    {
        // Watched here, nothing agreed yet. The peer has never seen the work.
        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(Finished(_agreedAt), NeverTouched(), null));

        // Agreed as played, and the peer now holds the same. Nothing to decide.
        var agreedPlayed = Finished(_agreedAt);

        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(Finished(_agreedAt), Finished(_agreedAt), agreedPlayed));

        // Unmarked here. The peer holds the agreed completion untouched, so the unmark carries.
        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromHere,
            DeliberateUnplayed.Reconcile(Unmarked(), Finished(_agreedAt), agreedPlayed));

        // Agreed as unplayed. The peer's completion is gone from the agreement, so the pair is
        // settled and the ratchet has nothing to hold either.
        var agreedUnplayed = Unmarked();

        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(Unmarked(), Unmarked(), agreedUnplayed));

        // Played again here. The agreement is not a completion, so this is an ordinary play
        // and the peer's unplayed is not an intent that undoes it.
        Assert.Equal(
            UnplayedAnswer.NoUnmarkToCarry,
            DeliberateUnplayed.Reconcile(new SyncedState(true, 2, 0, _later), Unmarked(), agreedUnplayed));
    }

    /// <summary>
    /// The answer is the same whichever way round the two sides are passed.
    ///
    /// A rule that answered differently in the two directions would settle a pair by which
    /// server was asked first, which is the failure #32 names for its own row and is worse
    /// here, because the field it decides is the one that cannot be undone by watching again.
    /// </summary>
    [Fact]
    public void TheAnswerIsMirroredRatherThanDirectional()
    {
        var unmarked = Unmarked();
        var finished = Finished(_agreedAt);
        var agreed = Finished(_agreedAt);

        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromHere,
            DeliberateUnplayed.Reconcile(unmarked, finished, agreed));

        Assert.Equal(
            UnplayedAnswer.UnplayedCarriesFromThePeer,
            DeliberateUnplayed.Reconcile(finished, unmarked, agreed));
    }

    /// <summary>
    /// A play count below zero is refused rather than read.
    ///
    /// No server produces one, so it arrives out of an envelope, and #19 is what bounds what an
    /// envelope may carry. Read as an ordinary count it would make a side look as though it had
    /// fallen behind the agreement and hand the pair the wrong way.
    /// </summary>
    [Fact]
    public void APlayCountBelowZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeliberateUnplayed.Reconcile(
                new SyncedState(false, -1, 0, _agreedAt),
                Finished(_agreedAt),
                Finished(_agreedAt)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeliberateUnplayed.Reconcile(
                Finished(_agreedAt),
                new SyncedState(false, -1, 0, _agreedAt),
                Finished(_agreedAt)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeliberateUnplayed.Reconcile(
                Unmarked(),
                Finished(_agreedAt),
                new SyncedState(true, -1, 0, _agreedAt)));
    }

    /// <summary>
    /// A missing side is refused rather than read as an empty one.
    ///
    /// An absent peer state and a peer holding nothing for the item are different statements,
    /// and reading the first as the second would carry an unmark against a server that was
    /// never asked. A missing agreement is not the same case: null there is a first exchange,
    /// which is a defined state, and the fact above holds what it answers.
    /// </summary>
    [Fact]
    public void AMissingSideIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => DeliberateUnplayed.Reconcile(null!, Finished(_agreedAt), Finished(_agreedAt)));

        Assert.Throws<ArgumentNullException>(
            () => DeliberateUnplayed.Reconcile(Unmarked(), null!, Finished(_agreedAt)));
    }

    private static SyncedState Finished(DateTime lastPlayed) =>
        new SyncedState(true, 1, 0, lastPlayed);

    private static SyncedState Unmarked() =>
        new SyncedState(false, 1, 0, _agreedAt);

    private static SyncedState NeverTouched() =>
        new SyncedState(false, 0, 0, null);
}

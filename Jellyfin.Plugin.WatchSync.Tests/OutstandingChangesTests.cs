using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What a reading of this server's own record leaves outstanding against what was last agreed,
/// which is the second and third conditions of #14.
///
/// The failure underneath the whole set is the one the prior art keeps producing. Without the
/// agreed record there are two current values and no history, so the only available rule is to
/// overwrite, and the tool that always sends its own user data to the other server overwrites
/// whatever was there. With the record, a value equal to what was agreed is a value the peer
/// already has and nothing leaves; a value different from it is something that happened here.
///
/// Nothing here reads a clock. The moment a reading was observed is a parameter, which is the
/// <c>injected-clock</c> invariant and the headless rule together.
/// </summary>
public class OutstandingChangesTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _otherUser = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _otherFilm = new("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The second condition of #14. This is what stops an echo becoming an exchange: a value the
    /// two sides already settled on is not a change, whoever wrote it here and however recently.
    /// </summary>
    [Fact]
    public void AReadingEqualToWhatWasAgreedLeavesNothingOutstanding()
    {
        var agreed = new SyncedState(true, 3, 0, _watchedAt);

        Assert.Empty(Outstanding(Agreement(agreed), new SyncedState(true, 3, 0, _watchedAt)));
    }

    /// <summary>
    /// The third condition of #14, and the rule that every moved field is answered by an arm
    /// rather than by a default.
    ///
    /// Each case moves one field away from the same agreement and asserts one entry naming that
    /// field. A member added to the moved set with no rule saying when it has moved reaches the
    /// arm that throws, so the obligation arrives with the member instead of being noticed when
    /// a field silently stops syncing.
    /// </summary>
    [Theory]
    [InlineData(SyncedField.Played)]
    [InlineData(SyncedField.PlayCount)]
    [InlineData(SyncedField.PlaybackPositionTicks)]
    [InlineData(SyncedField.LastPlayedDate)]
    public void AReadingThatMovedOneFieldLeavesExactlyThatFieldOutstanding(SyncedField field)
    {
        var agreed = new SyncedState(true, 3, 600, _watchedAt);

        var outstanding = Outstanding(Agreement(agreed), Moved(agreed, field));

        var change = Assert.Single(outstanding);

        Assert.Equal(field, change.Field);
        Assert.Equal(_pairing, change.PairingId);
        Assert.Equal(_film, change.Subject.ItemId);
        Assert.Equal(_user, change.Subject.MappedUserId);
        Assert.Equal(_evening, change.FirstObservedAt);
    }

    /// <summary>
    /// Every member of the moved set has to be reachable by the rule above, or that theory is a
    /// list somebody keeps by hand beside an enumeration that moves.
    /// </summary>
    [Fact]
    public void EveryMovedFieldHasARuleThatSaysWhenItMoved()
    {
        var agreed = new SyncedState(true, 3, 600, _watchedAt);

        Assert.All(
            Enum.GetValues<SyncedField>(),
            field => Assert.Equal(
                field,
                Assert.Single(Outstanding(Agreement(agreed), Moved(agreed, field))).Field));
    }

    /// <summary>
    /// Two fields moving at once are two entries. The list a peer reads holds at most one entry
    /// per field, so a reading that moved two of them owes two, and an answer that carried one
    /// entry with the whole reading in it would leave the second field looking agreed.
    /// </summary>
    [Fact]
    public void AReadingThatMovedTwoFieldsLeavesOneEntryForEach()
    {
        var agreed = new SyncedState(false, 0, 600, null);

        var outstanding = Outstanding(Agreement(agreed), new SyncedState(true, 1, 600, null));

        Assert.Equal(2, outstanding.Count);
        Assert.Equal(
            new[] { SyncedField.Played, SyncedField.PlayCount },
            outstanding.Select(change => change.Field).ToArray());
    }

    /// <summary>
    /// The fifth condition of #14 from the side that sends. Nothing has been agreed about this
    /// item, so what this server holds is what a first exchange offers, and the peer merges it by
    /// the same conflict table as every later exchange, which is the answer taken on #37.
    /// </summary>
    [Fact]
    public void AnItemNothingWasAgreedAboutOffersWhatThisServerHolds()
    {
        var outstanding = Outstanding(null, new SyncedState(true, 1, 0, _watchedAt));

        Assert.Equal(
            new[] { SyncedField.Played, SyncedField.PlayCount, SyncedField.LastPlayedDate },
            outstanding.Select(change => change.Field).ToArray());
    }

    /// <summary>
    /// The other half of that condition, and the one that keeps the list the size of the work
    /// outstanding rather than the size of the library.
    ///
    /// An item nobody has watched and nobody has agreed offers nothing. Without this a first
    /// exchange would carry every matched item on the server, and the cap in #38 would stop the
    /// run that was supposed to be the cheap one.
    /// </summary>
    [Fact]
    public void AnItemNobodyWatchedAndNobodyAgreedOffersNothing()
    {
        Assert.Empty(Outstanding(null, new SyncedState(false, 0, 0, null)));
    }

    /// <summary>
    /// Comparing a reading of one item against what was agreed about another is a mistake nothing
    /// downstream could see, because what comes out of it looks like ordinary outstanding work.
    /// So it is refused where it is made.
    /// </summary>
    [Fact]
    public void AnAgreementAboutAnotherSubjectIsRefused()
    {
        var elsewhere = new AgreedRecord(
            Subject(_user, _otherFilm),
            new SyncedState(true, 1, 0, _watchedAt),
            _evening,
            1);

        var otherUser = new AgreedRecord(
            Subject(_otherUser, _film),
            new SyncedState(true, 1, 0, _watchedAt),
            _evening,
            1);

        Assert.Throws<ArgumentException>(() => Outstanding(elsewhere, Watched));
        Assert.Throws<ArgumentException>(() => Outstanding(otherUser, Watched));
    }

    /// <summary>
    /// An entry carries the whole reading the field was observed in rather than one value, which
    /// is what lets the collapse ask the conflict table's own rule whether an outstanding entry
    /// has been answered.
    /// </summary>
    [Fact]
    public void AnEntryCarriesTheReadingItWasObservedIn()
    {
        var local = new SyncedState(true, 1, 600, _watchedAt);

        var change = Assert.Single(Outstanding(
            Agreement(new SyncedState(false, 1, 600, _watchedAt)),
            local));

        Assert.Same(local, change.Observed);
    }

    /// <summary>
    /// What comes out of this rule is what goes into the collapse, and an evening of it leaves
    /// one entry per field rather than one per reading. The two rules are written apart and the
    /// second condition of #14 is only true of the pair, so the pair is asserted rather than
    /// assumed.
    /// </summary>
    [Fact]
    public void AnEveningOfReadingsCollapsesToOneEntryPerField()
    {
        var agreed = new SyncedState(false, 0, 0, null);
        IReadOnlyList<RecordedChange> recorded = Array.Empty<RecordedChange>();

        for (var second = 1; second <= 3600; second++)
        {
            var local = new SyncedState(false, 0, second * TimeSpan.TicksPerSecond, null);

            foreach (var change in Outstanding(Agreement(agreed), local, _evening.AddSeconds(second)))
            {
                recorded = ChangeCollapse.Record(recorded, change);
            }
        }

        var entry = Assert.Single(recorded);

        Assert.Equal(SyncedField.PlaybackPositionTicks, entry.Field);
        Assert.Equal(_evening.AddSeconds(1), entry.FirstObservedAt);
        Assert.Equal(3600 * TimeSpan.TicksPerSecond, entry.Observed.PlaybackPositionTicks);
    }

    private static SyncedState Watched => new SyncedState(true, 1, 0, _watchedAt);

    private static TransferSubject Subject(Guid user, Guid item) =>
        TransferSubject.From(user, item, BaseItemKind.Movie).Value!;

    private static AgreedRecord Agreement(SyncedState agreed) =>
        new AgreedRecord(Subject(_user, _film), agreed, _evening, 1);

    /// <summary>
    /// The same state with one field moved to a value it is not already at.
    /// </summary>
    /// <param name="state">The state to move.</param>
    /// <param name="field">The field that moves.</param>
    /// <returns>The moved state.</returns>
    private static SyncedState Moved(SyncedState state, SyncedField field) => field switch
    {
        SyncedField.Played => new SyncedState(
            !state.Played, state.PlayCount, state.PlaybackPositionTicks, state.LastPlayedDate),
        SyncedField.PlayCount => new SyncedState(
            state.Played, state.PlayCount + 1, state.PlaybackPositionTicks, state.LastPlayedDate),
        SyncedField.PlaybackPositionTicks => new SyncedState(
            state.Played, state.PlayCount, state.PlaybackPositionTicks + 1, state.LastPlayedDate),
        SyncedField.LastPlayedDate => new SyncedState(
            state.Played,
            state.PlayCount,
            state.PlaybackPositionTicks,
            state.LastPlayedDate?.AddMinutes(1) ?? _watchedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No case moves it."),
    };

    private static IReadOnlyList<RecordedChange> Outstanding(
        AgreedRecord? agreement,
        SyncedState local,
        DateTimeOffset? observedAt = null) =>
        OutstandingChanges.Since(
            _pairing,
            agreement,
            Subject(_user, _film),
            local,
            observedAt ?? _evening);
}

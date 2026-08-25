using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the collapse rule, which is #49.
///
/// The list a peer reads holds at most one entry per pairing, mapped user, item and field. What
/// this set is written against is an evening of viewing arriving as hundreds of entries that each
/// contradict the next, and the second failure underneath it: a collapse that decides the
/// completion-against-position pair for itself instead of asking the rule
/// <c>docs/conflicts.md</c> already gives that pair. Two implementations of one rule agree until
/// one of them is edited, and the edit is invisible in both files.
///
/// Nothing here reads a clock. Every moment is a parameter, so the boundary cases are asserted at
/// the boundary rather than near it, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public class ChangeCollapseTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _otherUser = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid _otherFilm = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid _episode = new("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The vocabulary this rule keys on is a second list of the moved set, and two lists of one
    /// thing drift. A field added to the moved set with no member here is a field no change can
    /// be about, and it is silent: every existing fact still passes.
    /// </summary>
    [Fact]
    public void TheFieldEnumerationNamesEveryMovedFieldExactlyOnce()
    {
        var moved = typeof(SyncedState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        var named = Enum.GetNames<SyncedField>().ToList();

        Assert.NotEmpty(moved);

        Assert.Empty(moved.Where(field => !named.Contains(field, StringComparer.Ordinal))
            .Select(field => $"{field} moves and no member of {nameof(SyncedField)} names it, so no change can be about it."));

        Assert.Empty(named.Where(field => !moved.Contains(field, StringComparer.Ordinal))
            .Select(field => $"{field} is a member of {nameof(SyncedField)} and is not a field that moves, so it is an entry nothing can fill."));

        Assert.Equal(named.Count, named.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The first condition of #49. One playback is a start, a stream of progress reports and a
    /// finish, and the peer needs the last state of it.
    ///
    /// The two runs feed the same two hours at two report rates, so the assertion is that the
    /// count does not follow the number of reports rather than that it is small on one fixture.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public void AWholePlaybackLeavesOneEntryForThatItem(int secondsBetweenReports)
    {
        IReadOnlyList<RecordedChange> list = Array.Empty<RecordedChange>();
        var reports = (2 * 60 * 60) / secondsBetweenReports;

        for (var report = 1; report <= reports; report++)
        {
            var seconds = report * secondsBetweenReports;

            list = ChangeCollapse.Record(
                list,
                Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(seconds)), _evening.AddSeconds(seconds)));
        }

        list = ChangeCollapse.Record(
            list,
            Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening.AddHours(2)));

        var only = Assert.Single(list);

        Assert.Equal(SyncedField.Played, only.Field);
        Assert.True(only.Observed.Played);
    }

    /// <summary>
    /// The first half of the second condition. The completion answers the position, so what a
    /// peer reads is the completion and not a point somebody passed on the way to it.
    /// </summary>
    [Fact]
    public void APositionFollowedByAPlayedLeavesAPlayed()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(600)), _evening)),
            Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening.AddMinutes(90)));

        var only = Assert.Single(list);

        Assert.Equal(SyncedField.Played, only.Field);
    }

    /// <summary>
    /// The second half of it, and the case that separates this rule from the ratchet. Marking a
    /// work unwatched is a real thing a person does, and these are one server's own successive
    /// readings rather than two servers disagreeing, so the later one is what this server holds.
    /// </summary>
    [Fact]
    public void APlayedFollowedByAnExplicitUnplayedLeavesTheUnplayed()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening)),
            Change(SyncedField.Played, Reading(played: false, playCount: 1), _evening.AddHours(1)));

        var only = Assert.Single(list);

        Assert.Equal(SyncedField.Played, only.Field);
        Assert.False(only.Observed.Played);
    }

    /// <summary>
    /// The third condition. Three items watched in one evening keep the order they were first
    /// seen in, however much collapsing happens inside any of them.
    ///
    /// The two later reports are of the first two items, so an entry that collapses has to stay
    /// where it was rather than being removed and put back. A collapse that appended instead
    /// would leave the item watched first at the end of the list, which is the order a peer is
    /// handed a rewatch in.
    /// </summary>
    [Fact]
    public void CollapsingNeverReordersChangesToDifferentItems()
    {
        IReadOnlyList<RecordedChange> list = Array.Empty<RecordedChange>();

        list = Position(list, _film, Seconds(60), _evening);
        list = Position(list, _otherFilm, Seconds(90), _evening.AddMinutes(1));
        list = Position(list, _episode, Seconds(120), _evening.AddMinutes(2));

        list = Position(list, _film, Seconds(1200), _evening.AddMinutes(20));
        list = Position(list, _otherFilm, Seconds(1500), _evening.AddMinutes(25));

        Assert.Equal(
            new[] { _film, _otherFilm, _episode },
            list.Select(entry => entry.Subject.ItemId).ToArray());
    }

    /// <summary>
    /// What a supersession does to the order, stated rather than left to be discovered. It is a
    /// removal and not a move: the answered entry goes, and the arriving change is appended the
    /// way any first entry for a field is, so the subject takes its place at the end.
    ///
    /// The order among the entries that survive is untouched, which is what the condition above
    /// is about. A peer reads the list against a watermark rather than by position, and #48 is
    /// where what the list is ordered by becomes a property of a store rather than of this fold.
    /// </summary>
    [Fact]
    public void AnAnsweredEntryIsRemovedAndTheArrivingOneIsAppended()
    {
        IReadOnlyList<RecordedChange> list = Array.Empty<RecordedChange>();

        list = Position(list, _film, Seconds(60), _evening);
        list = Position(list, _otherFilm, Seconds(90), _evening.AddMinutes(1));

        list = ChangeCollapse.Record(
            list,
            Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening.AddMinutes(30), item: _film));

        Assert.Equal(
            new[] { _otherFilm, _film },
            list.Select(entry => entry.Subject.ItemId).ToArray());
    }

    /// <summary>
    /// The latest value and the earliest moment, in one entry. The moment is the earlier of the
    /// two rather than the arriving one, because a peer asks for what changed since its
    /// watermark, and an entry restamped on every later report falls out of that question for as
    /// long as the peer is away.
    /// </summary>
    [Fact]
    public void AnEntryKeepsTheLatestValueAndTheEarliestMoment()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(60)), _evening)),
            Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(1200)), _evening.AddMinutes(20)));

        var only = Assert.Single(list);

        Assert.Equal(Seconds(1200), only.Observed.PlaybackPositionTicks);
        Assert.Equal(_evening, only.FirstObservedAt);
    }

    /// <summary>
    /// The same, where the two arrive out of order. The earliest moment is the earlier of the
    /// two whichever one was recorded second, because an event queue that delivers late is a
    /// thing that happens and the moment is a property of the change rather than of the delivery.
    /// </summary>
    [Fact]
    public void AnEntryRecordedOutOfOrderStillKeepsTheEarliestMoment()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(1200)), _evening.AddMinutes(20))),
            Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(60)), _evening));

        Assert.Equal(_evening, Assert.Single(list).FirstObservedAt);
    }

    /// <summary>
    /// The fourth condition, first half. The fields that supersede are read out of the conflict
    /// table rather than listed here, in both directions, so the rule and the table cannot say
    /// different things about which field is the stronger statement.
    /// </summary>
    [Fact]
    public void TheFieldsThatSupersedeAreTheFieldsTheConflictTableGivesTheRatchetTo()
    {
        var ratcheted = ConflictTableTests.ConflictDocument
            .Rows(ConflictTableTests.ConflictDocument.Text())
            .Where(row => string.Equals(row.Rule, "ratchet", StringComparison.Ordinal))
            .Select(row => row.Field)
            .ToList();

        Assert.NotEmpty(ratcheted);

        Assert.Equal(
            ratcheted.OrderBy(field => field, StringComparer.Ordinal).ToArray(),
            ChangeCollapse.SupersedingFields
                .Select(field => field.ToString())
                .OrderBy(field => field, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// The other direction of the same closure, asserted through the rule rather than through the
    /// list it derives. A field the table settles by something other than the ratchet answers no
    /// outstanding entry about another field: a play count and a last played date are readings
    /// that stand beside a position rather than statements that answer it.
    /// </summary>
    [Fact]
    public void AFieldTheTableDoesNotGiveTheRatchetToSupersedesNothing()
    {
        var ratcheted = ConflictTableTests.ConflictDocument
            .Rows(ConflictTableTests.ConflictDocument.Text())
            .Where(row => string.Equals(row.Rule, "ratchet", StringComparison.Ordinal))
            .Select(row => row.Field)
            .ToHashSet(StringComparer.Ordinal);

        var settledOtherwise = Enum.GetValues<SyncedField>()
            .Where(field => !ratcheted.Contains(field.ToString()))
            .ToList();

        Assert.NotEmpty(settledOtherwise);

        Assert.All(settledOtherwise, arriving => Assert.All(
            Enum.GetValues<SyncedField>(),
            outstanding => Assert.False(
                ChangeCollapse.Supersedes(arriving, outstanding),
                $"{arriving} answers an outstanding {outstanding} and the conflict table does not give it the ratchet.")));
    }

    /// <summary>
    /// The fourth condition, second half, and the one that makes it one rule rather than two
    /// implementations of one rule. Whether the standing position is removed is asked of
    /// <see cref="PlayedRatchet"/>, so the collapse and the resolver cannot disagree about the
    /// pair even at the boundaries where each of them has a branch.
    ///
    /// The three rows are the three answers that rule gives: a position somebody reached against
    /// a completion, a position nobody reached, and a reading that already held the work played.
    /// </summary>
    [Theory]
    [InlineData(600, false, true)]
    [InlineData(0, false, true)]
    [InlineData(600, true, true)]
    [InlineData(600, true, false)]
    public void WhetherAStandingPositionIsRemovedIsTheRatchetsOwnAnswer(
        int standingPositionSeconds,
        bool standingReadingPlayed,
        bool arrivingReadingPlayed)
    {
        var standingReading = Reading(played: standingReadingPlayed, position: Seconds(standingPositionSeconds));
        var arrivingReading = Reading(played: arrivingReadingPlayed, playCount: arrivingReadingPlayed ? 1 : 0);

        var held = PlayedRatchet.Hold(standingReading, arrivingReading);

        var removed = held.Answer == RatchetAnswer.PlayedStands && held.PositionDiscardedHere is not null;

        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.PlaybackPositionTicks, standingReading, _evening)),
            Change(SyncedField.Played, arrivingReading, _evening.AddMinutes(90)));

        Assert.Equal(
            removed ? 1 : 2,
            list.Count);

        Assert.Equal(
            !removed,
            list.Any(entry => entry.Field == SyncedField.PlaybackPositionTicks));
    }

    /// <summary>
    /// The case the row above covers in the direction that is easy to get wrong by simplifying.
    /// An unmark arriving while a position was recorded on a work this server already held played
    /// is #34's case, the deliberate unplayed, and the position is kept: removing it here would
    /// be this rule deciding something it has no agreed record to decide.
    /// </summary>
    [Fact]
    public void AnUnmarkDoesNotRemoveAPositionRecordedWhileTheWorkWasPlayed()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.PlaybackPositionTicks, Reading(played: true, playCount: 1, position: Seconds(600)), _evening)),
            Change(SyncedField.Played, Reading(played: false, playCount: 1), _evening.AddMinutes(5)));

        Assert.Equal(2, list.Count);
        Assert.Contains(list, entry => entry.Field == SyncedField.PlaybackPositionTicks);
    }

    /// <summary>
    /// An arriving position never answers an outstanding completion. A position recorded after a
    /// completion is a rewatch in progress, and both statements are true of this server's record
    /// at once, so a collapse that dropped one of them would offer a peer half of what happened.
    /// </summary>
    [Fact]
    public void APositionArrivingAfterACompletionLeavesBoth()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening)),
            Change(SyncedField.PlaybackPositionTicks, Reading(played: true, playCount: 1, position: Seconds(300)), _evening.AddDays(1)));

        Assert.Equal(2, list.Count);
        Assert.Equal(
            new[] { SyncedField.Played, SyncedField.PlaybackPositionTicks },
            list.Select(entry => entry.Field).ToArray());
    }

    /// <summary>
    /// Two pairings of one server hold their own agreed record and their own watermark, so an
    /// entry outstanding for one of them says nothing about the other and neither collapses into
    /// the other.
    /// </summary>
    [Fact]
    public void TwoPairingsHoldTheirOwnEntries()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening)),
            Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening, pairing: _otherPairing));

        Assert.Equal(2, list.Count);
    }

    /// <summary>
    /// The same for two mapped users of one pairing, which is the half that would put one
    /// person's watch history into another person's exchange.
    /// </summary>
    [Fact]
    public void TwoMappedUsersHoldTheirOwnEntries()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening)),
            Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening, user: _otherUser));

        Assert.Equal(2, list.Count);
    }

    /// <summary>
    /// Two fields of one subject are two entries. The collapse is per field, and a rule that
    /// folded them would lose whichever of the two arrived first.
    /// </summary>
    [Fact]
    public void TwoFieldsOfOneSubjectAreTwoEntries()
    {
        var list = ChangeCollapse.Record(
            ChangeCollapse.Record(
                Array.Empty<RecordedChange>(),
                Change(SyncedField.PlayCount, Reading(playCount: 2), _evening)),
            Change(SyncedField.LastPlayedDate, Reading(playCount: 2, lastPlayed: new DateTime(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc)), _evening.AddHours(2)));

        Assert.Equal(2, list.Count);
    }

    /// <summary>
    /// The list is returned rather than mutated, so a caller that kept the previous one still has
    /// what it had. An exchange reading the list while a handler records into it is the case, and
    /// a shared mutable list would hand that reader a state that existed at no moment.
    /// </summary>
    [Fact]
    public void RecordingLeavesTheListItWasGivenAsItWas()
    {
        var before = ChangeCollapse.Record(
            Array.Empty<RecordedChange>(),
            Change(SyncedField.PlaybackPositionTicks, Reading(position: Seconds(60)), _evening));

        var after = ChangeCollapse.Record(
            before,
            Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening.AddHours(1)));

        Assert.Equal(SyncedField.PlaybackPositionTicks, Assert.Single(before).Field);
        Assert.Equal(SyncedField.Played, Assert.Single(after).Field);
    }

    /// <summary>
    /// An entry whose moment does not move is the entry that was already there, so a collapse
    /// over a list nothing changed in allocates nothing and hands back what it held.
    /// </summary>
    [Fact]
    public void AnEntryWhoseMomentDoesNotMoveIsTheOneThatWasThere()
    {
        var change = Change(SyncedField.PlayCount, Reading(playCount: 3), _evening);

        Assert.Same(change, change.ObservedSince(_evening));
        Assert.NotSame(change, change.ObservedSince(_evening.AddMinutes(-1)));
    }

    /// <summary>
    /// A caller that recorded nothing and called it a change. The three refusals are separate
    /// because each names a different mistake one step back from this rule.
    /// </summary>
    [Fact]
    public void ACallerThatRecordedNothingIsRefused()
    {
        var change = Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening);

        Assert.Throws<ArgumentNullException>(() => ChangeCollapse.Record(null!, change));
        Assert.Throws<ArgumentNullException>(() => ChangeCollapse.Record(Array.Empty<RecordedChange>(), null!));
        Assert.Throws<ArgumentException>(() => ChangeCollapse.Record(new RecordedChange[] { null! }, change));
    }

    /// <summary>
    /// What an entry refuses to be built out of. A pairing is what an entry is outstanding for,
    /// so an empty one is an entry no exchange can collect; a field outside the moved set is a
    /// change about nothing; and a position or a count below zero is not a reading this server
    /// produces, which the rules this collapse asks refuse rather than treat as ordinary values.
    /// </summary>
    [Fact]
    public void AnEntryRefusesWhatThisServerDoesNotProduce()
    {
        var subject = Subject(_user, _film);

        Assert.Throws<ArgumentNullException>(() =>
            new RecordedChange(_pairing, null!, SyncedField.Played, Reading(), _evening));

        Assert.Throws<ArgumentNullException>(() =>
            new RecordedChange(_pairing, subject, SyncedField.Played, null!, _evening));

        Assert.Throws<ArgumentException>(() =>
            new RecordedChange(Guid.Empty, subject, SyncedField.Played, Reading(), _evening));

        Assert.Throws<ArgumentException>(() =>
            new RecordedChange(_pairing, subject, (SyncedField)99, Reading(), _evening));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecordedChange(_pairing, subject, SyncedField.PlaybackPositionTicks, Reading(position: -1), _evening));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecordedChange(_pairing, subject, SyncedField.PlayCount, Reading(playCount: -1), _evening));
    }

    /// <summary>
    /// Two entries about one subject are the same subject whichever of them is asked, and an
    /// entry asked about nothing is refused rather than answered.
    /// </summary>
    [Fact]
    public void OneSubjectIsOneSubjectWhicheverEntryIsAsked()
    {
        var first = Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening);
        var second = Change(SyncedField.PlayCount, Reading(playCount: 1), _evening);
        var elsewhere = Change(SyncedField.Played, Reading(played: true, playCount: 1), _evening, item: _otherFilm);

        Assert.True(first.IsAboutTheSameSubjectAs(second));
        Assert.True(second.IsAboutTheSameSubjectAs(first));
        Assert.False(first.IsAboutTheSameSubjectAs(elsewhere));
        Assert.Throws<ArgumentNullException>(() => first.IsAboutTheSameSubjectAs(null!));
    }

    private static IReadOnlyList<RecordedChange> Position(
        IReadOnlyList<RecordedChange> list,
        Guid item,
        long position,
        DateTimeOffset moment) =>
        ChangeCollapse.Record(
            list,
            Change(SyncedField.PlaybackPositionTicks, Reading(position: position), moment, item: item));

    private static long Seconds(int seconds) => seconds * TimeSpan.TicksPerSecond;

    private static SyncedState Reading(
        bool played = false,
        int playCount = 0,
        long position = 0,
        DateTime? lastPlayed = null) =>
        new(played, playCount, position, lastPlayed);

    private static TransferSubject Subject(Guid user, Guid item)
    {
        var reading = TransferSubject.From(user, item, BaseItemKind.Movie);

        Assert.True(reading.IsSubject);

        return reading.Value!;
    }

    private static RecordedChange Change(
        SyncedField field,
        SyncedState observed,
        DateTimeOffset moment,
        Guid? pairing = null,
        Guid? user = null,
        Guid? item = null) =>
        new(pairing ?? _pairing, Subject(user ?? _user, item ?? _film), field, observed, moment);
}

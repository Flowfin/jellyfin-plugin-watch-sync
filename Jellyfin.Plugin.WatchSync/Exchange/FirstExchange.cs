using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Exchange;

/// <summary>
/// The run two servers make when they have never agreed anything for a mapped user.
///
/// It is the most dangerous run this plugin ever makes, because it is the one where the most
/// data moves and the one where every rule that reads what the two sides last agreed has
/// nothing to read. The prior art says so from experience: add the backend with the correct
/// state first, then force an export, or the wrong side wins
/// (https://github.com/arabcoders/watchstate/blob/master/FAQ.md).
///
/// What it does was decided on #37 on 2026-08-08. It applies the same conflict table as every
/// later exchange, seeds neither side, overwrites nothing, and records what it cannot decide.
/// <c>docs/conflicts.md</c> fixes the table and carries that decision under
/// <c>## The first exchange is this table and nothing else</c>; this type points at that
/// document rather than restating it.
///
/// <para>
/// Two things follow from applying the table with no agreement to read, and both are why this
/// mode exists rather than an ordinary run meeting an empty record. The deliberate unmark
/// <c>DeliberateUnplayed</c> holds against the ratchet needs an agreement to separate an intent
/// from an old value, so it cannot arise here at all and is not asked: that rule answers
/// <c>NoUnmarkToCarry</c> for every pair whose agreement is absent, which is its own contract
/// rather than a reading of this one. And a play count cannot be reckoned, which
/// <c>PlayCountAnswer.NoAgreement</c> refuses to guess at and sends here; what this run does
/// with it is written at <see cref="ReckonedWithNoAgreement"/>.
/// </para>
///
/// <para>
/// Nothing here writes. The apply path is #50 and the run that would call this is #55, and
/// neither is in this tree, so the two states arrive as parameters and what would be written is
/// carried out as an answer.
/// </para>
/// </summary>
public sealed class FirstExchange
{
    private FirstExchange(
        ExchangeMode mode,
        IReadOnlyList<FirstExchangeResolution> resolutions,
        AgreedRecords agreed)
    {
        Mode = mode;
        Resolutions = resolutions;
        Agreed = agreed;
    }

    /// <summary>
    /// Gets which mode this run was, which is <see cref="ExchangeMode.First"/> for every run
    /// this type makes.
    ///
    /// It is carried on the answer rather than left to be inferred, because #37 asks that the
    /// mode be distinguishable in the record from an ordinary run, and a record assembled from
    /// an answer that does not carry it would have to name the mode from somewhere else.
    /// </summary>
    public ExchangeMode Mode { get; }

    /// <summary>
    /// Gets what the run answered, one entry per item it was handed, in the order it was handed
    /// them.
    /// </summary>
    public IReadOnlyList<FirstExchangeResolution> Resolutions { get; }

    /// <summary>
    /// Gets the record of what the two sides have agreed after the run.
    ///
    /// It carries an agreement for every item the run decided and for every item an earlier
    /// interrupted run of the same exchange had already agreed, and nothing for an item left
    /// standing. The watermark is untouched: confirming a point says the whole set was
    /// exchanged, and that is the far side's answer rather than this run's, so a run that
    /// stopped halfway leaves a record that still reads as a first exchange.
    /// </summary>
    public AgreedRecords Agreed { get; }

    /// <summary>
    /// Gets the items the run decided.
    /// </summary>
    public IReadOnlyList<FirstExchangeResolution> Decided =>
        Resolutions.Where(each => each.Answer == ResolutionAnswer.Decided).ToList().AsReadOnly();

    /// <summary>
    /// Gets the items the run left standing, which are what an operator has to look at and what
    /// the status page in #62 has to show.
    /// </summary>
    public IReadOnlyList<FirstExchangeResolution> Undecided =>
        Resolutions.Where(each => each.Answer == ResolutionAnswer.Undecided).ToList().AsReadOnly();

    /// <summary>
    /// Reads out of the record whether a pairing and a mapped user are still in their first
    /// exchange.
    ///
    /// The point the two sides confirmed is what says the whole set was exchanged, so a record
    /// carrying none is a pair that has never finished one, whether it never started or was
    /// interrupted halfway. That is deliberate, and it is what makes the fourth condition of
    /// #37 answerable: a resumed run reads the same mode as the run it is resuming and skips
    /// what that run already agreed, rather than becoming an ordinary exchange the moment one
    /// item landed.
    /// </summary>
    /// <param name="records">The record of what the two sides last agreed.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentNullException">The record is absent.</exception>
    public static ExchangeMode ModeFor(AgreedRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records.Watermark.IsNoneYet ? ExchangeMode.First : ExchangeMode.Ordinary;
    }

    /// <summary>
    /// Runs a first exchange over a set of items.
    /// </summary>
    /// <param name="records">
    /// The record of what the two sides last agreed, which is empty on the first run and
    /// carries what an interrupted one reached.
    /// </param>
    /// <param name="items">The items and the two readings of each.</param>
    /// <param name="toleratedSkew">
    /// How far apart the two clocks may be before the position rule refuses to compare them.
    /// </param>
    /// <param name="now">
    /// This server's present moment, in UTC, which the position rule reads.
    /// </param>
    /// <param name="agreedAt">The moment an agreement this run records was reached.</param>
    /// <returns>The run.</returns>
    /// <exception cref="ArgumentNullException">The record or the items are absent.</exception>
    /// <exception cref="ArgumentException">
    /// The record is not in its first exchange, which is a caller running this mode over a pair
    /// that has already finished one, or an item is about another mapped user than the record.
    /// The first is refused rather than answered because this mode assumes there is nothing to
    /// have moved since a point, and a run that quietly did the ordinary work under the first
    /// exchange's name is the thing #37 asks the mode to be distinguishable from.
    /// </exception>
    public static FirstExchange Over(
        AgreedRecords records,
        IReadOnlyList<ItemOnBothSides> items,
        TimeSpan toleratedSkew,
        DateTime now,
        DateTimeOffset agreedAt)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(items);

        if (ModeFor(records) != ExchangeMode.First)
        {
            throw new ArgumentException(
                "The two sides have confirmed a point, so this is not their first exchange and this mode may not run over it.",
                nameof(records));
        }

        var resolutions = new List<FirstExchangeResolution>(items.Count);
        var agreed = records;

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item.Subject.MappedUserId != records.MappedUserId)
            {
                throw new ArgumentException(
                    "An item is about another mapped user, and a record of what two sides agreed is one user's.",
                    nameof(items));
            }

            if (agreed.For(item.Subject.ItemId) is not null)
            {
                resolutions.Add(FirstExchangeResolution.AlreadyAgreed(item));
                continue;
            }

            var resolution = Resolve(item, toleratedSkew, now);
            resolutions.Add(resolution);

            if (resolution.Resolved is SyncedState settled)
            {
                agreed = agreed.With(
                    new AgreedRecord(item.Subject, settled, agreedAt, EnvelopeVersions.Current));
            }
        }

        return new FirstExchange(
            ExchangeMode.First,
            new ReadOnlyCollection<FirstExchangeResolution>(resolutions),
            agreed);
    }

    /// <summary>
    /// What a first exchange does with two play counts and no agreement to reckon them against.
    ///
    /// The reckoning rule refuses this pair and #37 is where it is answered rather than guessed
    /// at. Three cases, and only one of them cannot be answered:
    ///
    /// <list type="bullet">
    /// <item>
    /// Two equal counts are agreed at that count. Nothing is seeded and nothing is overwritten,
    /// and the other reading, that the same number on both sides is two histories that happen
    /// to be the same length, is the reading that doubles somebody's count on the day two
    /// servers first meet.
    /// </item>
    /// <item>
    /// A side holding no plays at all contributes nothing, so the other side's count stands. No
    /// play is invented and none is discarded, because a count of zero is not a history that
    /// disagrees with anything.
    /// </item>
    /// <item>
    /// Two different counts, both above zero, cannot be told apart. Two sides holding two and
    /// three plays may be three watchings in total and may be five, and no reading of the two
    /// numbers separates them. The item is left standing.
    /// </item>
    /// </list>
    /// </summary>
    /// <param name="here">The count this server holds.</param>
    /// <param name="atThePeer">The count the peer offered.</param>
    /// <returns>The agreed count, or null where the pair cannot be told apart.</returns>
    private static int? ReckonedWithNoAgreement(int here, int atThePeer)
    {
        if (here == atThePeer)
        {
            return here;
        }

        if (here == 0)
        {
            return atThePeer;
        }

        return atThePeer == 0 ? here : null;
    }

    private static FirstExchangeResolution Resolve(
        ItemOnBothSides item,
        TimeSpan toleratedSkew,
        DateTime now)
    {
        if (ReckonedWithNoAgreement(item.Here.PlayCount, item.AtThePeer.PlayCount)
            is not int playCount)
        {
            return FirstExchangeResolution.Undecided(
                item,
                UndecidedReason.TwoHistoriesOfPlaysThatHaveNeverAgreed);
        }

        var ratchet = PlayedRatchet.Hold(item.Here, item.AtThePeer);
        var lastPlayed = LastPlayedMaximum.Take(item.Here, item.AtThePeer);

        if (ratchet.Answer == RatchetAnswer.PlayedStands)
        {
            return PositionUnderACompletion(item) is long held
                ? FirstExchangeResolution.Decided(
                    item,
                    new SyncedState(true, playCount, held, lastPlayed.LastPlayedDate))
                : FirstExchangeResolution.Undecided(
                    item,
                    UndecidedReason.TwoCompletionsHoldingDifferentPositions);
        }

        var position = PositionRecency.Settle(item.Here, item.AtThePeer, toleratedSkew, now);

        return position.Answer == PositionAnswer.PeerClockOutsideTolerance
            ? FirstExchangeResolution.Undecided(
                item,
                UndecidedReason.ThePeersClockIsOutsideTheTolerance)
            : FirstExchangeResolution.Decided(
                item,
                new SyncedState(
                    false,
                    playCount,
                    position.Position ?? 0,
                    lastPlayed.LastPlayedDate));
    }

    /// <summary>
    /// The position that stands where a completion is held.
    ///
    /// The ratchet's own answer is that the position offered against a completion is discarded,
    /// so where one side is played and the other is not, what stands is the played side's own
    /// position and the other is the loser the record names. Where both sides are played the
    /// ratchet discards neither, and two leftover positions on one finished work are a pair the
    /// table decides nothing between. So an equal pair is the position and an unequal one is
    /// null, which is this run leaving the item standing rather than picking a winner the table
    /// never named.
    /// </summary>
    /// <param name="item">The item and the two readings.</param>
    /// <returns>The position, or null where the two completions disagree about it.</returns>
    private static long? PositionUnderACompletion(ItemOnBothSides item)
    {
        if (item.Here.Played && item.AtThePeer.Played)
        {
            return item.Here.PlaybackPositionTicks == item.AtThePeer.PlaybackPositionTicks
                ? item.Here.PlaybackPositionTicks
                : null;
        }

        return item.Here.Played
            ? item.Here.PlaybackPositionTicks
            : item.AtThePeer.PlaybackPositionTicks;
    }
}

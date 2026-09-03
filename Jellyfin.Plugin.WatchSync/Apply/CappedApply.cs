using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Transfer;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// The one route from a decided set of items to a write, which judges the cap before anything
/// is written, and the approval that applies a run the cap stopped. This is the rest of #38.
///
/// <see cref="RunCap"/> is the rule and this is where it is asked. The rule was on the mainline
/// before anything called it, and the reading that put it there says why that was not enough: a
/// rule nothing calls stops nothing, and the cheapest thing in this plan to write is a caller
/// that hands a decided set straight to the walk. So the walk is not called from anywhere else.
/// A decided set reaches <see cref="ItemByItemApply"/> through this type, and the ordering rule
/// #38 carries is kept by construction rather than by care: nothing that applies changes to a
/// server lands ahead of the cap, because this is the only thing that applies them.
///
/// <para>
/// A run within the cap walks exactly as it would have without one, and pays nothing visible for
/// having been judged: no read the walk would not have made, no document, no record. That is
/// #38's fourth condition and it is what keeps the cap on. A cap that cost an ordinary evening
/// anything is the cap an operator turns off, and the operator who needs it most is the one who
/// has not thought about it.
/// </para>
///
/// <para>
/// A run the cap stops writes nothing and records what it would have done, item by item, with
/// what this server held at that moment beside what the run had decided. The plan is answered
/// rather than written, which is the shape <see cref="ApplyAnswer"/> gives its records: whether
/// it reaches the store is the caller's decision, and <see cref="StoppedRun.DocumentName"/> is
/// the name it goes under.
/// </para>
///
/// <para>
/// An approval applies exactly what a plan recorded, and nothing that changed in the meantime is
/// written without being noticed. Noticing is a comparison and not a recomputation: for every
/// item the approval reads what this server holds now and sets the item aside where that is not
/// what the plan recorded, and where the library no longer holds the item, and where the plan
/// had no baseline for it. What is left is handed to the walk as the plan wrote it. A plan
/// recomputed at approval would be a second run the operator never read, and a plan applied
/// without the comparison would overwrite whatever a person did in the days the plan waited.
/// </para>
///
/// <para>
/// The approval does not ask the cap again. The operator's approval is the answer to the cap's
/// question, and a plan that was stopped for exceeding a bound would be stopped again by the
/// same bound forever.
/// </para>
///
/// <para>
/// The matched count and both bounds arrive as parameters, for the reason the walk's failure
/// share does: the count is the matcher's answer and the bounds are per pairing, and this type
/// decides none of them. Where the bounds live is <c>docs/configuration.md</c>, and nothing
/// holds a pairing yet to keep them beside.
/// </para>
/// </summary>
public static class CappedApply
{
    /// <summary>
    /// Judges a decided set against the cap and walks it where it is within, or records the plan
    /// where it is not.
    /// </summary>
    /// <param name="user">The local user the mapping names, and the only one written to.</param>
    /// <param name="items">The items an exchange decided about, in the order to write them.</param>
    /// <param name="matched">How many items this person has matched, which the share is taken over.</param>
    /// <param name="maximumChanges">The count bound in force for this pairing.</param>
    /// <param name="maximumShare">The share bound in force for this pairing.</param>
    /// <param name="gateway">The one interface this plugin reads and writes a record through.</param>
    /// <param name="agreed">The agreed record as it stands before this run.</param>
    /// <param name="provenance">What this plugin has written under this pairing so far.</param>
    /// <param name="peerUserId">The peer user the decided values came from, as the peer names them.</param>
    /// <param name="envelopeVersion">The version of the envelope the changes arrived under.</param>
    /// <param name="maximumFailureShare">The share of attempted items the walk may fail before it stops.</param>
    /// <param name="now">The moment this run is running, by this server's clock.</param>
    /// <param name="cancellationToken">Stops the walk between two items.</param>
    /// <returns>The walk, or the plan.</returns>
    /// <exception cref="ArgumentNullException">
    /// The user, the list, the gateway or either record is null, or an entry of the list is.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A bound is outside what <see cref="RunCap"/> accepts, the failure share is outside what
    /// <see cref="FailureShare"/> accepts, or the matched count is below zero. All are refused
    /// before anything is read or written, because a run that wrote three items and then threw
    /// for a setting it was handed at the start has already changed somebody's record for a run
    /// that was never going to be legal.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The record is about another mapped user or another pairing than the one being written
    /// to, or an item is, which <see cref="ItemByItemApply"/> refuses and this does not soften.
    /// </exception>
    public static CappedApplyAnswer Apply(
        User user,
        IReadOnlyList<ItemToApply> items,
        int matched,
        int maximumChanges,
        double maximumShare,
        IUserDataGateway gateway,
        AgreedRecords agreed,
        ProvenanceRecords provenance,
        Guid peerUserId,
        int envelopeVersion,
        double maximumFailureShare,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(agreed);
        ArgumentNullException.ThrowIfNull(provenance);

        // Every setting is judged before the first read. The empty judgement of the failure
        // share is the same call the walk makes and answers TooFewToJudge; what it is used for
        // here is its refusal, so that a run the cap stops is not later approved into a walk
        // that refuses a share it was handed at the stop.
        FailureShare.Judge(0, 0, maximumFailureShare);

        var verdict = RunCap.Judge(items.Count, matched, maximumChanges, maximumShare);

        if (verdict.Answer == RunCapAnswer.Within)
        {
            return CappedApplyAnswer.Walked(
                verdict,
                ItemByItemApply.Apply(
                    user,
                    items,
                    gateway,
                    agreed,
                    provenance,
                    peerUserId,
                    envelopeVersion,
                    maximumFailureShare,
                    now,
                    cancellationToken));
        }

        // The crossing the walk refuses before its first write is refused here before the first
        // read, and over the whole set rather than item by item, so a plan is never recorded
        // for a set the walk would have thrown on.
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));

            if (item.Subject.MappedUserId != user.Id)
            {
                throw new ArgumentException(
                    "A decided item is about another mapped user than the one being written to, and the mapping is what says whose account a change belongs in.",
                    nameof(items));
            }
        }

        var planned = new List<StoppedRunItem>(items.Count);

        foreach (var item in items)
        {
            planned.Add(HeldNow(user, item.Item, gateway, out var held)
                ? StoppedRunItem.Read(item.Subject, item.Decided, held)
                : StoppedRunItem.Unread(item.Subject, item.Decided));
        }

        return CappedApplyAnswer.StoppedWith(
            verdict,
            StoppedRun.Of(agreed.PairingId, user.Id, verdict, matched, planned, now));
    }

    /// <summary>
    /// Applies a plan an operator approved, writing exactly what it recorded and setting aside
    /// every item that is not as the plan found it.
    /// </summary>
    /// <param name="plan">The run the cap stopped, as it was recorded.</param>
    /// <param name="user">The local user the mapping names, and the only one written to.</param>
    /// <param name="library">
    /// The item as this server holds it now, by identifier, or null where the library no longer
    /// holds it. The plan carries identifiers because it is a document that outlives the run, so
    /// the item is asked for again here, and this is the one place in the apply path that asks
    /// the library a second time.
    /// </param>
    /// <param name="gateway">The one interface this plugin reads and writes a record through.</param>
    /// <param name="agreed">The agreed record as it stands before this approval.</param>
    /// <param name="provenance">What this plugin has written under this pairing so far.</param>
    /// <param name="peerUserId">The peer user the decided values came from, as the peer names them.</param>
    /// <param name="envelopeVersion">The version of the envelope the changes arrived under.</param>
    /// <param name="maximumFailureShare">The share of attempted items the walk may fail before it stops.</param>
    /// <param name="approvedAt">The moment of the approval, by this server's clock.</param>
    /// <param name="cancellationToken">Stops the walk between two items.</param>
    /// <returns>What was written, what was refused, and what was set aside.</returns>
    /// <exception cref="ArgumentNullException">
    /// The plan, the user, the library, the gateway or either record is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The plan is about another mapped user than the one being written to, or about another
    /// pairing than the record being agreed. Both are refused before anything is read, for the
    /// reason the walk refuses the same crossing: the writes would land and nothing afterwards
    /// would look wrong.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The envelope version is not a whole number above zero, or the failure share is outside
    /// what <see cref="FailureShare"/> accepts.
    /// </exception>
    public static ApprovalAnswer Approve(
        StoppedRun plan,
        User user,
        Func<Guid, BaseItem?> library,
        IUserDataGateway gateway,
        AgreedRecords agreed,
        ProvenanceRecords provenance,
        Guid peerUserId,
        int envelopeVersion,
        double maximumFailureShare,
        DateTimeOffset approvedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(agreed);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentOutOfRangeException.ThrowIfLessThan(envelopeVersion, 1);

        if (plan.MappedUserId != user.Id)
        {
            throw new ArgumentException(
                "The plan is about another mapped user than the one being written to, so approving it would write one person's history into another person's account.",
                nameof(plan));
        }

        if (plan.PairingId != agreed.PairingId)
        {
            throw new ArgumentException(
                "The plan was stopped under another pairing than the record being agreed, so approving it would agree one peer's values under another peer's record.",
                nameof(plan));
        }

        FailureShare.Judge(0, 0, maximumFailureShare);

        var toWalk = new List<ItemToApply>(plan.Items.Count);
        var setAside = new List<ItemSetAside>();

        foreach (var planned in plan.Items)
        {
            if (!planned.HeldWasRead)
            {
                setAside.Add(new ItemSetAside(planned.Subject, SetAsideReason.HeldWasNotReadWhenTheRunStopped));
                continue;
            }

            var item = library(planned.Subject.ItemId);

            if (item is null)
            {
                setAside.Add(new ItemSetAside(planned.Subject, SetAsideReason.ItemGoneFromTheLibrary));
                continue;
            }

            if (!HeldNow(user, item, gateway, out var held))
            {
                setAside.Add(new ItemSetAside(planned.Subject, SetAsideReason.HeldCouldNotBeReadAtTheApproval));
                continue;
            }

            if (!StoppedRunItem.SameReading(held, planned.Held))
            {
                setAside.Add(new ItemSetAside(planned.Subject, SetAsideReason.HeldMovedSinceTheRunStopped));
                continue;
            }

            toWalk.Add(new ItemToApply(planned.Subject, item, planned.Decided));
        }

        return new ApprovalAnswer(
            ItemByItemApply.Apply(
                user,
                toWalk,
                gateway,
                agreed,
                provenance,
                peerUserId,
                envelopeVersion,
                maximumFailureShare,
                approvedAt,
                cancellationToken),
            setAside);
    }

    /// <summary>
    /// Reads what this server holds for one item, answering whether the read was made at all.
    ///
    /// Every failure a read can produce is caught here rather than a chosen few, for the reason
    /// the walk gives: this plugin does not own the server's user data manager and cannot
    /// enumerate what it throws. A cancellation is not a failure of the read and leaves.
    /// </summary>
    /// <param name="user">The local user the mapping names.</param>
    /// <param name="item">The item.</param>
    /// <param name="gateway">The one interface this plugin reads a record through.</param>
    /// <param name="held">What this server holds, or null where it holds nothing.</param>
    /// <returns>Whether the read was made.</returns>
    private static bool HeldNow(User user, BaseItem item, IUserDataGateway gateway, out SyncedState? held)
    {
        held = null;

        try
        {
            held = gateway.Read(user, item).State;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}

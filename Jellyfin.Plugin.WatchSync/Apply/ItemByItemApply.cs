using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// Applies a decided set of items one at a time, which is #54.
///
/// There is no transaction across two servers, so an exchange that half succeeded is a normal
/// outcome rather than an exceptional one. The rule this type is: each item is written on its
/// own, a failure on one does not stop the others, the failure is recorded with its reason, and
/// the agreed record for that item is left exactly as it was so the next exchange offers it
/// again. <c>docs/transfer.md</c> fixes that under the heading about nothing already applied
/// being unwound, and this is the walk that section is about.
///
/// <para>
/// Nothing already applied is unwound, for any reason, including the seventh item of ten failing.
/// An unwind is not a rollback here: it is a second pass of writes made at the moment something
/// is already going wrong, against a server that has just refused one, and it can fail halfway
/// itself. What it would leave then is a third state nobody planned, with some items at the
/// peer's value, some at the value they held before, and an agreed record matching neither. So
/// there is no path out of this walk that writes an item twice, and the property is structural
/// rather than remembered: the walk holds no record of what an item held before it, so it has
/// nothing to put back.
/// </para>
///
/// <para>
/// Every value is assigned rather than added to, which is what makes a second delivery of one
/// envelope indistinguishable from the first, #50. That is a property of
/// <see cref="IUserDataGateway.Write"/> and of the state this walk is handed, and the
/// invariant named for it refuses the spellings that add.
/// </para>
///
/// <para>
/// The mapped user is refused per item rather than trusted from the caller, which is #42's
/// fourth condition. A walk handed one person's items and another person's user would write one
/// household's history into somebody else's account, and every later exchange would agree it.
/// </para>
///
/// <para>
/// WHAT THIS DOES NOT DO, AND IT IS ANOTHER ISSUE'S CONDITION RATHER THAN AN OVERSIGHT. Nothing
/// here stamps provenance, and #44's first condition asks that every write path record it. The
/// reason is a defect in the record rather than a choice made here: the value written is held as
/// a whole number and not as a nullable one, and a write that clears the last played date writes
/// no number at all, so such a write has no representation and the entry for it would either be
/// missing or be a sentinel this walk invented. The finding is written on #44 with the reading
/// that produced it, and it is what has to move before this walk can stamp anything. Until it
/// does, an undo driven by provenance would not see what this walk wrote, and that is a live gap
/// rather than a deferred one.
/// </para>
/// </summary>
public static class ItemByItemApply
{
    /// <summary>
    /// The reason the server records against every write this plugin makes.
    ///
    /// Two of the server's seven reasons are ones an applied change would plausibly arrive under,
    /// which <c>docs/sync-model.md</c> establishes by reading both lines: import, and the one an
    /// ordinary update carries. This is import, because it is the one whose meaning is that a
    /// value came from outside this server, which is what an applied change is.
    ///
    /// It is not an identifier of this plugin and nothing may treat it as one. Both reasons are
    /// produced without this plugin being involved, a metadata scan under this one and anything
    /// holding an access token under the other, so a suppression that filtered on the reason
    /// would drop a person's own change and let an echo through. Suppressing the echo is the
    /// agreed record and the window in #16.
    /// </summary>
    private const UserDataSaveReason WrittenBecauseItCameFromAPeer = UserDataSaveReason.Import;

    /// <summary>
    /// Writes each decided item on its own and answers what was written and what was not.
    /// </summary>
    /// <param name="user">The local user the mapping names, and the only one written to.</param>
    /// <param name="items">The items an exchange decided about, in the order to write them.</param>
    /// <param name="gateway">The one interface this plugin writes a record through.</param>
    /// <param name="agreed">The agreed record as it stands before this walk.</param>
    /// <param name="envelopeVersion">The version of the envelope the changes arrived under.</param>
    /// <param name="appliedAt">The moment this walk is running, by this server's clock.</param>
    /// <param name="cancellationToken">Stops the walk between two items.</param>
    /// <returns>What was written, what was not, and the record advanced for the written.</returns>
    /// <exception cref="ArgumentNullException">
    /// The user, the list, the gateway or the record is null, or an entry of the list is.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The record is about another mapped user than the one being written to, or an item is. Both
    /// are the same failure seen from two ends, and neither is visible in an answer: the writes
    /// would land, the record would be written, and one person's history would be in another
    /// person's account.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The envelope version is not a whole number above zero. An agreement records which version
    /// carried it, so a version below one would be written into every entry this walk agrees.
    /// </exception>
    public static ApplyAnswer Apply(
        User user,
        IReadOnlyList<ItemToApply> items,
        IUserDataGateway gateway,
        AgreedRecords agreed,
        int envelopeVersion,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(agreed);
        ArgumentOutOfRangeException.ThrowIfLessThan(envelopeVersion, 1);

        if (agreed.MappedUserId != user.Id)
        {
            throw new ArgumentException(
                "The agreed record is about another mapped user than the one being written to, so this walk would agree one person's state under another person's record.",
                nameof(agreed));
        }

        var applied = new List<TransferSubject>();
        var failed = new List<ApplyFailure>();

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));

            if (item.Subject.MappedUserId != user.Id)
            {
                throw new ArgumentException(
                    "A decided item is about another mapped user than the one being written to, and the mapping is what says whose account a change belongs in.",
                    nameof(items));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!Written(user, item, gateway, cancellationToken, out var refusal))
            {
                failed.Add(new ApplyFailure(item.Subject, refusal!));
                continue;
            }

            agreed = agreed.With(
                new AgreedRecord(item.Subject, item.Decided, appliedAt, envelopeVersion));

            applied.Add(item.Subject);
        }

        return new ApplyAnswer(applied, failed, agreed);
    }

    /// <summary>
    /// Writes one item, answering whether it was written and what it was refused with.
    ///
    /// Every failure a write can produce is caught here rather than a chosen few. This plugin
    /// does not own the server's user data manager and cannot enumerate what it throws, and a
    /// walk that caught the failures somebody thought of would stop at the first one nobody did,
    /// which is the all-or-nothing outcome #54 refuses. The type name is carried out so that an
    /// operator can still tell one refusal from another.
    ///
    /// A cancellation is not a failure of the item and is not recorded as one. It is asked for by
    /// this side, it says nothing about the item or about the peer, and an entry naming it would
    /// put a row on a page for something an operator did.
    /// </summary>
    /// <param name="user">The local user the mapping names.</param>
    /// <param name="item">The decided item.</param>
    /// <param name="gateway">The one interface this plugin writes a record through.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="refusal">The name of the type the write was refused with, where it was.</param>
    /// <returns>Whether the item was written.</returns>
    private static bool Written(
        User user,
        ItemToApply item,
        IUserDataGateway gateway,
        CancellationToken cancellationToken,
        out string? refusal)
    {
        refusal = null;

        try
        {
            gateway.Write(
                user,
                item.Item,
                item.Decided,
                WrittenBecauseItCameFromAPeer,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception thrown)
        {
            refusal = thrown.GetType().Name;

            return false;
        }

        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
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
/// there is no path out of this walk that writes an item twice. This paragraph gave the reason as
/// the walk holding no record of what an item held before it, and that reason has gone: it reads
/// exactly that value now and puts it in the record of provenance. The rule stands on its own
/// terms instead, which is where it always belonged, and the paragraph below says why the record
/// being available does not make the unwind available with it.
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
/// Every value it writes is stamped, which is #44's first condition. The stamp is one entry per
/// field the write changed, holding what this server held immediately before it and what was put
/// there, so a revocation can put back what came from a peer. The paragraph above said this walk
/// could not stamp anything, because the record held the value written as a whole number and a
/// write that clears a last played date writes no number at all; the record takes an absence on
/// both members now, so the write that clears has a representation and the reason to wait is
/// gone.
/// </para>
///
/// <para>
/// The stamp does not make the unwind above possible and is not a step towards it. What it holds
/// is written to the store for a revocation that arrives days or months later, and it is read by
/// an operator action rather than by anything inside a walk. A walk that read it back to put
/// items right would be the second pass of writes the paragraph above refuses, made at the moment
/// the server is already refusing one.
/// </para>
///
/// <para>
/// A write that changed nothing is stamped with nothing, and that is the record's rule rather
/// than this walk's shortcut: <see cref="ProvenanceRecord"/> refuses an entry whose written value
/// is the value that was already there, because there is nothing for an undo to put back. So the
/// count of entries is the count of fields that moved and never the count of writes, and a walk
/// that wrote a decided state identical to what this server already held advances the agreed
/// record and stamps no provenance.
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
    /// <param name="provenance">What this plugin has written under this pairing so far.</param>
    /// <param name="peerUserId">The peer user the decided values came from, as the peer names them.</param>
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
    /// person's account. The provenance is a third end of the same failure and is refused the same
    /// way, on the pairing as well as on the user, because an undo is bounded by the pairing that
    /// was revoked and an entry filed under the wrong one is either reverted on a revocation that
    /// has nothing to do with it or left standing on the one that does. A peer user that names
    /// nobody is refused for the reason the record gives: an undo bounded by a mapping cannot
    /// decide whether such an entry is in scope.
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
        ProvenanceRecords provenance,
        Guid peerUserId,
        int envelopeVersion,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(agreed);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentOutOfRangeException.ThrowIfLessThan(envelopeVersion, 1);

        if (agreed.MappedUserId != user.Id)
        {
            throw new ArgumentException(
                "The agreed record is about another mapped user than the one being written to, so this walk would agree one person's state under another person's record.",
                nameof(agreed));
        }

        if (provenance.MappedUserId != user.Id)
        {
            throw new ArgumentException(
                "The record of provenance is about another mapped user than the one being written to, so what this walk wrote into one person's record would be filed under another person's.",
                nameof(provenance));
        }

        if (provenance.PairingId != agreed.PairingId)
        {
            throw new ArgumentException(
                "The record of provenance is about another pairing than the one being agreed, and an undo is bounded by the pairing that was revoked, so these writes would be reverted on a revocation they have nothing to do with or left standing on the one that revoked them.",
                nameof(provenance));
        }

        if (peerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "A stamp of provenance says which peer user a value came from, and this walk was given nobody, so an undo bounded by a mapping could not decide whether what it wrote is in scope.",
                nameof(peerUserId));
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

            if (!Written(user, item, gateway, cancellationToken, out var held, out var refusal))
            {
                failed.Add(new ApplyFailure(item.Subject, refusal!));
                continue;
            }

            provenance = Stamped(provenance, item, held, peerUserId, appliedAt);

            agreed = agreed.With(
                new AgreedRecord(item.Subject, item.Decided, appliedAt, envelopeVersion));

            applied.Add(item.Subject);
        }

        return new ApplyAnswer(applied, failed, agreed, provenance);
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
    /// <param name="held">What this server held immediately before the write, where it was made.</param>
    /// <param name="refusal">The name of the type the write was refused with, where it was.</param>
    /// <returns>Whether the item was written.</returns>
    private static bool Written(
        User user,
        ItemToApply item,
        IUserDataGateway gateway,
        CancellationToken cancellationToken,
        out SyncedState? held,
        out string? refusal)
    {
        held = null;
        refusal = null;

        try
        {
            // Read inside the same attempt as the write, so a read this server refuses is a
            // failure of the item rather than something thrown out of the walk. It is also the
            // last moment the value being replaced can be read: a read taken earlier in the run
            // would be what the person held before the exchange started rather than before this
            // write, and an undo would put back a value somebody had already moved on from.
            held = gateway.Read(user, item.Item).State;

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

    /// <summary>
    /// The record of provenance with one entry for each field this write changed.
    ///
    /// Per field rather than per write, because an undo puts one value back and has to know which
    /// value it is. A write moves the fields the conflict table decided about and leaves the rest
    /// where they were, and an entry for a field that did not move would tell a revocation to
    /// restore a value this plugin never touched.
    ///
    /// A reading this server holds nothing for answers nothing for every field rather than the
    /// values an unwatched item would carry. Nothing and a never-watched record are different
    /// states, and restoring the second where the first was true leaves a row on an item the
    /// person has never opened.
    /// </summary>
    /// <param name="provenance">The record as it stands before this write.</param>
    /// <param name="item">The item that was written, with the state that was decided for it.</param>
    /// <param name="held">What this server held immediately before the write.</param>
    /// <param name="peerUserId">The peer user the values came from.</param>
    /// <param name="writtenAt">The moment of the write, by this server's clock.</param>
    /// <returns>The record carrying what this write replaced.</returns>
    private static ProvenanceRecords Stamped(
        ProvenanceRecords provenance,
        ItemToApply item,
        SyncedState? held,
        Guid peerUserId,
        DateTimeOffset writtenAt)
    {
        foreach (var field in Enum.GetValues<SyncedField>())
        {
            var before = RecordedValue.Of(held, field);
            var written = RecordedValue.Of(item.Decided, field);

            if (before == written)
            {
                continue;
            }

            provenance = provenance.With(new ProvenanceRecord(
                provenance.PairingId,
                item.Subject.MappedUserId,
                peerUserId,
                item.Subject.ItemId,
                field,
                before,
                written,
                writtenAt));
        }

        return provenance;
    }
}

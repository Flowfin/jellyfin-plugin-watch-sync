using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// What one walk over a decided set of items did.
///
/// Four things, and two of them are records the caller writes. The applied and the failed
/// together say what the walk examined, which is the rule <c>docs/transfer.md</c> puts on every
/// run: a run that covered less than everything is never reported as one that covered it all. The
/// agreed record is advanced for the applied items and for no others, so an item that failed keeps
/// the record it had and is offered again unchanged. The provenance carries what each of those
/// writes replaced, which is #44.
///
/// Both records are answered rather than written. Whether either reaches the store, and in which
/// order against the watermark, is the exchange's decision in <c>docs/transfer.md</c> and not this
/// walk's: the walk is handed two records and answers two, so a caller that stops between the two
/// leaves the records it started with rather than half of a new one.
///
/// The two are not interchangeable and a caller that wrote one without the other would leave a
/// state neither of them describes. An agreed record advanced with no provenance beside it is an
/// exchange whose writes cannot be undone; provenance written with the agreement left behind is
/// a set of items this server offers the peer again and would then undo twice.
///
/// A walk that was cancelled answers fewer applied and fewer failed than it was handed items. It
/// stops between two items and never inside one, so the difference is the tail nothing was tried
/// on, and every item it did reach is in one of the two lists. A walk stopped by
/// <see cref="FailureShare"/> ends the same way and is told apart by
/// <see cref="StoppedOnFailureShare"/>, because the two are the same shortfall for opposite
/// reasons: one is this side asking the walk to stop, and the other is this side failing.
/// </summary>
public sealed class ApplyAnswer
{
    private readonly List<TransferSubject> _applied;

    private readonly List<ApplyFailure> _failed;

    internal ApplyAnswer(
        List<TransferSubject> applied,
        List<ApplyFailure> failed,
        AgreedRecords agreed,
        ProvenanceRecords provenance,
        bool stoppedOnFailureShare)
    {
        _applied = applied;
        _failed = failed;
        Agreed = agreed;
        Provenance = provenance;
        StoppedOnFailureShare = stoppedOnFailureShare;
    }

    /// <summary>
    /// Gets the items that were written, in the order the walk reached them.
    /// </summary>
    public IReadOnlyList<TransferSubject> Applied => _applied;

    /// <summary>
    /// Gets the items that were not written, each with what the write was refused with.
    /// </summary>
    public IReadOnlyList<ApplyFailure> Failed => _failed;

    /// <summary>
    /// Gets the agreed record with an entry for every item that was written.
    ///
    /// It is the record the walk was handed where nothing was written at all, rather than a
    /// record of its own, so a caller cannot tell an empty walk from one it never made by
    /// comparing what it holds.
    /// </summary>
    public AgreedRecords Agreed { get; }

    /// <summary>
    /// Gets what this plugin wrote, with an entry for every field a write changed.
    ///
    /// It is the record the walk was handed where no write changed anything, for the reason the
    /// agreed record is: a caller cannot tell a walk that stamped nothing from one it never made
    /// by comparing what the record holds.
    ///
    /// An entry per changed field rather than per write. A write moves the fields the conflict
    /// table decided about and leaves the rest where they were, so a walk over three items that
    /// each moved one field answers three entries and not three items' worth.
    /// </summary>
    public ProvenanceRecords Provenance { get; }

    /// <summary>
    /// Gets how many items the walk reached, whether it wrote them or not.
    /// </summary>
    public int Examined => _applied.Count + _failed.Count;

    /// <summary>
    /// Gets a value indicating whether the walk stopped because its failures had stopped being
    /// about the items, which is <see cref="FailureShare"/>.
    ///
    /// It is on the answer rather than left to be inferred from the counts, because the inference
    /// is not available: a walk of ten items that stopped after the eighth and a walk of eight that
    /// finished leave the same two lists, and the difference is whether an operator is looking at
    /// an exchange that ran or at a side that is refusing writes. <c>docs/transfer.md</c> puts the
    /// same rule on every run in this plan, that one which covered less than everything is never
    /// reported as one that covered it all, and this is what carries it here.
    ///
    /// A caller may not read it the other way round. False says this rule did not stop the walk,
    /// and never that the walk reached every item it was handed: a cancellation ends one early and
    /// answers false, which is the pair the summary above separates.
    /// </summary>
    public bool StoppedOnFailureShare { get; }
}

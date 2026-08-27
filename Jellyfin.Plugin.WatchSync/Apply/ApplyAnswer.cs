using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// What one walk over a decided set of items did.
///
/// Three things, and the third is the one the next exchange is decided against. The applied and
/// the failed together say what the walk examined, which is the rule <c>docs/transfer.md</c> puts
/// on every run: a run that covered less than everything is never reported as one that covered it
/// all. The agreed record is advanced for the applied items and for no others, so an item that
/// failed keeps the record it had and is offered again unchanged.
///
/// The record is answered rather than written. Whether it reaches the store, and in which order
/// against the watermark, is the exchange's decision in <c>docs/transfer.md</c> and not this
/// walk's: the walk is handed a record and answers one, so a caller that stops between the two
/// leaves the record it started with rather than half of a new one.
///
/// A walk that was cancelled answers fewer applied and fewer failed than it was handed items. It
/// stops between two items and never inside one, so the difference is the tail nothing was tried
/// on, and every item it did reach is in one of the two lists.
/// </summary>
public sealed class ApplyAnswer
{
    private readonly List<TransferSubject> _applied;

    private readonly List<ApplyFailure> _failed;

    internal ApplyAnswer(
        List<TransferSubject> applied,
        List<ApplyFailure> failed,
        AgreedRecords agreed)
    {
        _applied = applied;
        _failed = failed;
        Agreed = agreed;
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
    /// Gets how many items the walk reached, whether it wrote them or not.
    /// </summary>
    public int Examined => _applied.Count + _failed.Count;
}

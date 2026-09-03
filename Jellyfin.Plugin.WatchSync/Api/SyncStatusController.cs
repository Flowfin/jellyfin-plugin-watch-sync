using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using Jellyfin.Plugin.WatchSync.Transfer;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// The sync status of one pairing for one person, and the export of its unmatched items, which
/// is #62 as far as the records reach.
///
/// Nothing is decided here. Every number is read out of the document the behaviour wrote, by
/// the record type's own reader, and the surface adds a route, the server's own authorisation
/// and a shape a caller can read. The rule #62 turns on, that no number is counted separately
/// for display, is kept by there being no count in this file: each is the record's <c>Count</c>.
/// The one record that is not a document is the sweep's last run, which the task keeps in
/// memory because the server hands the task to nobody, and it is read from there.
///
/// <para>
/// Both are elevated, for the reason the record endpoints are: every number here is about one
/// person's history on one pairing, and an action about another person's record is the case the
/// rule under <c>endpoint-user-from-the-request</c> names elevation for. The policy is the
/// constant the server declares rather than a string.
/// </para>
///
/// <para>
/// Which pairing and which person are the caller's to name, in the route, as they are for the
/// record endpoints. This plugin holds no pairing and cannot list them, which is #40, so a page
/// asks the operator for both identifiers rather than offering a choice. A pairing this plugin
/// has never exchanged on and a pairing that does not exist answer identically, with every
/// record absent, and that is deliberate: this plugin cannot tell them apart without asking the
/// pairing plugin, and an answer that did would tell a caller which pairings exist.
/// </para>
///
/// <para>
/// Nothing here opens a document about another person. Every document is named for the pairing
/// and the person it is about, so the reads are by name and a document about somebody else is
/// never read on the way to answering about this one. That is #62's fourth condition kept where
/// the data is fetched rather than where it is rendered, and there is no title anywhere in the
/// answer to withhold.
/// </para>
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
public class SyncStatusController : ControllerBase
{
    /// <summary>
    /// How many reasons the status names beside the unmatched count.
    ///
    /// Three, because the status is a glance and the export is the list. An operator reading the
    /// status wants to know whether the unmatched items are one problem or many, and the top
    /// three answer that; every reason with its count is in the export.
    /// </summary>
    public const int ReasonsShown = 3;

    private readonly DocumentStore _store;
    private readonly SweepRuns _sweeps;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStatusController"/> class.
    /// </summary>
    /// <param name="store">The store this plugin keeps its documents in.</param>
    /// <param name="sweeps">Where the scheduled sweep keeps its last run.</param>
    public SyncStatusController(DocumentStore store, SweepRuns sweeps)
    {
        _store = store;
        _sweeps = sweeps;
    }

    /// <summary>
    /// The sync status of one pairing for one person.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>The status.</returns>
    [HttpGet("Plugins/WatchSync/Pairings/{pairingId}/Persons/{mappedUserId}/Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<SyncStatus> Status([FromRoute] Guid pairingId, [FromRoute] Guid mappedUserId)
    {
        if (pairingId == Guid.Empty || mappedUserId == Guid.Empty)
        {
            return BadRequest();
        }

        return new SyncStatus(
            pairingId,
            mappedUserId,
            StoppedRunOf(pairingId, mappedUserId),
            LastExchangeOf(pairingId, mappedUserId),
            LastSweepOf(),
            UnmatchedOf(pairingId, mappedUserId),
            ConflictsOf(pairingId, mappedUserId));
    }

    /// <summary>
    /// Every unmatched item of one pairing for one person, which is the export.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>The items.</returns>
    [HttpGet("Plugins/WatchSync/Pairings/{pairingId}/Persons/{mappedUserId}/Unmatched")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<UnmatchedExport> Unmatched([FromRoute] Guid pairingId, [FromRoute] Guid mappedUserId)
    {
        if (pairingId == Guid.Empty || mappedUserId == Guid.Empty)
        {
            return BadRequest();
        }

        var reading = Read(UnmatchedRecords.DocumentName(pairingId, mappedUserId), out var document);

        if (reading != RecordReading.Read)
        {
            return new UnmatchedExport(pairingId, mappedUserId, reading, Array.Empty<UnmatchedExportEntry>());
        }

        var records = UnmatchedRecords.Read(document!);

        if (records.IsRefused)
        {
            return new UnmatchedExport(pairingId, mappedUserId, RecordReading.Unreadable, Array.Empty<UnmatchedExportEntry>());
        }

        return new UnmatchedExport(
            pairingId,
            mappedUserId,
            RecordReading.Read,
            records.Records!.All
                .Select(entry => new UnmatchedExportEntry(
                    entry.ItemId,
                    entry.Kind.ToString(),
                    entry.Refusal.ToString(),
                    entry.Answer?.ToString(),
                    entry.LastAttemptedAt))
                .ToList());
    }

    private StoppedRunStatus StoppedRunOf(Guid pairingId, Guid mappedUserId)
    {
        var reading = Read(StoppedRun.DocumentName(pairingId, mappedUserId), out var document);

        if (reading != RecordReading.Read)
        {
            return new StoppedRunStatus(reading, null, null, null, null, null);
        }

        var plan = StoppedRun.Read(document!);

        if (plan.IsRefused)
        {
            return new StoppedRunStatus(RecordReading.Unreadable, null, null, null, null, null);
        }

        var run = plan.Run!;

        return new StoppedRunStatus(
            RecordReading.Read,
            run.Answer,
            run.Changes,
            run.Allowed,
            run.Matched,
            run.StoppedAt);
    }

    private LastExchangeStatus LastExchangeOf(Guid pairingId, Guid mappedUserId)
    {
        var reading = Read(AgreedRecords.DocumentName(pairingId, mappedUserId), out var document);

        if (reading != RecordReading.Read)
        {
            return new LastExchangeStatus(reading, false, null, null);
        }

        var agreed = AgreedRecords.Read(document!);

        if (agreed.IsRefused)
        {
            return new LastExchangeStatus(RecordReading.Unreadable, false, null, null);
        }

        var records = agreed.Records!;

        return new LastExchangeStatus(
            RecordReading.Read,
            !records.Watermark.IsNoneYet,
            records.Watermark.IsNoneYet ? null : records.Watermark.ConfirmedAt,
            records.Count);
    }

    /// <summary>
    /// What the last sweep did, from the run record the sweep keeps.
    ///
    /// It takes no pairing and no person because the run is the server's: the sweep walks the
    /// records the store holds rather than pairs today, so one run is over every pairing and
    /// every person at once. Where no run has ended since the server started, the status says
    /// so rather than answering zeros a reader would take for a run that examined nothing.
    /// </summary>
    /// <returns>The status of the last run.</returns>
    private LastSweepStatus LastSweepOf()
    {
        var run = _sweeps.Last;

        return run is null ? LastSweepStatus.NoneSinceTheServerStarted : LastSweepStatus.Of(run);
    }

    private UnmatchedStatus UnmatchedOf(Guid pairingId, Guid mappedUserId)
    {
        var reading = Read(UnmatchedRecords.DocumentName(pairingId, mappedUserId), out var document);

        if (reading != RecordReading.Read)
        {
            return new UnmatchedStatus(reading, 0, Array.Empty<UnmatchedReasonCount>());
        }

        var unmatched = UnmatchedRecords.Read(document!);

        if (unmatched.IsRefused)
        {
            return new UnmatchedStatus(RecordReading.Unreadable, 0, Array.Empty<UnmatchedReasonCount>());
        }

        var records = unmatched.Records!;

        return new UnmatchedStatus(RecordReading.Read, records.Count, TopReasons(records.All));
    }

    private ConflictStatus ConflictsOf(Guid pairingId, Guid mappedUserId)
    {
        var reading = Read(ConflictRecords.DocumentName(pairingId, mappedUserId), out var document);

        if (reading != RecordReading.Read)
        {
            return new ConflictStatus(reading, 0, null);
        }

        var conflicts = ConflictRecords.Read(document!);

        if (conflicts.IsRefused)
        {
            return new ConflictStatus(RecordReading.Unreadable, 0, null);
        }

        var records = conflicts.Records!;

        return new ConflictStatus(
            RecordReading.Read,
            records.Count,
            records.Count == 0 ? null : records.All.Max(conflict => conflict.RecordedAt));
    }

    /// <summary>
    /// The reasons that account for the most unmatched items, most frequent first.
    ///
    /// The reason of an entry is the refusal where a key could not be derived and the lookup's
    /// answer where it could, which is the record's own rule about which of the two an entry
    /// carries. Ties are broken by name so two reads of one record answer the same list.
    /// </summary>
    /// <param name="entries">The record's entries.</param>
    /// <returns>At most <see cref="ReasonsShown"/> reasons with their counts.</returns>
    private static List<UnmatchedReasonCount> TopReasons(IReadOnlyList<UnmatchedRecord> entries) =>
        entries
            .Select(entry => entry.Answer?.ToString() ?? entry.Refusal.ToString())
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .Select(group => new UnmatchedReasonCount(group.Key, group.Count()))
            .OrderByDescending(reason => reason.Count)
            .ThenBy(reason => reason.Reason, StringComparer.Ordinal)
            .Take(ReasonsShown)
            .ToList();

    /// <summary>
    /// Reads one document by name, telling absence, a document this code may read, and one it
    /// may not apart.
    ///
    /// A document from the future is unreadable rather than absent, because a status that
    /// answered nothing for a record a newer version wrote would tell an operator a sync has
    /// never run on a pairing that is running under the newer plugin on the other server.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <param name="document">The document, where it may be read.</param>
    /// <returns>What the read came back with.</returns>
    private RecordReading Read(string name, out StoredDocument? document)
    {
        document = null;

        var reading = _store.Read(name);

        if (reading is null)
        {
            return RecordReading.Absent;
        }

        if (reading.Document is null)
        {
            return RecordReading.Unreadable;
        }

        document = reading.Document;

        return RecordReading.Read;
    }
}

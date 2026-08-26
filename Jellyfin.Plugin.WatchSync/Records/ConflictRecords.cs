using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// Every conflict written down under one pairing for one mapped user, as a document the store
/// keeps.
///
/// This is the fourth condition of #36: the record survives a restart, because the question an
/// operator asks it is usually asked the next day. A record that lived in memory would answer
/// only for the run that produced it, and the run that produced it is the one nobody was
/// watching.
///
/// <para>
/// It is a list and not an object keyed on the item, which is the one place this shape departs
/// from <see cref="Jellyfin.Plugin.WatchSync.Agreement.AgreedRecords"/>. An agreement is a
/// current fact about an item and a later one replaces it; a conflict is an event that happened,
/// and two conflicts about one item are two answers an operator may need to see. Replacing the
/// first with the second would hide the case they are most often asking about, which is a field
/// that keeps being decided the same way against them.
/// </para>
///
/// <para>
/// So the bound cannot come from the number of matched items the way #14's does, and this record
/// carries two of its own. <see cref="MaximumEntries"/> is what one document may hold, and
/// <see cref="Retaining"/> drops what is older than a retention. Both are here because the issue
/// asks for both: watch history is personal data and this is a diagnostic rather than an archive
/// of what somebody watched, so it expires; and a peer that conflicts on every exchange must not
/// be able to fill a disk between two sweeps, so it is capped as well. A retention alone leaves
/// the disk unbounded inside the window, and a cap alone keeps the oldest entries for as long as
/// nothing new arrives, which for a pairing that has gone quiet is forever.
/// </para>
///
/// <para>
/// It is immutable and every change answers with a new record, for the reason
/// <c>DocumentStore.Write</c> imposes: a change handed to the store is computed from the
/// document that was on disk when the attempt began and is made again where somebody else
/// replaced it in between, so it has to be a function of what it was handed.
/// </para>
///
/// <para>
/// What this does not decide: nothing writes one yet. The first condition of #36 asks that every
/// branch of the resolver write a record, and the three rules on the mainline are reached
/// independently with nothing driving them together. The third asks that the retention be
/// enforced after a sweep, and the sweep is #55 and the setting is #58; what is here is the rule
/// the sweep will call and the two numbers it will be handed, declared with the reason for each
/// rather than a member on a configuration nothing reads.
/// </para>
/// </summary>
public sealed class ConflictRecords
{
    /// <summary>
    /// The prefix of the document's name in the store.
    /// </summary>
    internal const string NamePrefix = "conflicts-";

    /// <summary>
    /// The member naming the pairing the conflicts arose on.
    /// </summary>
    internal const string PairingMember = "pairing";

    /// <summary>
    /// The member naming the mapped user the conflicts are about.
    /// </summary>
    internal const string UserMember = "user";

    /// <summary>
    /// The member holding the conflicts, oldest first.
    /// </summary>
    internal const string ConflictsMember = "conflicts";

    /// <summary>
    /// The member of one entry naming the item the two readings were about.
    /// </summary>
    internal const string ItemMember = "item";

    /// <summary>
    /// The member of one entry naming the moved field.
    /// </summary>
    internal const string FieldMember = "field";

    /// <summary>
    /// The member of one entry naming the rule that decided.
    /// </summary>
    internal const string RuleMember = "rule";

    /// <summary>
    /// The member of one entry holding what this server held.
    /// </summary>
    internal const string HereMember = "here";

    /// <summary>
    /// The member of one entry holding what the peer offered.
    /// </summary>
    internal const string AtThePeerMember = "atThePeer";

    /// <summary>
    /// The member of one entry naming the side whose reading did not survive.
    /// </summary>
    internal const string DiscardedMember = "discarded";

    /// <summary>
    /// The member of one entry holding the moment the rule decided.
    /// </summary>
    internal const string RecordedAtMember = "recordedAt";

    private readonly IReadOnlyList<ConflictRecord> _conflicts;

    private ConflictRecords(
        Guid pairingId,
        Guid mappedUserId,
        IReadOnlyList<ConflictRecord> conflicts)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        _conflicts = conflicts;
    }

    /// <summary>
    /// Gets how many conflicts one document may hold before the oldest are dropped.
    ///
    /// It is a number rather than a setting for now, and #58 is where it becomes one. What it is
    /// for is the pairing that conflicts on every exchange: an operator reading this record wants
    /// the recent decisions, and the two hundredth copy of the same decision answers nothing the
    /// first ten did not. Two hundred is large enough that a day of real disagreement is whole in
    /// it and small enough that the document stays a file somebody can read.
    /// </summary>
    public static int MaximumEntries => 200;

    /// <summary>
    /// Gets how long a conflict is kept by default.
    ///
    /// The default is short because the record is a diagnostic and the entries are about what a
    /// person watched. Fourteen days is the span in which the question is actually asked: an
    /// operator notices an episode marked watched, or a person says their position moved, within
    /// the fortnight rather than in the quarter.
    /// </summary>
    public static TimeSpan DefaultRetention => TimeSpan.FromDays(14);

    /// <summary>
    /// Gets the longest retention this plugin will keep conflicts for.
    ///
    /// A maximum exists because the setting is one an operator can raise, and every day it is
    /// raised by is another day of somebody's viewing kept in a file that was never meant to be
    /// an archive. Ninety days is the outer edge of a diagnostic: past it, what is being kept is
    /// a history of what a household watched rather than an account of what this plugin did.
    /// </summary>
    public static TimeSpan MaximumRetention => TimeSpan.FromDays(90);

    /// <summary>
    /// Gets the pairing these conflicts arose on.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the mapped user these conflicts are about, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets how many conflicts are recorded.
    /// </summary>
    public int Count => _conflicts.Count;

    /// <summary>
    /// Gets the conflicts, oldest first.
    /// </summary>
    public IReadOnlyList<ConflictRecord> All => _conflicts;

    /// <summary>
    /// The record of a pairing and a mapped user that has recorded no conflict.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <returns>A record holding no conflict.</returns>
    /// <exception cref="ArgumentException">Either identifier is empty.</exception>
    public static ConflictRecords NoneYet(Guid pairingId, Guid mappedUserId)
    {
        RefuseAnEmptyIdentifier(pairingId, nameof(pairingId));
        RefuseAnEmptyIdentifier(mappedUserId, nameof(mappedUserId));

        return new ConflictRecords(pairingId, mappedUserId, Array.Empty<ConflictRecord>());
    }

    /// <summary>
    /// What this record is called in the store.
    ///
    /// The name is derived from what the record is about rather than counted, the way #14's is,
    /// so two pairings or two users never collide on one document and neither needs to know what
    /// the other has written.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <returns>The document's name, without a suffix.</returns>
    public static string DocumentName(Guid pairingId, Guid mappedUserId) =>
        NamePrefix
        + pairingId.ToString("n", CultureInfo.InvariantCulture)
        + "-"
        + mappedUserId.ToString("n", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads one document in this plugin's store as the conflicts recorded for a pairing and a
    /// mapped user.
    ///
    /// Every entry is read or the document is refused, and the count is the one thing not refused
    /// on. A document holding more than <see cref="MaximumEntries"/> is read as it stands and
    /// trimmed at the next <see cref="With"/>, because the alternative refuses an operator's
    /// whole record on the day somebody lowers the cap, and this record is the one kind in the
    /// store that no reconciliation can rebuild.
    /// </summary>
    /// <param name="document">The document, already read at a version this code may read.</param>
    /// <returns>The records, or the reason the document is not one.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static ConflictRecordsReading Read(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryReadIdentifier(document.Fields, PairingMember, out var pairingId)
            || !TryReadIdentifier(document.Fields, UserMember, out var mappedUserId)
            || document.Fields[ConflictsMember] is not JsonArray entries)
        {
            return ConflictRecordsReading.NotARecordOfConflicts();
        }

        var conflicts = new List<ConflictRecord>();

        foreach (var entry in entries)
        {
            if (!TryReadConflict(pairingId, mappedUserId, entry, out var conflict))
            {
                return ConflictRecordsReading.NotARecordOfConflicts();
            }

            conflicts.Add(conflict!);
        }

        return ConflictRecordsReading.Readable(
            new ConflictRecords(pairingId, mappedUserId, conflicts));
    }

    /// <summary>
    /// This record with one more conflict in it, dropping the oldest where that takes the
    /// document past <see cref="MaximumEntries"/>.
    ///
    /// The conflict goes on the end rather than into a sorted position. The order is the order
    /// the rules decided in, which is what an operator is reading the record for, and sorting on
    /// the recorded moment would reorder two conflicts a clock reported at the same instant.
    /// </summary>
    /// <param name="conflict">The conflict to record.</param>
    /// <returns>A record carrying it.</returns>
    /// <exception cref="ArgumentNullException">The conflict is null.</exception>
    /// <exception cref="ArgumentException">
    /// The conflict is about another pairing or another mapped user. A document is one pairing's
    /// and one user's, so a conflict from elsewhere would be readable afterwards under a pairing
    /// and a person it never happened to.
    /// </exception>
    public ConflictRecords With(ConflictRecord conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        if (conflict.PairingId != PairingId)
        {
            throw new ArgumentException(
                "The conflict arose on another pairing, and this record is one pairing's.",
                nameof(conflict));
        }

        if (conflict.MappedUserId != MappedUserId)
        {
            throw new ArgumentException(
                "The conflict is about another mapped user, and this record is one user's.",
                nameof(conflict));
        }

        var conflicts = new List<ConflictRecord>(_conflicts) { conflict };

        if (conflicts.Count > MaximumEntries)
        {
            conflicts.RemoveRange(0, conflicts.Count - MaximumEntries);
        }

        return new ConflictRecords(PairingId, MappedUserId, conflicts);
    }

    /// <summary>
    /// This record with everything recorded before a moment dropped.
    ///
    /// This is the retention, expressed as the boundary rather than as a span, because the rule
    /// reads no clock: the caller subtracts the retention from the present moment it was given,
    /// which is the shape every rule under <c>Conflict/</c> takes. What calls it is the sweep in
    /// #55, and what supplies the span is the setting in #58.
    /// </summary>
    /// <param name="from">The oldest moment kept. A conflict recorded before it is dropped.</param>
    /// <returns>A record holding what is still inside the retention.</returns>
    public ConflictRecords Retaining(DateTimeOffset from) =>
        new ConflictRecords(
            PairingId,
            MappedUserId,
            _conflicts.Where(conflict => conflict.RecordedAt >= from).ToList());

    /// <summary>
    /// This record as a document the store can write.
    /// </summary>
    /// <returns>The document.</returns>
    public StoredDocument ToDocument()
    {
        var conflicts = new JsonArray();

        foreach (var conflict in _conflicts)
        {
            conflicts.Add(Written(conflict));
        }

        var fields = new JsonObject
        {
            [PairingMember] =
                JsonValue.Create(PairingId.ToString("n", CultureInfo.InvariantCulture)),
            [UserMember] =
                JsonValue.Create(MappedUserId.ToString("n", CultureInfo.InvariantCulture)),
            [ConflictsMember] = conflicts,
        };

        return StoredDocument.At(DocumentVersions.Current, fields);
    }

    private static void RefuseAnEmptyIdentifier(Guid identifier, string name)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "A record of conflicts is about one pairing and one mapped user, and this one is empty.",
                name);
        }
    }

    private static JsonObject Written(ConflictRecord conflict) => new JsonObject
    {
        [ItemMember] =
            JsonValue.Create(conflict.ItemId.ToString("n", CultureInfo.InvariantCulture)),
        [FieldMember] = JsonValue.Create(conflict.Field.ToString()),
        [RuleMember] = JsonValue.Create(conflict.Rule.ToString()),
        [HereMember] = conflict.Here is null ? null : JsonValue.Create(conflict.Here.Value),
        [AtThePeerMember] =
            conflict.AtThePeer is null ? null : JsonValue.Create(conflict.AtThePeer.Value),
        [DiscardedMember] = JsonValue.Create(conflict.Discarded.ToString()),
        [RecordedAtMember] =
            JsonValue.Create(conflict.RecordedAt.ToString("o", CultureInfo.InvariantCulture)),
    };

    private static bool TryReadIdentifier(JsonObject fields, string member, out Guid identifier)
    {
        identifier = Guid.Empty;

        return fields[member] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Guid.TryParseExact(text, "n", out identifier)
            && identifier != Guid.Empty;
    }

    /// <summary>
    /// Reads one entry of the document as a conflict.
    ///
    /// It goes back through the record's own constructor rather than into fields of its own, so a
    /// document naming a side that lost a reading it never held is refused on the way in by the
    /// same rule that refuses one on the way out. A record that could be read holding that shape
    /// would be a way around the refusal for anything written by hand, and what it would tell an
    /// operator is that this plugin discarded a value nobody had.
    /// </summary>
    /// <param name="pairingId">The pairing the document is under.</param>
    /// <param name="mappedUserId">The mapped user the document is about.</param>
    /// <param name="node">The entry.</param>
    /// <param name="conflict">What it holds, where it is a conflict.</param>
    /// <returns>Whether the entry is a conflict.</returns>
    private static bool TryReadConflict(
        Guid pairingId,
        Guid mappedUserId,
        JsonNode? node,
        out ConflictRecord? conflict)
    {
        conflict = null;

        if (node is not JsonObject members
            || !TryReadIdentifier(members, ItemMember, out var itemId)
            || !TryReadName<SyncedField>(members, FieldMember, out var field)
            || !TryReadName<ConflictRule>(members, RuleMember, out var rule)
            || !TryReadName<ConflictSide>(members, DiscardedMember, out var discarded)
            || !TryReadReading(members, HereMember, out var here)
            || !TryReadReading(members, AtThePeerMember, out var atThePeer)
            || !TryReadMoment(members, RecordedAtMember, out var recordedAt))
        {
            return false;
        }

        try
        {
            conflict = new ConflictRecord(
                pairingId,
                mappedUserId,
                itemId,
                field,
                rule,
                here,
                atThePeer,
                discarded,
                recordedAt);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads one member as the name of an enumeration member.
    ///
    /// The name has to come back out of the enumeration unchanged, which is what refuses a number
    /// written where a name belongs. A document carrying a field or a rule by number would keep
    /// meaning whatever that position happened to be on the day it was written, and both of those
    /// enumerations gain members as this plan lands rows.
    /// </summary>
    /// <typeparam name="T">The enumeration.</typeparam>
    /// <param name="members">The entry.</param>
    /// <param name="member">The member to read.</param>
    /// <param name="named">What it names.</param>
    /// <returns>Whether the member names one.</returns>
    private static bool TryReadName<T>(JsonObject members, string member, out T named)
        where T : struct, Enum
    {
        named = default;

        return members[member] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Enum.TryParse(text, out named)
            && string.Equals(named.ToString(), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads one of the two readings, which is a whole number or null.
    ///
    /// A missing member and a member holding null are different documents and only the second is
    /// a conflict, the way #14's last played date is: the first is a document somebody assembled
    /// without the field, and reading it as "this side held nothing" would invent the half of the
    /// conflict that decides whether a side lost anything at all.
    ///
    /// Both widths are tried, for the reason written at #14's reader: a number parsed out of
    /// bytes converts between them and one assembled in memory does not, so a document this
    /// record has just built and one the store has just read are not the same subject to a reader
    /// that asks for one width only.
    /// </summary>
    /// <param name="members">The entry.</param>
    /// <param name="member">The member to read.</param>
    /// <param name="reading">The reading, or null where there is none.</param>
    /// <returns>Whether the entry carries the member at all.</returns>
    private static bool TryReadReading(JsonObject members, string member, out long? reading)
    {
        reading = null;

        if (!members.TryGetPropertyValue(member, out var node))
        {
            return false;
        }

        if (node is null)
        {
            return true;
        }

        if (node is not JsonValue value)
        {
            return false;
        }

        if (!value.TryGetValue<long>(out var number))
        {
            if (!value.TryGetValue<int>(out var narrower))
            {
                return false;
            }

            number = narrower;
        }

        reading = number;
        return true;
    }

    private static bool TryReadMoment(JsonObject members, string member, out DateTimeOffset moment)
    {
        moment = default;

        return members[member] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParseExact(
                text,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out moment);
    }
}

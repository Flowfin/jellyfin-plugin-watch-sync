using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// Every value this plugin wrote under one pairing for one mapped user, as a document the store
/// keeps.
///
/// #44's second condition asks that the value this plugin replaced be recoverable for every write
/// inside the retention window, and a record that lived in memory would answer only for the run
/// that wrote it. A revocation is not an event this plugin schedules: it arrives days or months
/// after the writes it is about, and by then the only run that could have answered has been over
/// for a long time.
///
/// <para>
/// It is a list and not an object keyed on the item and the field, which is the shape
/// <see cref="ConflictRecords"/> takes and for a related reason. A write is an event that
/// happened. Two writes to one field are two events, and the second one's replaced value is what
/// the person held between them, which is a value this plugin never wrote and has no other record
/// of. Keeping only the latest write per field would be enough for an undo and would lose that,
/// and keeping only the earliest would make an undo restore a value the person had already
/// replaced themselves.
/// </para>
///
/// <para>
/// So an undo walks this list newest first and stops at the first entry for a field: that entry's
/// <see cref="ProvenanceRecord.Written"/> is what the record should still be standing on if
/// nobody has touched it since, and its <see cref="ProvenanceRecord.Before"/> is what to put
/// back. Where the current value is something else the person changed it afterwards, their action
/// outranks the undo, and #44's third condition is that the skip is recorded rather than forced.
/// The walk is the caller's and there is no caller, which is why nothing here performs it.
/// </para>
///
/// <para>
/// It carries two bounds of its own, <see cref="MaximumEntries"/> and the retention
/// <see cref="Retaining"/> enforces, and both of them cost something this record's siblings do
/// not pay. An entry dropped by either is a write that can no longer be undone, and nothing tells
/// an operator that a revocation reached back as far as it could rather than as far as it should.
/// That residual is real and it is the price of the alternative being an archive: this is the one
/// kind in the store that holds copies of what somebody watched, and a record kept for as long as
/// a pairing lives is a history of a household rather than an account of what this plugin did.
/// The cap drops the oldest, which is the direction the walk above wants, because the entry an
/// undo reads for any field is the newest one.
/// </para>
///
/// <para>
/// It is immutable and every change answers with a new record, for the reason
/// <c>DocumentStore.Write</c> imposes: a change handed to the store is computed from the document
/// that was on disk when the attempt began and is made again where somebody else replaced it in
/// between, so it has to be a function of what it was handed.
/// </para>
///
/// <para>
/// What this does not decide: nothing writes one yet. #44's first condition asks that every write
/// path record provenance over the whole apply surface, and nothing calls
/// <c>IUserDataGateway.Write</c>. The retention is a setting in that issue's fourth condition and
/// there is no setting here either; what is on the type is the rule a sweep will call and the two
/// numbers it will be handed, in the shape <see cref="ConflictRecords"/> took, and the sweep is
/// #55 and the setting is #58. The default that condition asks to be stated in the privacy note
/// is #107.
/// </para>
/// </summary>
public sealed class ProvenanceRecords
{
    /// <summary>
    /// The prefix of the document's name in the store.
    /// </summary>
    internal const string NamePrefix = "provenance-";

    /// <summary>
    /// The member naming the pairing the values came in on.
    /// </summary>
    internal const string PairingMember = "pairing";

    /// <summary>
    /// The member naming the mapped user the writes were against.
    /// </summary>
    internal const string UserMember = "user";

    /// <summary>
    /// The member holding the writes, oldest first.
    /// </summary>
    internal const string WritesMember = "writes";

    /// <summary>
    /// The member of one entry naming the peer user the value came from.
    /// </summary>
    internal const string PeerUserMember = "peerUser";

    /// <summary>
    /// The member of one entry naming the item the value was written against.
    /// </summary>
    internal const string ItemMember = "item";

    /// <summary>
    /// The member of one entry naming the moved field.
    /// </summary>
    internal const string FieldMember = "field";

    /// <summary>
    /// The member of one entry holding what this server held before the write.
    /// </summary>
    internal const string BeforeMember = "before";

    /// <summary>
    /// The member of one entry holding what this plugin wrote, which is null where what it wrote
    /// was the absence of a value.
    /// </summary>
    internal const string WrittenMember = "written";

    /// <summary>
    /// The member of one entry holding the moment of the write.
    /// </summary>
    internal const string WrittenAtMember = "writtenAt";

    private readonly IReadOnlyList<ProvenanceRecord> _writes;

    private ProvenanceRecords(
        Guid pairingId,
        Guid mappedUserId,
        IReadOnlyList<ProvenanceRecord> writes)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        _writes = writes;
    }

    /// <summary>
    /// Gets how many writes one document may hold before the oldest are dropped.
    ///
    /// It is a number rather than a setting for now, and #58 is where it becomes one. It is an
    /// order of magnitude above what ordinary use produces rather than a comfortable fit, because
    /// what it costs when it binds is an undo that cannot reach: twenty runs at
    /// <c>RunCap.DefaultMaximumChanges</c> fit under it whole, and a pairing that has reached it
    /// has changed one person's record two thousand times.
    /// </summary>
    public static int MaximumEntries => 2000;

    /// <summary>
    /// Gets how long the provenance of a write is kept by default.
    ///
    /// It is longer than <see cref="ConflictRecords.DefaultRetention"/> and it is measured against
    /// a different thing, which is why the two numbers differ rather than one of them being
    /// wrong. A conflict is kept for as long as somebody might ask about it, and the answer to
    /// that is a fortnight. This is kept for as long as the undo it exists for might be asked for,
    /// and that is decided by when a pairing is revoked, which nobody here schedules. Ninety days
    /// covers the revocation that follows a household arrangement ending, and it stops well short
    /// of being a year of somebody's viewing held against an event that may never happen.
    /// </summary>
    public static TimeSpan DefaultRetention => TimeSpan.FromDays(90);

    /// <summary>
    /// Gets the longest retention this plugin will keep the provenance of a write for.
    ///
    /// A maximum exists because the setting is one an operator can raise, and every day it is
    /// raised by is another day of copies of somebody's viewing kept for an undo. A year is where
    /// the trade turns: past it the record is being kept for a revocation nobody expects, and what
    /// is actually being held is a year of what a household watched.
    /// </summary>
    public static TimeSpan MaximumRetention => TimeSpan.FromDays(365);

    /// <summary>
    /// Gets the pairing these values came in on.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the mapped user these writes were against, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets how many writes are recorded.
    /// </summary>
    public int Count => _writes.Count;

    /// <summary>
    /// Gets the writes, oldest first.
    /// </summary>
    public IReadOnlyList<ProvenanceRecord> All => _writes;

    /// <summary>
    /// The record of a pairing and a mapped user this plugin has written nothing for.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <returns>A record holding no write.</returns>
    /// <exception cref="ArgumentException">Either identifier is empty.</exception>
    public static ProvenanceRecords NoneYet(Guid pairingId, Guid mappedUserId)
    {
        RefuseAnEmptyIdentifier(pairingId, nameof(pairingId));
        RefuseAnEmptyIdentifier(mappedUserId, nameof(mappedUserId));

        return new ProvenanceRecords(pairingId, mappedUserId, Array.Empty<ProvenanceRecord>());
    }

    /// <summary>
    /// What this record is called in the store.
    ///
    /// The name is derived from what the record is about rather than counted, the way #14's and
    /// #36's are, so two pairings or two users never collide on one document and neither needs to
    /// know what the other has written. A revocation is per pairing, so a walk over the store can
    /// find every document a revoked pairing wrote by its name alone.
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
    /// Reads one document in this plugin's store as the provenance recorded for a pairing and a
    /// mapped user.
    ///
    /// Every entry is read or the document is refused, and the count is the one thing not refused
    /// on, which is <see cref="ConflictRecords.Read"/>'s rule for the same reason: refusing a
    /// document holding more than the cap would turn an operator's whole record unreadable on the
    /// day somebody lowers the cap. A document over the cap is read as it stands and trimmed at
    /// the next <see cref="With"/>.
    /// </summary>
    /// <param name="document">The document, already read at a version this code may read.</param>
    /// <returns>The records, or the reason the document is not one.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static ProvenanceRecordsReading Read(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryReadIdentifier(document.Fields, PairingMember, out var pairingId)
            || !TryReadIdentifier(document.Fields, UserMember, out var mappedUserId)
            || document.Fields[WritesMember] is not JsonArray entries)
        {
            return ProvenanceRecordsReading.NotARecordOfProvenance();
        }

        var writes = new List<ProvenanceRecord>();

        foreach (var entry in entries)
        {
            if (!TryReadWrite(pairingId, mappedUserId, entry, out var write))
            {
                return ProvenanceRecordsReading.NotARecordOfProvenance();
            }

            writes.Add(write!);
        }

        return ProvenanceRecordsReading.Readable(
            new ProvenanceRecords(pairingId, mappedUserId, writes));
    }

    /// <summary>
    /// This record with one more write in it, dropping the oldest where that takes the document
    /// past <see cref="MaximumEntries"/>.
    ///
    /// The write goes on the end rather than into a sorted position. The order is the order the
    /// writes happened in, which is what an undo walks backwards, and sorting on the recorded
    /// moment would reorder two writes a clock reported at the same instant.
    /// </summary>
    /// <param name="write">The write to record.</param>
    /// <returns>A record carrying it.</returns>
    /// <exception cref="ArgumentNullException">The write is null.</exception>
    /// <exception cref="ArgumentException">
    /// The write is about another pairing or another mapped user. A document is one pairing's and
    /// one user's, so a write from elsewhere would be readable afterwards as one this plugin made
    /// on a pairing it never came in on, and an undo bounded by a revoked pairing would revert it.
    /// </exception>
    public ProvenanceRecords With(ProvenanceRecord write)
    {
        ArgumentNullException.ThrowIfNull(write);

        if (write.PairingId != PairingId)
        {
            throw new ArgumentException(
                "The write came in on another pairing, and this record is one pairing's.",
                nameof(write));
        }

        if (write.MappedUserId != MappedUserId)
        {
            throw new ArgumentException(
                "The write was against another mapped user, and this record is one user's.",
                nameof(write));
        }

        var writes = new List<ProvenanceRecord>(_writes) { write };

        if (writes.Count > MaximumEntries)
        {
            writes.RemoveRange(0, writes.Count - MaximumEntries);
        }

        return new ProvenanceRecords(PairingId, MappedUserId, writes);
    }

    /// <summary>
    /// This record with everything written before a moment dropped.
    ///
    /// This is the retention, expressed as the boundary rather than as a span, because the rule
    /// reads no clock: the caller subtracts the retention from the present moment it was given,
    /// which is the shape every rule in this plugin takes. What calls it is the sweep in #55, and
    /// what supplies the span is the setting in #58.
    /// </summary>
    /// <param name="from">The oldest moment kept. A write made before it is dropped.</param>
    /// <returns>A record holding what is still inside the retention.</returns>
    public ProvenanceRecords Retaining(DateTimeOffset from) =>
        new ProvenanceRecords(
            PairingId,
            MappedUserId,
            _writes.Where(write => write.WrittenAt >= from).ToList());

    /// <summary>
    /// This record as a document the store can write.
    /// </summary>
    /// <returns>The document.</returns>
    public StoredDocument ToDocument()
    {
        var writes = new JsonArray();

        foreach (var write in _writes)
        {
            writes.Add(Written(write));
        }

        var fields = new JsonObject
        {
            [PairingMember] =
                JsonValue.Create(PairingId.ToString("n", CultureInfo.InvariantCulture)),
            [UserMember] =
                JsonValue.Create(MappedUserId.ToString("n", CultureInfo.InvariantCulture)),
            [WritesMember] = writes,
        };

        return StoredDocument.At(DocumentVersions.Current, fields);
    }

    private static void RefuseAnEmptyIdentifier(Guid identifier, string name)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "A record of provenance is about one pairing and one mapped user, and this one is empty.",
                name);
        }
    }

    private static JsonObject Written(ProvenanceRecord write) => new JsonObject
    {
        [PeerUserMember] =
            JsonValue.Create(write.PeerUserId.ToString("n", CultureInfo.InvariantCulture)),
        [ItemMember] =
            JsonValue.Create(write.ItemId.ToString("n", CultureInfo.InvariantCulture)),
        [FieldMember] = JsonValue.Create(write.Field.ToString()),
        [BeforeMember] = write.Before is null ? null : JsonValue.Create(write.Before.Value),
        [WrittenMember] = write.Written is null ? null : JsonValue.Create(write.Written.Value),
        [WrittenAtMember] =
            JsonValue.Create(write.WrittenAt.ToString("o", CultureInfo.InvariantCulture)),
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
    /// Reads one entry of the document as a write.
    ///
    /// It goes back through the record's own constructor rather than into fields of its own, so a
    /// document naming no peer user, or claiming a write that replaced a value with itself, is
    /// refused on the way in by the same rule that refuses one on the way out. That is what
    /// refuses an entry holding null in both values, which is a document saying this plugin wrote
    /// nothing over nothing, rather than the reader holding a rule of its own about the member. A record that could
    /// be read holding either shape would be a way around the refusal for anything written by
    /// hand, and what the second of them would give an undo is a write to revert that never
    /// changed anything.
    /// </summary>
    /// <param name="pairingId">The pairing the document is under.</param>
    /// <param name="mappedUserId">The mapped user the document is about.</param>
    /// <param name="node">The entry.</param>
    /// <param name="write">What it holds, where it is a write.</param>
    /// <returns>Whether the entry is a write.</returns>
    private static bool TryReadWrite(
        Guid pairingId,
        Guid mappedUserId,
        JsonNode? node,
        out ProvenanceRecord? write)
    {
        write = null;

        if (node is not JsonObject members
            || !TryReadIdentifier(members, PeerUserMember, out var peerUserId)
            || !TryReadIdentifier(members, ItemMember, out var itemId)
            || !TryReadName<SyncedField>(members, FieldMember, out var field)
            || !TryReadValue(members, BeforeMember, out var before)
            || !TryReadValue(members, WrittenMember, out var written)
            || !TryReadMoment(members, WrittenAtMember, out var writtenAt))
        {
            return false;
        }

        try
        {
            write = new ProvenanceRecord(
                pairingId,
                mappedUserId,
                peerUserId,
                itemId,
                field,
                before,
                written,
                writtenAt);
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
    /// written where a name belongs. A document carrying a field by number would keep meaning
    /// whatever that position happened to be on the day it was written.
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
    /// Reads one of the two values, which is a whole number or null.
    ///
    /// A missing member and a member holding null are different documents, the way #14's last
    /// played date and #36's two readings are: the first is a document somebody assembled without
    /// the field, and reading it as "this server held nothing" would invent the half of the record
    /// that decides what an undo puts back. That holds on both members for the same reason. Null
    /// under the written value is a write that cleared a person's last played date, and a missing
    /// member there is a document that never said what was written at all; reading the second as
    /// the first would give an undo a clearing to reverse that nothing performed.
    ///
    /// Both widths are tried, for the reason written at #14's reader: a number parsed out of bytes
    /// converts between them and one assembled in memory does not, so a document this record has
    /// just built and one the store has just read are not the same subject to a reader that asks
    /// for one width only.
    /// </summary>
    /// <param name="members">The entry.</param>
    /// <param name="member">The member to read.</param>
    /// <param name="value">The value, or null where there is none.</param>
    /// <returns>Whether the entry carries the member at all.</returns>
    private static bool TryReadValue(JsonObject members, string member, out long? value)
    {
        value = null;

        if (!members.TryGetPropertyValue(member, out var node))
        {
            return false;
        }

        if (node is null)
        {
            return true;
        }

        if (node is not JsonValue held)
        {
            return false;
        }

        if (!held.TryGetValue<long>(out var number))
        {
            if (!held.TryGetValue<int>(out var narrower))
            {
                return false;
            }

            number = narrower;
        }

        value = number;
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

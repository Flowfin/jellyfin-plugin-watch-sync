using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// Everything this server and one peer have agreed, for one pairing and one mapped user.
///
/// One document per pairing and per mapped user, holding one entry per item. The shape is what
/// #14's fourth condition asks for rather than something a caller has to be careful about: the
/// entries are an object keyed on the item, so agreeing the same item again replaces an entry
/// and an evening of playback leaves the record the size it was. Nothing here can grow with the
/// number of playback events, because there is nowhere for a second entry about one item to go.
/// What that shape does not bound is a peer offering items this side has never agreed, one
/// exchange at a time, and <see cref="MaximumEntries"/> is #313's answer to it.
///
/// The pairing is the document rather than a column in it. Two pairings of one server agree
/// separately, so an agreement under one of them says nothing about the other, and a record
/// keyed on both would make every read of one pairing a read of all of them.
///
/// It is immutable and every change answers with a new record. That is what the store's write
/// path needs: a change handed to <c>DocumentStore.Write</c> is computed from the document that
/// was on disk when the attempt began and is made again where somebody else replaced it in
/// between, so it has to be a function of what it was handed. A record that mutated what it was
/// given would carry the first attempt's entry into the second.
/// </summary>
public sealed class AgreedRecords
{
    /// <summary>
    /// The prefix of the document's name in the store.
    /// </summary>
    internal const string NamePrefix = "agreed-";

    /// <summary>
    /// The member naming the pairing the record is under.
    /// </summary>
    internal const string PairingMember = "pairing";

    /// <summary>
    /// The member naming the mapped user the record is about.
    /// </summary>
    internal const string UserMember = "user";

    /// <summary>
    /// The member holding one entry per item.
    /// </summary>
    internal const string ItemsMember = "items";

    /// <summary>
    /// The member holding the point the peer last confirmed, absent where none has been.
    /// </summary>
    internal const string WatermarkMember = "watermark";

    /// <summary>
    /// The member of the watermark holding the point itself, as the far side wrote it.
    /// </summary>
    internal const string WatermarkPointMember = "point";

    /// <summary>
    /// The member of the watermark holding when this server confirmed the point.
    /// </summary>
    internal const string WatermarkConfirmedAtMember = "confirmedAt";

    private const string KindMember = "kind";
    private const string PlayedMember = "played";
    private const string PlayCountMember = "playCount";
    private const string PositionTicksMember = "positionTicks";
    private const string LastPlayedMember = "lastPlayed";
    private const string AgreedAtMember = "agreedAt";
    private const string EnvelopeVersionMember = "envelopeVersion";

    private readonly Dictionary<Guid, AgreedRecord> _byItem;

    private AgreedRecords(
        Guid pairingId,
        Guid mappedUserId,
        Dictionary<Guid, AgreedRecord> byItem,
        Watermark watermark)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        _byItem = byItem;
        Watermark = watermark;
    }

    /// <summary>
    /// Gets how many items one record may hold, which is #313.
    ///
    /// The shape above bounds this record by the number of matched items, and that is a bound
    /// against an evening of playback rather than against a peer. A peer offers changes and this
    /// side keys them on its own items, so a peer with a large library reaches one entry per item
    /// it can name, one exchange at a time, without breaking a single rule the wire carries. The
    /// number of matched items is also a number an operator never chose and cannot see, which is
    /// what makes it the wrong bound to be relying on: nothing about it says what this side has
    /// agreed to hold.
    ///
    /// <para>
    /// Twenty thousand is twice <c>RunCap.MaximumConfigurableChanges</c>, so two runs at the
    /// widest cap an operator can set fit under it whole and no ordinary run reaches it. It is
    /// deliberately reachable, because a bound nothing can reach bounds nothing:
    /// <c>docs/configuration.md</c> says what an operator does when it is reached, and the size
    /// of a full document is measured by <c>AgreedRecordsBoundTests</c> rather than asserted
    /// here.
    /// </para>
    /// </summary>
    public static int MaximumEntries => 20000;

    /// <summary>
    /// Gets the pairing these agreements are under.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the mapped user these agreements are about, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets how many items have been agreed.
    ///
    /// It is the number of matched items two servers have settled on, which is the bound #14's
    /// fourth condition names.
    /// </summary>
    public int Count => _byItem.Count;

    /// <summary>
    /// Gets the point up to which this pairing and this mapped user have agreed, which is #51.
    ///
    /// It is here rather than in a document of its own because the two are restored together or
    /// they are not restored at all. A store holding a watermark later than the agreements
    /// beside it would ask a peer for changes after a point whose items this record never
    /// received, and every item in between would be one neither side mentions again.
    /// </summary>
    public Watermark Watermark { get; }

    /// <summary>
    /// The record of a pairing and a mapped user that have agreed nothing yet.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <returns>A record holding no agreement.</returns>
    /// <exception cref="ArgumentException">Either identifier is empty.</exception>
    public static AgreedRecords NoneYet(Guid pairingId, Guid mappedUserId)
    {
        RefuseAnEmptyIdentifier(pairingId, nameof(pairingId));
        RefuseAnEmptyIdentifier(mappedUserId, nameof(mappedUserId));

        return new AgreedRecords(
            pairingId,
            mappedUserId,
            new Dictionary<Guid, AgreedRecord>(),
            Watermark.NoneYet);
    }

    /// <summary>
    /// What this record is called in the store.
    ///
    /// The store composes a path out of the name and refuses anything but lower case letters,
    /// digits and hyphens, which the two identifiers written without their own hyphens satisfy
    /// for every pairing and every user. So the name is derived from what the record is about
    /// rather than counted, and two pairings or two users never collide on one document.
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
    /// Reads one document in this plugin's store as an agreed record.
    ///
    /// Every entry is read or the document is refused. What that costs is one unreadable entry
    /// refusing a whole pairing's record, and what it buys is that an item is never silently
    /// unagreed: an entry dropped on the way in is a first exchange for that item on the way
    /// out, and a first exchange is the run that is allowed to change the most.
    ///
    /// <para>
    /// The count is the one thing not refused on. A document holding more than
    /// <see cref="MaximumEntries"/> is read as it stands, for the same reason the entries are:
    /// refusing it would unagree every item in it at once, which is the outcome the bound exists
    /// against, arrived at through the bound. What such a record does is stop taking items it
    /// does not already hold, at the next <see cref="Agreeing"/>, until it holds fewer than the
    /// bound again.
    /// </para>
    /// </summary>
    /// <param name="document">The document, already read at a version this code may read.</param>
    /// <returns>The record, or the reason the document is not one.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static AgreedRecordsReading Read(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryReadIdentifier(document.Fields, PairingMember, out var pairingId)
            || !TryReadIdentifier(document.Fields, UserMember, out var mappedUserId)
            || document.Fields[ItemsMember] is not JsonObject items
            || !TryReadWatermark(document.Fields, out var watermark))
        {
            return AgreedRecordsReading.NotAnAgreedRecord();
        }

        var byItem = new Dictionary<Guid, AgreedRecord>();

        foreach (var entry in items)
        {
            if (!Guid.TryParseExact(entry.Key, "n", out var itemId)
                || itemId == Guid.Empty
                || byItem.ContainsKey(itemId)
                || !TryReadAgreement(mappedUserId, itemId, entry.Value, out var agreement))
            {
                return AgreedRecordsReading.NotAnAgreedRecord();
            }

            byItem[itemId] = agreement!;
        }

        return AgreedRecordsReading.Readable(
            new AgreedRecords(pairingId, mappedUserId, byItem, watermark!));
    }

    /// <summary>
    /// What was agreed about one item, or null where nothing has been.
    ///
    /// Null is a defined state and it is #14's fifth condition: an item with no agreed record is
    /// a first exchange, and a first exchange merges by the conflict table like every later one,
    /// which is the answer taken on #37. It is deliberately not an agreement holding a
    /// never-watched state. The two produce the same outstanding changes and say different
    /// things about intent, and which of the two a deliberate unplayed sits behind is what #34
    /// turns on.
    /// </summary>
    /// <param name="itemId">The item, as this server names it.</param>
    /// <returns>The agreement, or null.</returns>
    public AgreedRecord? For(Guid itemId) =>
        _byItem.TryGetValue(itemId, out var agreement) ? agreement : null;

    /// <summary>
    /// This record with one agreement in it, replacing whatever was agreed about that item.
    ///
    /// This is the route for a caller that cannot reach <see cref="MaximumEntries"/> or has
    /// already been told there is room. At the bound it throws rather than answering, because the
    /// two things it could do instead are the two this bound exists against: returning the record
    /// unchanged loses the agreement in silence, and dropping an older entry to make room
    /// unagrees an item two servers had settled. <see cref="Agreeing"/> is the route that answers.
    /// </summary>
    /// <param name="agreement">What was agreed.</param>
    /// <returns>A record carrying it.</returns>
    /// <exception cref="ArgumentNullException">The agreement is null.</exception>
    /// <exception cref="ArgumentException">
    /// The agreement is about a mapped user this record is not about. A record is one user's, so
    /// an agreement about another one has no entry here it could replace, and it would be
    /// readable afterwards under a user it was never about.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The record holds <see cref="MaximumEntries"/> items already and the agreement is about an
    /// item it does not hold.
    /// </exception>
    public AgreedRecords With(AgreedRecord agreement)
    {
        var admission = Agreeing(agreement);

        if (admission.IsRefused)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"This record already holds {admission.Held} items, which is the bound of {MaximumEntries}, and the agreement is about an item it does not hold. Ask Agreeing rather than this, because the alternatives here are losing this agreement or unagreeing an item two servers had settled."));
        }

        return admission.Records!;
    }

    /// <summary>
    /// This record with one agreement offered to it, answering whether it took it.
    ///
    /// This is the route a caller that can reach the bound takes, and #313 is why there is one.
    /// An agreement about an item this record already holds replaces that entry and is never
    /// refused, whatever the record holds, because a replacement cannot grow it: a record at the
    /// bound goes on agreeing every item it has already agreed, and only an item it has never
    /// agreed is turned away. That is the difference between a record that has stopped taking new
    /// work and one that has frozen.
    /// </summary>
    /// <param name="agreement">What was agreed.</param>
    /// <returns>The record carrying it, or the reason it does not.</returns>
    /// <exception cref="ArgumentNullException">The agreement is null.</exception>
    /// <exception cref="ArgumentException">
    /// The agreement is about a mapped user this record is not about. A record is one user's, so
    /// an agreement about another one has no entry here it could replace, and it would be
    /// readable afterwards under a user it was never about.
    /// </exception>
    public AgreementAdmission Agreeing(AgreedRecord agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);

        if (agreement.Subject.MappedUserId != MappedUserId)
        {
            throw new ArgumentException(
                "The agreement is about another mapped user, and this record is one user's.",
                nameof(agreement));
        }

        if (_byItem.Count >= MaximumEntries
            && !_byItem.ContainsKey(agreement.Subject.ItemId))
        {
            return AgreementAdmission.AtTheBound(_byItem.Count);
        }

        var byItem = new Dictionary<Guid, AgreedRecord>(_byItem)
        {
            [agreement.Subject.ItemId] = agreement,
        };

        return AgreementAdmission.Agreed(
            new AgreedRecords(PairingId, MappedUserId, byItem, Watermark));
    }

    /// <summary>
    /// This record standing at one watermark, replacing whatever it stood at before.
    ///
    /// The record and the point are written in one document and therefore in one write. The
    /// order the transfer document fixes, the agreed record first and the watermark second, is
    /// about what a caller computes before it writes and not about two writes: a record whose
    /// watermark landed and whose agreements did not would offer a peer a point it never
    /// received the items for.
    /// </summary>
    /// <param name="watermark">The point the peer last confirmed.</param>
    /// <returns>A record standing at it.</returns>
    /// <exception cref="ArgumentNullException">The watermark is null.</exception>
    public AgreedRecords At(Watermark watermark)
    {
        ArgumentNullException.ThrowIfNull(watermark);

        return new AgreedRecords(
            PairingId,
            MappedUserId,
            new Dictionary<Guid, AgreedRecord>(_byItem),
            watermark);
    }

    /// <summary>
    /// This record as a document the store can write.
    ///
    /// The entries are written in the order of their item, so two writes of one record produce
    /// the same bytes and a difference between two documents is a difference somebody made
    /// rather than the order a dictionary happened to be walked in.
    /// </summary>
    /// <returns>The document.</returns>
    public StoredDocument ToDocument()
    {
        var items = new JsonObject();

        foreach (var itemId in _byItem.Keys.OrderBy(id => id))
        {
            items[itemId.ToString("n", CultureInfo.InvariantCulture)] = Written(_byItem[itemId]);
        }

        var fields = new JsonObject
        {
            [PairingMember] =
                JsonValue.Create(PairingId.ToString("n", CultureInfo.InvariantCulture)),
            [UserMember] =
                JsonValue.Create(MappedUserId.ToString("n", CultureInfo.InvariantCulture)),
            [ItemsMember] = items,
        };

        if (!Watermark.IsNoneYet)
        {
            fields[WatermarkMember] = new JsonObject
            {
                [WatermarkPointMember] = JsonValue.Create(Watermark.Point),
                [WatermarkConfirmedAtMember] = JsonValue.Create(
                    Watermark.ConfirmedAt.ToString("o", CultureInfo.InvariantCulture)),
            };
        }

        return StoredDocument.At(DocumentVersions.Current, fields);
    }

    private static void RefuseAnEmptyIdentifier(Guid identifier, string name)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "An agreed record is about one pairing and one mapped user, and this one is empty.",
                name);
        }
    }

    private static JsonObject Written(AgreedRecord agreement) => new JsonObject
    {
        [KindMember] = JsonValue.Create(agreement.Subject.Kind.ToString()),
        [PlayedMember] = JsonValue.Create(agreement.Agreed.Played),
        [PlayCountMember] = JsonValue.Create(agreement.Agreed.PlayCount),
        [PositionTicksMember] = JsonValue.Create(agreement.Agreed.PlaybackPositionTicks),
        [LastPlayedMember] = agreement.Agreed.LastPlayedDate is null
            ? null
            : JsonValue.Create(
                agreement.Agreed.LastPlayedDate.Value.ToString("o", CultureInfo.InvariantCulture)),
        [AgreedAtMember] =
            JsonValue.Create(agreement.AgreedAt.ToString("o", CultureInfo.InvariantCulture)),
        [EnvelopeVersionMember] = JsonValue.Create(agreement.EnvelopeVersion),
    };

    /// <summary>
    /// Reads the point the peer last confirmed out of the document.
    ///
    /// An absent member is a record that has confirmed nothing, which is a state rather than a
    /// damaged document: it is what a pairing that has never exchanged writes, and it is what
    /// every record written before this member existed holds. A member that is present and is
    /// not a watermark refuses the whole document, by the same rule an unreadable entry does,
    /// because the alternative is a record that reads as having agreed nothing about a point and
    /// silently asks a peer for a library it has already synced.
    /// </summary>
    /// <param name="fields">The members beside the version.</param>
    /// <param name="watermark">The point, where the document carries one.</param>
    /// <returns>Whether the document is readable this far.</returns>
    private static bool TryReadWatermark(JsonObject fields, out Watermark? watermark)
    {
        watermark = Watermark.NoneYet;

        if (!fields.TryGetPropertyValue(WatermarkMember, out var watermarkNode))
        {
            return true;
        }

        if (watermarkNode is not JsonObject members
            || members[WatermarkPointMember] is not JsonValue pointValue
            || !pointValue.TryGetValue<string>(out var point)
            || !TryReadMoment(members, WatermarkConfirmedAtMember, out var confirmedAt))
        {
            watermark = null;
            return false;
        }

        var reading = Watermark.Confirmed(point, confirmedAt);

        if (reading.IsRefused)
        {
            watermark = null;
            return false;
        }

        watermark = reading.Mark;

        return true;
    }

    private static bool TryReadIdentifier(JsonObject fields, string member, out Guid identifier)
    {
        identifier = Guid.Empty;

        return fields[member] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Guid.TryParseExact(text, "n", out identifier)
            && identifier != Guid.Empty;
    }

    /// <summary>
    /// Reads one entry of the document as an agreement.
    ///
    /// The kind goes back through <see cref="TransferSubject.From"/> rather than into a field of
    /// its own, so a document naming an aggregate is refused on the way in by the same rule that
    /// refuses one on the way out. A record that could be read holding a series would be a way
    /// around that refusal for anything that had once been written by hand.
    /// </summary>
    /// <param name="mappedUserId">The mapped user the document is about.</param>
    /// <param name="itemId">The item the entry is keyed on.</param>
    /// <param name="node">The entry.</param>
    /// <param name="agreement">What it holds, where it is an agreement.</param>
    /// <returns>Whether the entry is an agreement.</returns>
    private static bool TryReadAgreement(
        Guid mappedUserId,
        Guid itemId,
        JsonNode? node,
        out AgreedRecord? agreement)
    {
        agreement = null;

        if (node is not JsonObject members
            || !TryReadKind(members, out var kind)
            || members[PlayedMember] is not JsonValue playedValue
            || !playedValue.TryGetValue<bool>(out var played)
            || !TryReadWholeNumber(members, PlayCountMember, out var playCount)
            || playCount > int.MaxValue
            || !TryReadWholeNumber(members, PositionTicksMember, out var positionTicks)
            || !TryReadMoment(members, AgreedAtMember, out var agreedAt)
            || !TryReadLastPlayed(members, out var lastPlayed)
            || !TryReadWholeNumber(members, EnvelopeVersionMember, out var envelopeVersion)
            || envelopeVersion < 1
            || envelopeVersion > int.MaxValue)
        {
            return false;
        }

        var subject = TransferSubject.From(mappedUserId, itemId, kind);

        if (!subject.IsSubject)
        {
            return false;
        }

        agreement = new AgreedRecord(
            subject.Value!,
            new SyncedState(played, (int)playCount, positionTicks, lastPlayed),
            agreedAt,
            (int)envelopeVersion);

        return true;
    }

    /// <summary>
    /// Reads the kind out of an entry, by the name the server's own enumeration gives it.
    ///
    /// The name has to come back out of the enumeration unchanged, which is what refuses a
    /// number written where a name belongs. A document carrying a kind by number would keep
    /// meaning whatever that position happened to be on the day it was written, and the
    /// enumeration is the server's rather than this plugin's.
    /// </summary>
    /// <param name="members">The entry.</param>
    /// <param name="kind">The kind, where the entry names one.</param>
    /// <returns>Whether the entry names a kind.</returns>
    private static bool TryReadKind(JsonObject members, out BaseItemKind kind)
    {
        kind = default;

        return members[KindMember] is JsonValue value
            && value.TryGetValue<string>(out var name)
            && Enum.TryParse(name, out kind)
            && string.Equals(kind.ToString(), name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads one whole number that is not below zero out of an entry.
    ///
    /// Both widths are tried, and that is not defensive coding. A member that was parsed out of
    /// bytes converts between them and a member assembled in memory does not, so a document this
    /// record has just built and one the store has just read are not the same subject to a reader
    /// that asks for one width only. The document written to disk is the same either way, which
    /// is what makes the difference invisible until a caller reads a document it did not fetch.
    /// </summary>
    /// <param name="members">The entry.</param>
    /// <param name="member">The member to read.</param>
    /// <param name="number">The number.</param>
    /// <returns>Whether the member is a whole number that is not below zero.</returns>
    private static bool TryReadWholeNumber(JsonObject members, string member, out long number)
    {
        number = 0;

        if (members[member] is not JsonValue value)
        {
            return false;
        }

        if (!value.TryGetValue<long>(out number))
        {
            if (!value.TryGetValue<int>(out var narrower))
            {
                return false;
            }

            number = narrower;
        }

        return number >= 0;
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

    /// <summary>
    /// Reads the moment the person last watched the work, which is the one member of an
    /// agreement that may be null.
    ///
    /// A missing member and a member holding null are different documents and only the second is
    /// an agreement. What the first one is, is a document somebody assembled without that field,
    /// and reading it as never-watched would invent an agreement about a field nobody agreed.
    /// </summary>
    /// <param name="members">The entry.</param>
    /// <param name="lastPlayed">The moment, or null where there is none.</param>
    /// <returns>Whether the entry carries the member at all.</returns>
    private static bool TryReadLastPlayed(JsonObject members, out DateTime? lastPlayed)
    {
        lastPlayed = null;

        if (!members.TryGetPropertyValue(LastPlayedMember, out var member))
        {
            return false;
        }

        if (member is null)
        {
            return true;
        }

        if (member is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || !DateTime.TryParseExact(
                text,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var moment))
        {
            return false;
        }

        lastPlayed = moment;
        return true;
    }
}

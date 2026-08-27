using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Matching;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// Every item that did not match under one pairing for one mapped user, as a document the store
/// keeps.
///
/// #26's second condition asks for a record holding the item, its class, the reason and when it
/// was last attempted, and says it is the source for the count on the status page. A count is
/// asked for the day after the sweep that produced it rather than during it, so the record is a
/// document rather than something a run holds.
///
/// <para>
/// The entries are keyed on the item, which is #26's third condition and is the whole of the
/// bound that condition is about. An item that will never match is attempted on every pass, and
/// keyed on the item those passes replace one entry; keyed on anything else they would add one
/// per attempt, so a library of unmatchable items would grow this document forever while every
/// small fixture stayed green. What it also buys is the fourth condition's reading: the moment
/// moving is what says a pass has been past an item since.
/// </para>
///
/// <para>
/// That is not the whole bound, because a library is not small. <see cref="MaximumEntries"/> caps
/// what one document holds, and when it binds the entry dropped is the one attempted longest ago,
/// because the recently attempted items are the ones an operator is working through.
/// </para>
///
/// <para>
/// WHAT THE CAP COSTS IS THAT THIS RECORD IS A SAMPLE AND NOT A CENSUS, and #26's second
/// condition names it as the source for a count. A page reading <see cref="Count"/> off a capped
/// record on a library of a hundred thousand unmatchable items would tell an operator there are a
/// thousand, which is the number improving rather than the library being wrong. Nothing here can
/// repair that: a census is a number only the pass that walked the library knows, so it is
/// reported by that pass rather than counted off this document. The pass is #55 and the page is
/// #62, and this paragraph is the constraint both of them inherit rather than a gap in the
/// record.
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
/// What this does not decide: nothing writes one yet. No run walks a library and hands the
/// matcher's refusals to a record, so the count has no producer and the fourth condition has no
/// pass to be true of. #26's fifth condition, that no code path turns an unmatched item into a
/// match by relaxing a rule, is about the matcher rather than about this record and is not
/// touched here.
/// </para>
/// </summary>
public sealed class UnmatchedRecords
{
    /// <summary>
    /// The prefix of the document's name in the store.
    /// </summary>
    internal const string NamePrefix = "unmatched-";

    /// <summary>
    /// The member naming the pairing the items were being matched for.
    /// </summary>
    internal const string PairingMember = "pairing";

    /// <summary>
    /// The member naming the mapped user the pass was for.
    /// </summary>
    internal const string UserMember = "user";

    /// <summary>
    /// The member holding the entries, keyed on the item.
    /// </summary>
    internal const string ItemsMember = "items";

    /// <summary>
    /// The member of one entry naming the item's class.
    /// </summary>
    internal const string KindMember = "kind";

    /// <summary>
    /// The member of one entry naming why no key could be derived.
    /// </summary>
    internal const string RefusalMember = "refusal";

    /// <summary>
    /// The member of one entry naming what the lookup answered.
    /// </summary>
    internal const string AnswerMember = "answer";

    /// <summary>
    /// The member of one entry holding when matching was last attempted.
    /// </summary>
    internal const string LastAttemptedMember = "lastAttempted";

    private readonly IReadOnlyDictionary<Guid, UnmatchedRecord> _byItem;

    private UnmatchedRecords(
        Guid pairingId,
        Guid mappedUserId,
        IReadOnlyDictionary<Guid, UnmatchedRecord> byItem)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        _byItem = byItem;
    }

    /// <summary>
    /// Gets how many unmatched items one document holds before the one attempted longest ago is
    /// dropped.
    ///
    /// It is a number rather than a setting for now, and #58 is where it becomes one. A thousand
    /// is a document somebody can read and a list somebody can work through, and it is far above
    /// what a library whose metadata is in order produces. A library that reaches it has a
    /// systematic problem rather than a list of items to repair, and the answer to that is the
    /// reasons rather than the rows.
    /// </summary>
    public static int MaximumEntries => 1000;

    /// <summary>
    /// Gets the pairing these items were being matched for.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the mapped user the pass was for, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets how many unmatched items this document holds, which is at most
    /// <see cref="MaximumEntries"/> and is not the number of unmatched items in the library.
    /// </summary>
    public int Count => _byItem.Count;

    /// <summary>
    /// Gets the entries, in the order of their item, so a reader walks them the same way twice.
    /// </summary>
    public IReadOnlyList<UnmatchedRecord> All =>
        _byItem.Keys.OrderBy(id => id).Select(id => _byItem[id]).ToList();

    /// <summary>
    /// The record of a pairing and a mapped user with nothing recorded as unmatched.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <returns>A record holding no entry.</returns>
    /// <exception cref="ArgumentException">Either identifier is empty.</exception>
    public static UnmatchedRecords NoneYet(Guid pairingId, Guid mappedUserId)
    {
        RefuseAnEmptyIdentifier(pairingId, nameof(pairingId));
        RefuseAnEmptyIdentifier(mappedUserId, nameof(mappedUserId));

        return new UnmatchedRecords(
            pairingId,
            mappedUserId,
            new Dictionary<Guid, UnmatchedRecord>());
    }

    /// <summary>
    /// What this record is called in the store, derived from what it is about rather than
    /// counted, the way its siblings are.
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
    /// Reads one document in this plugin's store as the unmatched items recorded for a pairing
    /// and a mapped user.
    ///
    /// Every entry is read or the document is refused. A document holding more than
    /// <see cref="MaximumEntries"/> is read as it stands and trimmed at the next
    /// <see cref="With"/>, because refusing it would turn an operator's whole list unreadable on
    /// the day somebody lowers the cap.
    /// </summary>
    /// <param name="document">The document, already read at a version this code may read.</param>
    /// <returns>The records, or the reason the document is not one.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static UnmatchedRecordsReading Read(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryReadIdentifier(document.Fields, PairingMember, out var pairingId)
            || !TryReadIdentifier(document.Fields, UserMember, out var mappedUserId)
            || document.Fields[ItemsMember] is not JsonObject items)
        {
            return UnmatchedRecordsReading.NotARecordOfUnmatchedItems();
        }

        var byItem = new Dictionary<Guid, UnmatchedRecord>();

        foreach (var entry in items)
        {
            if (!Guid.TryParseExact(entry.Key, "n", out var itemId)
                || itemId == Guid.Empty
                || byItem.ContainsKey(itemId)
                || !TryReadUnmatched(itemId, entry.Value, out var unmatched))
            {
                return UnmatchedRecordsReading.NotARecordOfUnmatchedItems();
            }

            byItem[itemId] = unmatched!;
        }

        return UnmatchedRecordsReading.Readable(
            new UnmatchedRecords(pairingId, mappedUserId, byItem));
    }

    /// <summary>
    /// What was recorded about one item, or null where nothing was.
    ///
    /// Null is the item matching and the item never having been attempted at once, and this
    /// record cannot tell them apart because neither leaves an entry. What separates them is the
    /// pass that walked the library, which is #55.
    /// </summary>
    /// <param name="itemId">The item, as this server names it.</param>
    /// <returns>The entry, or null.</returns>
    public UnmatchedRecord? For(Guid itemId) =>
        _byItem.TryGetValue(itemId, out var unmatched) ? unmatched : null;

    /// <summary>
    /// This record with one item recorded as unmatched, replacing whatever was recorded about
    /// that item, and dropping the entry attempted longest ago where that takes the document past
    /// <see cref="MaximumEntries"/>.
    ///
    /// Replacing rather than adding is what keeps a library of items that will never match from
    /// growing this document on every pass, and it is why the entry carries the last attempt
    /// rather than the first.
    /// </summary>
    /// <param name="unmatched">The item and why it did not match.</param>
    /// <returns>A record carrying it.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public UnmatchedRecords With(UnmatchedRecord unmatched)
    {
        ArgumentNullException.ThrowIfNull(unmatched);

        var byItem = new Dictionary<Guid, UnmatchedRecord>(_byItem)
        {
            [unmatched.ItemId] = unmatched,
        };

        while (byItem.Count > MaximumEntries)
        {
            var oldest = byItem
                .OrderBy(entry => entry.Value.LastAttemptedAt)
                .ThenBy(entry => entry.Key)
                .First()
                .Key;

            byItem.Remove(oldest);
        }

        return new UnmatchedRecords(PairingId, MappedUserId, byItem);
    }

    /// <summary>
    /// This record with one item no longer recorded as unmatched.
    ///
    /// It is what #26's fourth condition needs on the day an item that had no identifier acquires
    /// one: the pass attempts it again, it matches, and the entry has to go rather than being left
    /// as a row an operator keeps trying to repair. Removing an item that is not recorded answers
    /// with a record holding the same entries, because a pass that matched everything would
    /// otherwise have to ask before it could tell.
    /// </summary>
    /// <param name="itemId">The item, as this server names it.</param>
    /// <returns>A record without it.</returns>
    public UnmatchedRecords Without(Guid itemId)
    {
        if (!_byItem.ContainsKey(itemId))
        {
            return this;
        }

        var byItem = new Dictionary<Guid, UnmatchedRecord>(_byItem);

        byItem.Remove(itemId);

        return new UnmatchedRecords(PairingId, MappedUserId, byItem);
    }

    /// <summary>
    /// This record as a document the store can write.
    ///
    /// The entries are written in the order of their item, so two writes of one record produce
    /// the same bytes and a difference between two documents is a difference somebody made rather
    /// than the order a dictionary happened to be walked in.
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

        return StoredDocument.At(DocumentVersions.Current, fields);
    }

    private static void RefuseAnEmptyIdentifier(Guid identifier, string name)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "A record of unmatched items is about one pairing and one mapped user, and this one is empty.",
                name);
        }
    }

    private static JsonObject Written(UnmatchedRecord unmatched) => new JsonObject
    {
        [KindMember] = JsonValue.Create(unmatched.Kind.ToString()),
        [RefusalMember] = JsonValue.Create(unmatched.Refusal.ToString()),
        [AnswerMember] = unmatched.Answer is null
            ? null
            : JsonValue.Create(unmatched.Answer.Value.ToString()),
        [LastAttemptedMember] =
            JsonValue.Create(unmatched.LastAttemptedAt.ToString("o", CultureInfo.InvariantCulture)),
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
    /// Reads one entry of the document as an unmatched item.
    ///
    /// It goes back through the record's own constructor rather than into fields of its own, so a
    /// document carrying a refusal beside an answer, carrying neither, or naming a match is
    /// refused on the way in by the same rules that refuse it on the way out. A reader that let
    /// any of the three through would be the way around them for every document an operator can
    /// edit, and the last of them would put an item that matched into a count of what did not.
    /// </summary>
    /// <param name="itemId">The item the entry is keyed on.</param>
    /// <param name="node">The entry.</param>
    /// <param name="unmatched">What it holds, where it is one.</param>
    /// <returns>Whether the entry is an unmatched item.</returns>
    private static bool TryReadUnmatched(
        Guid itemId,
        JsonNode? node,
        out UnmatchedRecord? unmatched)
    {
        unmatched = null;

        if (node is not JsonObject members
            || !TryReadName<BaseItemKind>(members, KindMember, out var kind)
            || !TryReadName<MatchKeyRefusal>(members, RefusalMember, out var refusal)
            || !TryReadAnswer(members, out var answer)
            || !TryReadMoment(members, LastAttemptedMember, out var lastAttemptedAt))
        {
            return false;
        }

        try
        {
            unmatched = new UnmatchedRecord(itemId, kind, refusal, answer, lastAttemptedAt);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the answer member, which is a name or null.
    ///
    /// A missing member and a member holding null are different documents and only the second is
    /// an entry. The first is a document somebody assembled without the member, and reading it as
    /// "no lookup happened" would decide which of the two halves of the reason this entry carries
    /// on behalf of whoever wrote it.
    /// </summary>
    /// <param name="members">The entry.</param>
    /// <param name="answer">The answer, or null where the entry carries none.</param>
    /// <returns>Whether the entry carries the member at all.</returns>
    private static bool TryReadAnswer(JsonObject members, out MatchAnswer? answer)
    {
        answer = null;

        if (!members.TryGetPropertyValue(AnswerMember, out var node))
        {
            return false;
        }

        if (node is null)
        {
            return true;
        }

        if (node is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || !Enum.TryParse<MatchAnswer>(text, out var named)
            || !string.Equals(named.ToString(), text, StringComparison.Ordinal))
        {
            return false;
        }

        answer = named;
        return true;
    }

    /// <summary>
    /// Reads one member as the name of an enumeration member.
    ///
    /// The name has to come back out of the enumeration unchanged, which is what refuses a number
    /// written where a name belongs. A document carrying a reason or a class by number would keep
    /// meaning whatever that position happened to be on the day it was written, and one of the
    /// two enumerations is the server's rather than this plugin's.
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

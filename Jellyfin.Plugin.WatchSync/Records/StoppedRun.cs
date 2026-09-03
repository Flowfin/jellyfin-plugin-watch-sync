using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Transfer;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What a run the cap stopped was about to do, written down for an operator to approve, which is
/// #38's second condition.
///
/// The cap in <see cref="RunCap"/> is the only rule in this plan that limits the damage when
/// another rule is wrong, and a run it stops has already been told it may not write. What is
/// left for the run to do is say what it would have written, so that an operator can read the
/// plan on the status page and approve it, and this is that plan. It is the whole plan rather
/// than a count: an operator deciding whether eighty changes to somebody's history are a
/// legitimate first reconciliation or a wrong mapping needs the items, and a record holding a
/// number would send them to guess.
///
/// <para>
/// One document per pairing and per mapped user, and a later stop replaces an earlier one. A
/// plan is a statement about a moment, and two plans for one person would be two moments an
/// operator has to choose between with nothing saying which is current. The one that stands is
/// the latest, and a plan an operator did not get to before the next run stopped is superseded
/// rather than queued.
/// </para>
///
/// <para>
/// Every item carries what this server held when the run stopped, and that is the half an
/// approval turns on. #38's third condition asks that an approved plan apply exactly what it
/// recorded, including nothing that changed in the meantime without being noticed, and the
/// approval notices by comparing what is held now against what is recorded here. The comparison
/// is the whole of what makes a plan applied days later safe, and it is why the plan is written
/// with both halves rather than with the decided state alone.
/// </para>
///
/// <para>
/// It is immutable and it is a record of one moment, so there is no <c>With</c>: nothing is
/// added to a stopped run after it stopped. It is a document the store keeps, because the
/// operator who approves it is usually not at the machine when it stops.
/// </para>
/// </summary>
public sealed class StoppedRun
{
    /// <summary>
    /// The prefix of the document's name in the store.
    /// </summary>
    internal const string NamePrefix = "stopped-";

    /// <summary>
    /// The member naming the pairing the run was for.
    /// </summary>
    internal const string PairingMember = "pairing";

    /// <summary>
    /// The member naming the mapped user the run was about.
    /// </summary>
    internal const string UserMember = "user";

    /// <summary>
    /// The member naming which bound stopped the run.
    /// </summary>
    internal const string AnswerMember = "answer";

    /// <summary>
    /// The member holding how many changes the run was about to make.
    /// </summary>
    internal const string ChangesMember = "changes";

    /// <summary>
    /// The member holding how many changes the crossed bound allowed.
    /// </summary>
    internal const string AllowedMember = "allowed";

    /// <summary>
    /// The member holding how many items this person had matched when the run was judged.
    /// </summary>
    internal const string MatchedMember = "matched";

    /// <summary>
    /// The member naming the peer user the decided values came from, as the peer names them.
    /// </summary>
    internal const string PeerUserMember = "peerUser";

    /// <summary>
    /// The member holding the version of the envelope the changes arrived under.
    /// </summary>
    internal const string EnvelopeVersionMember = "envelopeVersion";

    /// <summary>
    /// The member holding the moment the run stopped.
    /// </summary>
    internal const string StoppedAtMember = "stoppedAt";

    /// <summary>
    /// The member holding the items, in the order the run would have written them.
    /// </summary>
    internal const string ItemsMember = "items";

    private const string ItemMember = "item";
    private const string KindMember = "kind";
    private const string DecidedMember = "decided";
    private const string HeldMember = "held";
    private const string HeldWasReadMember = "heldWasRead";
    private const string PlayedMember = "played";
    private const string PlayCountMember = "playCount";
    private const string PositionTicksMember = "positionTicks";
    private const string LastPlayedMember = "lastPlayed";

    private readonly IReadOnlyList<StoppedRunItem> _items;

    private StoppedRun(
        Guid pairingId,
        Guid mappedUserId,
        Guid peerUserId,
        int envelopeVersion,
        RunCapAnswer answer,
        int changes,
        int allowed,
        int matched,
        DateTimeOffset stoppedAt,
        IReadOnlyList<StoppedRunItem> items)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        PeerUserId = peerUserId;
        EnvelopeVersion = envelopeVersion;
        Answer = answer;
        Changes = changes;
        Allowed = allowed;
        Matched = matched;
        StoppedAt = stoppedAt;
        _items = items;
    }

    /// <summary>
    /// Gets the pairing the run was for.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the mapped user the run was about, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets the peer user the decided values came from, as the peer names them.
    ///
    /// It is in the plan rather than handed to the approval, because it is part of what the run
    /// would have done: every value the walk writes is stamped with the peer user it came from,
    /// so that a revoked pairing can be undone, and an approval handed a different one would
    /// file the writes under an account the values never came from.
    /// </summary>
    public Guid PeerUserId { get; }

    /// <summary>
    /// Gets the version of the envelope the changes arrived under, which every agreement the
    /// approval records carries, for the same reason the peer user is here.
    /// </summary>
    public int EnvelopeVersion { get; }

    /// <summary>
    /// Gets which bound stopped the run. It is never <see cref="RunCapAnswer.Within"/>, because
    /// a run within the cap is not stopped and leaves no plan.
    /// </summary>
    public RunCapAnswer Answer { get; }

    /// <summary>
    /// Gets how many changes the run was about to make, which is the number of items here.
    /// </summary>
    public int Changes { get; }

    /// <summary>
    /// Gets how many changes the bound that was crossed allowed.
    /// </summary>
    public int Allowed { get; }

    /// <summary>
    /// Gets how many items this person had matched when the run was judged, which is what the
    /// share was taken over.
    /// </summary>
    public int Matched { get; }

    /// <summary>
    /// Gets the moment the run stopped, by this server's clock.
    /// </summary>
    public DateTimeOffset StoppedAt { get; }

    /// <summary>
    /// Gets the items the run was about to write, in the order it would have written them.
    /// </summary>
    public IReadOnlyList<StoppedRunItem> Items => _items;

    /// <summary>
    /// What this record is called in the store.
    ///
    /// Derived from what the record is about rather than counted, the way every other kind's
    /// is, so two pairings or two users never collide on one document and a walk over the store
    /// can say which person a plan is about without opening it.
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
    /// The record of one run the cap stopped.
    /// </summary>
    /// <param name="pairingId">The pairing the run was for.</param>
    /// <param name="mappedUserId">The mapped user the run was about.</param>
    /// <param name="peerUserId">The peer user the decided values came from, as the peer names them.</param>
    /// <param name="envelopeVersion">The version of the envelope the changes arrived under.</param>
    /// <param name="verdict">What the cap answered, which stopped the run.</param>
    /// <param name="matched">How many items this person had matched when the run was judged.</param>
    /// <param name="items">What the run was about to write, in order.</param>
    /// <param name="stoppedAt">The moment the run stopped, by this server's clock.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentNullException">The verdict or the items are null.</exception>
    /// <exception cref="ArgumentException">
    /// Any identifier is empty, the verdict is one that did not stop the run, an item is
    /// about another mapped user, or the number of items is not the number of changes the
    /// verdict was about. The last is refused because the plan is the run: a record saying
    /// eighty changes were stopped and listing seventy of them would be approved as eighty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The matched count is below zero, or the envelope version is not a whole number above
    /// zero.
    /// </exception>
    public static StoppedRun Of(
        Guid pairingId,
        Guid mappedUserId,
        Guid peerUserId,
        int envelopeVersion,
        RunCap verdict,
        int matched,
        IReadOnlyList<StoppedRunItem> items,
        DateTimeOffset stoppedAt)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(matched);
        ArgumentOutOfRangeException.ThrowIfLessThan(envelopeVersion, 1);
        RefuseAnEmptyIdentifier(pairingId, nameof(pairingId));
        RefuseAnEmptyIdentifier(mappedUserId, nameof(mappedUserId));
        RefuseAnEmptyIdentifier(peerUserId, nameof(peerUserId));

        if (verdict.Answer == RunCapAnswer.Within)
        {
            throw new ArgumentException(
                "The cap answered that the run may proceed, so nothing stopped it and there is no plan to record.",
                nameof(verdict));
        }

        if (items.Count != verdict.Changes)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cap judged {verdict.Changes} changes and the plan lists {items.Count} items, so the plan is not the run that was stopped."),
                nameof(items));
        }

        var copied = new List<StoppedRunItem>(items.Count);

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));

            if (item.Subject.MappedUserId != mappedUserId)
            {
                throw new ArgumentException(
                    "An item is about another mapped user, and a stopped run is one user's.",
                    nameof(items));
            }

            copied.Add(item);
        }

        return new StoppedRun(
            pairingId,
            mappedUserId,
            peerUserId,
            envelopeVersion,
            verdict.Answer,
            verdict.Changes,
            verdict.Allowed,
            matched,
            stoppedAt,
            copied);
    }

    /// <summary>
    /// Reads one document in this plugin's store as a stopped run.
    ///
    /// Every item is read or the document is refused. A plan that read the items it could and
    /// dropped the rest would be approved as the whole, and the items it dropped would be
    /// written by the next run that stops and is approved on the strength of the first.
    /// </summary>
    /// <param name="document">The document, already read at a version this code may read.</param>
    /// <returns>The run, or the reason the document is not one.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static StoppedRunReading Read(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryReadIdentifier(document.Fields, PairingMember, out var pairingId)
            || !TryReadIdentifier(document.Fields, UserMember, out var mappedUserId)
            || !TryReadIdentifier(document.Fields, PeerUserMember, out var peerUserId)
            || !TryReadWholeNumber(document.Fields, EnvelopeVersionMember, out var envelopeVersion)
            || envelopeVersion < 1
            || envelopeVersion > int.MaxValue
            || !TryReadAnswer(document.Fields, out var answer)
            || !TryReadWholeNumber(document.Fields, ChangesMember, out var changes)
            || changes > int.MaxValue
            || !TryReadWholeNumber(document.Fields, AllowedMember, out var allowed)
            || allowed > int.MaxValue
            || !TryReadWholeNumber(document.Fields, MatchedMember, out var matched)
            || matched > int.MaxValue
            || !TryReadMoment(document.Fields, StoppedAtMember, out var stoppedAt)
            || document.Fields[ItemsMember] is not JsonArray entries
            || entries.Count != changes)
        {
            return StoppedRunReading.NotAStoppedRun();
        }

        var items = new List<StoppedRunItem>(entries.Count);

        foreach (var entry in entries)
        {
            if (!TryReadItem(mappedUserId, entry, out var item))
            {
                return StoppedRunReading.NotAStoppedRun();
            }

            items.Add(item!);
        }

        return StoppedRunReading.Readable(new StoppedRun(
            pairingId,
            mappedUserId,
            peerUserId,
            (int)envelopeVersion,
            answer,
            (int)changes,
            (int)allowed,
            (int)matched,
            stoppedAt,
            items));
    }

    /// <summary>
    /// This record as a document the store can write.
    /// </summary>
    /// <returns>The document.</returns>
    public StoredDocument ToDocument()
    {
        var items = new JsonArray();

        foreach (var item in _items)
        {
            items.Add(Written(item));
        }

        var fields = new JsonObject
        {
            [PairingMember] =
                JsonValue.Create(PairingId.ToString("n", CultureInfo.InvariantCulture)),
            [UserMember] =
                JsonValue.Create(MappedUserId.ToString("n", CultureInfo.InvariantCulture)),
            [PeerUserMember] =
                JsonValue.Create(PeerUserId.ToString("n", CultureInfo.InvariantCulture)),
            [EnvelopeVersionMember] = JsonValue.Create(EnvelopeVersion),
            [AnswerMember] = JsonValue.Create(Answer.ToString()),
            [ChangesMember] = JsonValue.Create(Changes),
            [AllowedMember] = JsonValue.Create(Allowed),
            [MatchedMember] = JsonValue.Create(Matched),
            [StoppedAtMember] =
                JsonValue.Create(StoppedAt.ToString("o", CultureInfo.InvariantCulture)),
            [ItemsMember] = items,
        };

        return StoredDocument.At(DocumentVersions.Current, fields);
    }

    private static void RefuseAnEmptyIdentifier(Guid identifier, string name)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "A stopped run is about one pairing and one mapped user, and this one is empty.",
                name);
        }
    }

    private static JsonObject Written(StoppedRunItem item)
    {
        var entry = new JsonObject
        {
            [ItemMember] =
                JsonValue.Create(item.Subject.ItemId.ToString("n", CultureInfo.InvariantCulture)),
            [KindMember] = JsonValue.Create(item.Subject.Kind.ToString()),
            [DecidedMember] = Written(item.Decided),
            [HeldWasReadMember] = JsonValue.Create(item.HeldWasRead),
            [HeldMember] = item.Held is null ? null : Written(item.Held),
        };

        return entry;
    }

    private static JsonObject Written(SyncedState state) => new JsonObject
    {
        [PlayedMember] = JsonValue.Create(state.Played),
        [PlayCountMember] = JsonValue.Create(state.PlayCount),
        [PositionTicksMember] = JsonValue.Create(state.PlaybackPositionTicks),
        [LastPlayedMember] = state.LastPlayedDate is null
            ? null
            : JsonValue.Create(
                state.LastPlayedDate.Value.ToString("o", CultureInfo.InvariantCulture)),
    };

    /// <summary>
    /// Reads one entry of the document as an item of the plan.
    ///
    /// The held reading is read in two members and refused where they disagree: a state beside
    /// a flag saying nothing was read is a document somebody assembled, and reading it either
    /// way would decide for the approval whether that item has a baseline.
    /// </summary>
    /// <param name="mappedUserId">The mapped user the document is about.</param>
    /// <param name="node">The entry.</param>
    /// <param name="item">What it holds, where it is an item.</param>
    /// <returns>Whether the entry is an item.</returns>
    private static bool TryReadItem(Guid mappedUserId, JsonNode? node, out StoppedRunItem? item)
    {
        item = null;

        if (node is not JsonObject members
            || !TryReadIdentifier(members, ItemMember, out var itemId)
            || !TryReadKind(members, out var kind)
            || !TryReadState(members[DecidedMember], out var decided)
            || decided is null
            || members[HeldWasReadMember] is not JsonValue readValue
            || !readValue.TryGetValue<bool>(out var heldWasRead)
            || !members.ContainsKey(HeldMember)
            || !TryReadState(members[HeldMember], out var held)
            || (!heldWasRead && held is not null))
        {
            return false;
        }

        var subject = TransferSubject.From(mappedUserId, itemId, kind);

        if (!subject.IsSubject)
        {
            return false;
        }

        item = heldWasRead
            ? StoppedRunItem.Read(subject.Value!, decided, held)
            : StoppedRunItem.Unread(subject.Value!, decided);

        return true;
    }

    /// <summary>
    /// Reads a state out of a member, where null is a reading of nothing and an object is a
    /// reading of a state. A member that is neither is not a reading.
    /// </summary>
    /// <param name="node">The member.</param>
    /// <param name="state">The state, or null where the member holds null.</param>
    /// <returns>Whether the member is a reading.</returns>
    private static bool TryReadState(JsonNode? node, out SyncedState? state)
    {
        state = null;

        if (node is null)
        {
            return true;
        }

        if (node is not JsonObject members
            || members[PlayedMember] is not JsonValue playedValue
            || !playedValue.TryGetValue<bool>(out var played)
            || !TryReadWholeNumber(members, PlayCountMember, out var playCount)
            || playCount > int.MaxValue
            || !TryReadWholeNumber(members, PositionTicksMember, out var positionTicks)
            || !TryReadLastPlayed(members, out var lastPlayed))
        {
            return false;
        }

        state = new SyncedState(played, (int)playCount, positionTicks, lastPlayed);

        return true;
    }

    private static bool TryReadAnswer(JsonObject fields, out RunCapAnswer answer)
    {
        answer = default;

        return fields[AnswerMember] is JsonValue value
            && value.TryGetValue<string>(out var name)
            && Enum.TryParse(name, out answer)
            && string.Equals(answer.ToString(), name, StringComparison.Ordinal)
            && answer != RunCapAnswer.Within;
    }

    private static bool TryReadKind(JsonObject members, out BaseItemKind kind)
    {
        kind = default;

        return members[KindMember] is JsonValue value
            && value.TryGetValue<string>(out var name)
            && Enum.TryParse(name, out kind)
            && string.Equals(kind.ToString(), name, StringComparison.Ordinal);
    }

    private static bool TryReadIdentifier(JsonObject fields, string member, out Guid identifier)
    {
        identifier = Guid.Empty;

        return fields[member] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Guid.TryParseExact(text, "n", out identifier)
            && identifier != Guid.Empty;
    }

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

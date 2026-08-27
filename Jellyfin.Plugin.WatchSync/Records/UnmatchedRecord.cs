using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Matching;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// One item that did not match, as it is written down: which item, its class, why it did not
/// match, and when that was last attempted.
///
/// #26's rule is that no match is a terminal answer for that item in that run, with a reason
/// recorded, and never a fallback to a weaker comparison. A record that says an item did not
/// match and does not say why is what makes the fallback attractive: an operator who cannot tell
/// a home video with no identifier from a film the peer does not hold has no repair to make and
/// no way to tell that there was one.
///
/// <para>
/// Why it did not match is carried in two members rather than one, and exactly one of them says
/// it. Deriving a key and looking one up are two steps, and an item falls out of either: a key
/// that could not be derived at all carries a <see cref="MatchKeyRefusal"/> and never reached a
/// lookup, and a key that was derived and answered nothing carries a <see cref="MatchAnswer"/>.
/// A third enumeration spanning both would be a list drifting against two, which is what
/// <c>docs/unmatched.md</c> and #80's corpus are both already held to instead:
/// <c>UnmatchedGuideTests</c> reads its vocabulary off these same two enumerations, so a reason
/// added to either joins the set without anybody editing a list.
/// </para>
///
/// <para>
/// Three refusals hold that apart. A refusal beside an answer says a lookup happened after the
/// key was refused, which no route can do. Neither of the two says the item is unmatched and does
/// not say why, which is the guess this issue exists against arriving as an empty field. And a
/// record naming <see cref="MatchAnswer.Matched"/> says an item that matched is unmatched, which
/// is a count an operator would act on and a repair somebody would go looking for.
/// </para>
///
/// <para>
/// It carries identifiers, the class, the reason and a moment, and nothing else, which is
/// <see cref="ConflictRecord"/>'s rule for the same reason and one more of its own: this record
/// is about items a peer may have offered, so a title or a path here would be both a viewing
/// history and a string a machine this server does not administer, arriving in a record that is
/// meant to be counted on a page. <c>UnmatchedRecordTests</c> refuses a member of any type
/// outside the declared set.
/// </para>
///
/// <para>
/// What this does not decide: nothing produces one yet. The matcher answers, and no run walks a
/// library and hands what it refused to a record. The sweep that would is #55, and the count this
/// record is the source for is the status page in #62.
/// </para>
/// </summary>
public sealed class UnmatchedRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnmatchedRecord"/> class.
    /// </summary>
    /// <param name="itemId">The local item that did not match.</param>
    /// <param name="kind">Its class, as the server names it.</param>
    /// <param name="refusal">
    /// Why no key could be derived, or <see cref="MatchKeyRefusal.None"/> where one was.
    /// </param>
    /// <param name="answer">
    /// What the lookup answered, or <c>null</c> where no key was derived and none happened.
    /// </param>
    /// <param name="lastAttemptedAt">When matching this item was last attempted.</param>
    /// <exception cref="ArgumentException">
    /// A refusal and an answer are carried together, neither is carried, or the answer is
    /// <see cref="MatchAnswer.Matched"/>.
    /// </exception>
    public UnmatchedRecord(
        Guid itemId,
        BaseItemKind kind,
        MatchKeyRefusal refusal,
        MatchAnswer? answer,
        DateTimeOffset lastAttemptedAt)
    {
        if (refusal != MatchKeyRefusal.None && answer is not null)
        {
            throw new ArgumentException(
                "The key was refused, so nothing was looked up, and a record carrying both says a lookup happened that no route could have made.",
                nameof(answer));
        }

        if (refusal == MatchKeyRefusal.None && answer is null)
        {
            throw new ArgumentException(
                "The record says this item did not match and says nothing about why, which is the answer an operator cannot act on and the one this record exists to refuse.",
                nameof(answer));
        }

        if (answer == MatchAnswer.Matched)
        {
            throw new ArgumentException(
                "An item that matched is not an unmatched item, and a record of one is a count somebody would go looking for a repair for.",
                nameof(answer));
        }

        ItemId = itemId;
        Kind = kind;
        Refusal = refusal;
        Answer = answer;
        LastAttemptedAt = lastAttemptedAt;
    }

    /// <summary>
    /// Gets the local item that did not match.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the item's class, as the server names it.
    ///
    /// It is here because it separates the reasons an operator can act on from the ones they
    /// cannot without opening the item: an episode with no numbering and a film with no
    /// identifier are different pieces of work, and the reason alone does not say which of the
    /// two a row is.
    /// </summary>
    public BaseItemKind Kind { get; }

    /// <summary>
    /// Gets why no key could be derived, or <see cref="MatchKeyRefusal.None"/> where one was and
    /// <see cref="Answer"/> is what did not match.
    /// </summary>
    public MatchKeyRefusal Refusal { get; }

    /// <summary>
    /// Gets what the lookup answered, or <c>null</c> where no key was derived and no lookup
    /// happened.
    /// </summary>
    public MatchAnswer? Answer { get; }

    /// <summary>
    /// Gets when matching this item was last attempted, which the caller supplies rather than
    /// this type reading a clock, because a rule in this plugin reads the injected clock and
    /// nothing else.
    ///
    /// It is the last attempt rather than the first, which is what makes #26's fourth condition
    /// readable: an item that acquires an identifier later is attempted again, and the moment
    /// moving is what says a sweep has been past it since.
    /// </summary>
    public DateTimeOffset LastAttemptedAt { get; }
}

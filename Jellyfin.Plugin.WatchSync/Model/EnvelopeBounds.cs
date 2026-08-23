using System;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What one envelope may carry, which is #19.
///
/// An envelope arrives from another server. Even a paired one is a machine this server does not
/// administer, and an operator whose peer has been taken over should lose a pairing rather than
/// a server. So the four bounds below are refusals with their own answers rather than
/// truncations, and each of them carries the reason for its value beside it.
///
/// Two of the four sit under a ceiling this plugin does not choose. The transport an envelope
/// travels on caps the body of one exchange, and the same plane spends a bounded budget of
/// remembered requests per pairing inside a window. Both are read out of
/// <c>Flowfin/jellyfin-plugin-server-pairing</c>, and both move when that tree moves, so what
/// is written here is the reason and the relation rather than a copy of somebody else's number
/// treated as this plugin's own. <see cref="TransportBodyCeilingBytes"/> and
/// <see cref="FreshnessBudgetPerPairing"/> carry the readings and say when they were taken;
/// <c>EnvelopeBoundsTests</c> refuses this plugin's own bounds reaching either of them.
///
/// Nothing here reads an envelope. #18 defines and versions the type, and this judges the two
/// counts and the two lengths a reader hands over before it has read anything, which is what
/// lets the refusal happen before the allocation.
/// </summary>
public sealed class EnvelopeBounds
{
    private EnvelopeBounds(EnvelopeBoundsAnswer answer, long? bound, long? counted)
    {
        Answer = answer;
        Bound = bound;
        Counted = counted;
    }

    /// <summary>
    /// Gets how many changes one envelope may carry.
    ///
    /// A thousand. It is what the answering side may put in one reply, so it decides how much a
    /// peer can be made to hold at once and how long one exchange takes, and it is not a limit
    /// on how much can ever move: an exchange that reaches it stops, records that it stopped and
    /// why, and leaves a watermark the next exchange resumes from, which is the rule
    /// <c>docs/transfer.md</c> already states. So the bound costs an extra exchange rather than
    /// losing a change.
    ///
    /// A thousand against <see cref="MaximumBytes"/> leaves more than two hundred bytes for each
    /// change, and a change is a match key, four field values and a date. So the two bounds are
    /// reachable together rather than one of them making the other unreachable, which is the
    /// state a pair of bounds usually ends up in when neither was written against the other.
    /// </summary>
    public static int MaximumChanges => 1000;

    /// <summary>
    /// Gets how many bytes one envelope may declare.
    ///
    /// A quarter of a mebibyte, and the reason is where it sits rather than the number. The
    /// transport refuses an exchange body above <see cref="TransportBodyCeilingBytes"/> without
    /// reading past the limit and without parsing it, so anything this plugin set at or above
    /// that ceiling would never bind: the layer below would answer first, and this plugin's
    /// refusal, its own code and the record #19 asks for would all be unreachable.
    ///
    /// It sits well below rather than just below for two reasons. An envelope is the body of a
    /// request and not the whole of it, so whatever framing the transport puts around it is
    /// room this plugin does not control and must not spend. And a bound a legitimate envelope
    /// never approaches is one whose refusal is always a peer doing something wrong, which is
    /// what makes the refusal worth showing to an operator.
    /// </summary>
    public static int MaximumBytes => 256 * 1024;

    /// <summary>
    /// Gets how long a string field in one envelope may be, counted in characters.
    ///
    /// Five hundred and twelve. The longest string an envelope legitimately carries is a match
    /// key, which is a provider name beside an identifier, or a series key with the ordering it
    /// was matched under and two numbers. Those are tens of characters, so this sits an order of
    /// magnitude above anything anybody writes and still refuses the field that is carrying
    /// something other than a key.
    ///
    /// It is deliberately not <c>PeerText.DefaultLimit</c>, which bounds what is shown rather
    /// than what may arrive. A value between the two is accepted, stored and shortened when it
    /// reaches a page or a log. Making one number do both would mean either refusing an
    /// envelope for being wider than a column, or displaying whatever an envelope was allowed
    /// to carry.
    /// </summary>
    public static int LongestStringLength => 512;

    /// <summary>
    /// Gets how many envelopes one peer may send inside <see cref="Window"/>.
    ///
    /// Sixty four, which is one every ten seconds sustained. The event path and the sweep
    /// together produce nothing like that for one pairing, because the position thresholds
    /// bound what one playback carries and the sweep runs on a schedule, so a peer reaching this
    /// is looping rather than syncing.
    ///
    /// The value has to leave room under <see cref="FreshnessBudgetPerPairing"/> rather than sit
    /// at it. That budget is spent by every request type on the pairing and not by envelopes
    /// alone, so a peer running at a bound set near it would spend the pairing's freshness on
    /// syncing and refuse traffic that has nothing to do with this plugin.
    /// </summary>
    public static int MaximumEnvelopesInAWindow => 64;

    /// <summary>
    /// Gets the window <see cref="MaximumEnvelopesInAWindow"/> is counted over.
    ///
    /// Ten minutes. It is this plugin's own number and not a copy of the plane's remembered
    /// window, although the two are the same length today, because a window derived from
    /// somebody else's constant changes meaning silently on the day that constant moves. What
    /// the reason has to say is why ten minutes: it is long enough that an ordinary burst at the
    /// end of an evening, several people stopping several things at once, sits inside one
    /// window without reaching the count, and short enough that a peer refused for looping is
    /// answering again within an evening rather than the next day.
    /// </summary>
    public static TimeSpan Window => TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the largest exchange body the transport carries, above which the layer below
    /// refuses before this plugin sees anything.
    ///
    /// A mebibyte. It is a reading of another tree rather than a decision here, taken at
    /// <c>b9c23d4b36c8650bd0064ecd8c7122a86249ce96</c> of
    /// <c>Flowfin/jellyfin-plugin-server-pairing</c>, from the request table in
    /// <c>docs/protocol.md</c>. It is here so that <see cref="MaximumBytes"/> can be held below
    /// it by the suite rather than by somebody remembering the relation, and nothing in this
    /// tree re-reads it: a ceiling that moves over there leaves this number saying what used to
    /// be true, and a reading at review is what stands against that.
    /// </summary>
    public static int TransportBodyCeilingBytes => 1024 * 1024;

    /// <summary>
    /// Gets how many requests the plane remembers for one pairing before a fresh one arriving
    /// with no room left is refused.
    ///
    /// Four thousand and ninety six, read at the same commit as
    /// <see cref="TransportBodyCeilingBytes"/>, from <c>Protocol/FreshnessWindow.cs</c> in that
    /// tree. It is a budget shared by every request type on the pairing rather than a bound on
    /// envelopes, which is why <see cref="MaximumEnvelopesInAWindow"/> is a small fraction of it
    /// and not a number just underneath it.
    /// </summary>
    public static int FreshnessBudgetPerPairing => 4096;

    /// <summary>
    /// Gets what the bounds answered.
    /// </summary>
    public EnvelopeBoundsAnswer Answer { get; }

    /// <summary>
    /// Gets the bound that was crossed, or null where nothing was.
    ///
    /// It is carried out rather than left to be looked up, because #19's fourth condition asks
    /// that a refusal be recorded with the peer, the bound and the count. The peer is the
    /// caller's and the record is #36 and #62; these two are this rule's.
    /// </summary>
    public long? Bound { get; }

    /// <summary>
    /// Gets what was counted against the bound that was crossed, or null where none was.
    ///
    /// A refusal that says only which bound was crossed leaves an operator unable to tell a
    /// peer one change over the line from a peer sending a hundred times the limit, and those
    /// are different problems with different answers.
    /// </summary>
    public long? Counted { get; }

    /// <summary>
    /// Gets a value indicating whether the envelope may be read.
    /// </summary>
    public bool MayBeRead => Answer == EnvelopeBoundsAnswer.Within;

    /// <summary>
    /// Judges one envelope against the four bounds, before it is read.
    ///
    /// Everything handed in is a count or a length rather than the thing counted, which is what
    /// makes the refusal cheaper than the envelope. A caller that has the bytes already has
    /// paid the allocation this is meant to avoid, and the way to use this is to ask it with
    /// the declared length before the read rather than with the actual length after it.
    ///
    /// The order is the order below and it is a decision. The window is asked first, because a
    /// peer over its rate is refused whatever it is carrying. The byte length is asked next,
    /// because it is the only one of the three that is knowable without parsing, so a caller
    /// that stopped here has still not read anything. The change count and the longest string
    /// come after, in the order a reader learns them.
    ///
    /// Every boundary is drawn the same way and there is no exception to look for: a value
    /// equal to its bound is within, and one unit past it is refused. A bound is the largest
    /// value that is still allowed.
    ///
    /// The window is the one where reading that carefully matters, because the count handed in
    /// excludes the envelope being judged. What is held to the bound is the count including it,
    /// so a peer that has already sent one short of the bound may send this one, and the count
    /// carried out of a refusal is the number this envelope would have made rather than the
    /// number the caller passed.
    ///
    /// What this cannot see is stated here rather than left to be found. It is handed the
    /// longest string rather than the strings, so a reader that measured the wrong field, or
    /// measured in the units the runtime stores characters in rather than in the characters a
    /// person sees, is refused by nothing here. And it counts this peer's envelopes inside the
    /// window from a number the caller keeps, so a caller that keeps that count per process
    /// answers differently after a restart than one that keeps it in the store.
    /// </summary>
    /// <param name="envelopesFromThisPeerInTheWindow">
    /// How many envelopes this peer has already sent inside <see cref="Window"/>, not counting
    /// this one.
    /// </param>
    /// <param name="declaredBytes">
    /// How many bytes the envelope declares, read before the envelope is.
    /// </param>
    /// <param name="changes">How many changes the envelope carries.</param>
    /// <param name="longestStringLength">
    /// The length of the longest string field in the envelope, counted in the characters a
    /// person sees.
    /// </param>
    /// <returns>What the bounds answered.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any of the four below zero. None of them is a quantity that can be negative, so a
    /// negative one is a caller that computed it wrongly, and a rule that let it through would
    /// answer <see cref="EnvelopeBoundsAnswer.Within"/> for it.
    /// </exception>
    public static EnvelopeBounds Judge(
        int envelopesFromThisPeerInTheWindow,
        long declaredBytes,
        int changes,
        int longestStringLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            envelopesFromThisPeerInTheWindow,
            nameof(envelopesFromThisPeerInTheWindow));
        ArgumentOutOfRangeException.ThrowIfNegative(declaredBytes, nameof(declaredBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(changes, nameof(changes));
        ArgumentOutOfRangeException.ThrowIfNegative(
            longestStringLength,
            nameof(longestStringLength));

        if (envelopesFromThisPeerInTheWindow >= MaximumEnvelopesInAWindow)
        {
            return new EnvelopeBounds(
                EnvelopeBoundsAnswer.TooManyEnvelopesInTheWindow,
                MaximumEnvelopesInAWindow,
                envelopesFromThisPeerInTheWindow + 1L);
        }

        if (declaredBytes > MaximumBytes)
        {
            return new EnvelopeBounds(
                EnvelopeBoundsAnswer.TooManyBytes,
                MaximumBytes,
                declaredBytes);
        }

        if (changes > MaximumChanges)
        {
            return new EnvelopeBounds(
                EnvelopeBoundsAnswer.TooManyChanges,
                MaximumChanges,
                changes);
        }

        if (longestStringLength > LongestStringLength)
        {
            return new EnvelopeBounds(
                EnvelopeBoundsAnswer.AStringIsTooLong,
                LongestStringLength,
                longestStringLength);
        }

        return new EnvelopeBounds(EnvelopeBoundsAnswer.Within, null, null);
    }
}

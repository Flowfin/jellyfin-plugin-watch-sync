namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// Which of the two servers a conflict record is saying something about.
///
/// The vocabulary is the one the rules already answer in: a value discarded here and a value
/// discarded at the peer are separate members of every answer on the mainline, because the two
/// are different events for the person whose history it is. One is something they did on this
/// server that this server is about to stop showing them; the other is something they did
/// elsewhere that never arrives.
///
/// <see cref="Neither"/> is not an absence and is not a tie. Two of the four rows discard
/// nothing by construction: a reckoned count carries a side up rather than lowering the other,
/// and a maximum keeps a moment that already happened. A record of one of those conflicts is
/// still worth writing, because an operator asking why a count moved is asking about a rule
/// that ran, and a record type that could not say "nothing was discarded" would leave those
/// two rows either unrecorded or recorded as a loss nobody took.
/// </summary>
public enum ConflictSide
{
    /// <summary>
    /// The reading this server held.
    /// </summary>
    Here,

    /// <summary>
    /// The reading the peer offered.
    /// </summary>
    AtThePeer,

    /// <summary>
    /// Neither reading was discarded, because the rule that decided discards nothing.
    /// </summary>
    Neither,
}

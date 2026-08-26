namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// What the maximum over two last played dates answered.
///
/// Two values and neither is a refusal, which is what makes this row different from the other
/// three. A maximum over two moments has an answer for every pair there is: the later of them,
/// or nothing at all where neither side ever played the work. There is no state of the two
/// dates that this rule cannot decide, so an answer standing for a refusal would be a value
/// nothing could ever produce.
/// </summary>
public enum LastPlayedAnswer
{
    /// <summary>
    /// At least one side holds a date, and the later of the two is the answer.
    ///
    /// A side holding none is not a competitor that lost. Never having played the work is
    /// earlier than every moment somebody did, so the one date there is stands for the same
    /// reason the later of two does, and this is one answer rather than two.
    /// </summary>
    TheLaterDateStands,

    /// <summary>
    /// Neither side ever played the work, so there is no date to carry and none is invented.
    ///
    /// It is its own answer rather than the later date being null, because <c>docs/conflicts.md</c>
    /// says this row discards nothing and a caller reading a null out of an answer that claims a
    /// date stands has to decide for itself which of the two it is holding.
    /// </summary>
    NeitherSideHasPlayed,
}

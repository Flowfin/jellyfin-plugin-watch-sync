namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// What a lookup answered.
///
/// These are the three answers <c>docs/matching.md</c> fixes, and that document says in the
/// same sentence that an implementation inventing a fourth is wrong. A member added here is
/// a change to that document first.
/// </summary>
public enum MatchAnswer
{
    /// <summary>
    /// One local item carries the key. Watch state may move to it.
    /// </summary>
    Matched,

    /// <summary>
    /// More than one local item carries the key. Nothing moves, and the competing items are
    /// carried in the answer so an operator is told which they are rather than being told
    /// that something was ambiguous.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// No local item carries the key. Nothing moves. It is a terminal answer for that key in
    /// that run, and there is no second pass at a weaker comparison.
    /// </summary>
    NoMatch,
}

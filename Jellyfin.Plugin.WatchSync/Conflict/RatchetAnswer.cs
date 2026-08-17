namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// What holding a completion against a position answered.
///
/// The two answers are not a winner and a loser. One of them is the rule deciding, and the
/// other is the rule saying that its case is not the one in front of it, because two sides
/// that both hold a position and neither a completion are a disagreement about where the
/// person stopped rather than about whether they finished. That is a different rule with a
/// different reason, and #32 is where it is written.
/// </summary>
public enum RatchetAnswer
{
    /// <summary>
    /// At least one side holds the work played, so played is the answer for both.
    ///
    /// A position offered against it is discarded whatever its time says, and it is carried
    /// out rather than dropped so that #36 can record what lost.
    /// </summary>
    PlayedStands,

    /// <summary>
    /// Neither side holds the work played, so there is no completion for this rule to hold.
    ///
    /// Nothing is discarded and nothing is decided here. Which of the two positions is the
    /// answer is settled by the later play bounded by the tolerated clock skew, which is #32.
    /// </summary>
    NoCompletionToHold,
}

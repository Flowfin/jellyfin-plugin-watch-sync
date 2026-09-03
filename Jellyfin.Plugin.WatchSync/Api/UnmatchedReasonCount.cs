namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// How many unmatched items share one reason.
///
/// The reason is the name of the refusal or of the lookup answer, as the enumeration spells it,
/// and never a title or a path. What an operator does about an unmatched item is metadata work
/// they do elsewhere, and the reason is what tells them which work; <c>docs/unmatched.md</c> has
/// a section per reason under the same names.
/// </summary>
public sealed class UnmatchedReasonCount
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnmatchedReasonCount"/> class.
    /// </summary>
    /// <param name="reason">The reason, as the enumeration spells it.</param>
    /// <param name="count">How many unmatched items carry it.</param>
    public UnmatchedReasonCount(string reason, int count)
    {
        Reason = reason;
        Count = count;
    }

    /// <summary>
    /// Gets the reason, as the enumeration spells it.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets how many unmatched items carry it.
    /// </summary>
    public int Count { get; }
}

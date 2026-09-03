using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// What approving a stopped run did.
///
/// Two lists and a walk. The walk is what was written and what was refused, in
/// <see cref="ItemByItemApply"/>'s own answer, with the two records advanced for the written. The
/// set-aside list is the items the approval declined to hand to the walk at all, each with the
/// reason, and it is the part of this answer #38's third condition is about: an item that changed
/// between the stop and the approval is here rather than in the walk, so the plan applied is the
/// plan the operator read.
///
/// The three lists together say what the approval examined, which is <c>docs/transfer.md</c>'s
/// rule on every run: one that covered less than everything is never reported as one that
/// covered it all. An item is in exactly one of them or was never reached, and the walk says
/// whether it stopped short.
/// </summary>
public sealed class ApprovalAnswer
{
    private readonly List<ItemSetAside> _setAside;

    internal ApprovalAnswer(ApplyAnswer walk, List<ItemSetAside> setAside)
    {
        Walk = walk;
        _setAside = setAside;
    }

    /// <summary>
    /// Gets what the walk over the items that had not moved did: what was written, what was
    /// refused, and the two records advanced for the written.
    /// </summary>
    public ApplyAnswer Walk { get; }

    /// <summary>
    /// Gets the items the approval did not hand to the walk, each with the reason.
    /// </summary>
    public IReadOnlyList<ItemSetAside> SetAside => _setAside;

    /// <summary>
    /// Gets the items that were written, which is the walk's own list carried up so that a
    /// caller reading an approval does not have to know there is a walk inside it.
    /// </summary>
    public IReadOnlyList<TransferSubject> Applied => Walk.Applied;
}

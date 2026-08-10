using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// What the index answered for one key.
///
/// The competing items are carried rather than counted, for the same reason a key refusal
/// carries its reason: an operator who is told two items claim one key can fix the library,
/// and one who is told only that something was ambiguous can do nothing with it.
/// </summary>
public sealed class MatchLookup
{
    private static readonly IReadOnlyList<Guid> _none = Array.Empty<Guid>();

    private MatchLookup(MatchAnswer answer, Guid item, IReadOnlyList<Guid> competingItems)
    {
        Answer = answer;
        Item = item;
        CompetingItems = competingItems;
    }

    /// <summary>
    /// Gets the answer.
    /// </summary>
    public MatchAnswer Answer { get; }

    /// <summary>
    /// Gets the item that carries the key, where exactly one does, and
    /// <see cref="Guid.Empty"/> otherwise.
    /// </summary>
    public Guid Item { get; }

    /// <summary>
    /// Gets the items that claim the key, where more than one does, and an empty list
    /// otherwise.
    /// </summary>
    public IReadOnlyList<Guid> CompetingItems { get; }

    /// <summary>
    /// Gets a value indicating whether watch state may move to the item.
    ///
    /// It is one property rather than a comparison every caller writes, because the two
    /// answers that are not a match differ in what is recorded and not in what is done, and
    /// a caller checking only for <see cref="MatchAnswer.NoMatch"/> would write to a
    /// competing item.
    /// </summary>
    public bool IsMatched => Answer == MatchAnswer.Matched;

    /// <summary>
    /// The answer where one local item carries the key.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The answer.</returns>
    internal static MatchLookup Matched(Guid item) =>
        new MatchLookup(MatchAnswer.Matched, item, _none);

    /// <summary>
    /// The answer where several local items claim the key.
    /// </summary>
    /// <param name="competingItems">The items that claim it.</param>
    /// <returns>The answer.</returns>
    internal static MatchLookup Ambiguous(IReadOnlyList<Guid> competingItems) =>
        new MatchLookup(MatchAnswer.Ambiguous, Guid.Empty, competingItems);

    /// <summary>
    /// The answer where no local item carries the key.
    /// </summary>
    /// <returns>The answer.</returns>
    internal static MatchLookup NoMatch() =>
        new MatchLookup(MatchAnswer.NoMatch, Guid.Empty, _none);
}

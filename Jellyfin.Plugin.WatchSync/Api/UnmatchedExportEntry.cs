using System;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// One unmatched item as the export carries it: the item, its kind, why it did not match, and
/// when it was last attempted.
///
/// The item is an identifier and never a title or a path. The fix for most unmatched items is
/// metadata work an operator does in the library, where the identifier is the address of the
/// item; a title would be a work's name beside a person, which this plugin refuses on every
/// surface it serves.
/// </summary>
public sealed class UnmatchedExportEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnmatchedExportEntry"/> class.
    /// </summary>
    /// <param name="itemId">The item, as this server names it.</param>
    /// <param name="kind">Its kind, as the server classified it.</param>
    /// <param name="refusal">Why no key could be derived, or the name for none.</param>
    /// <param name="answer">What the lookup answered where a key was derived, or null.</param>
    /// <param name="lastAttemptedAt">When the item was last attempted.</param>
    public UnmatchedExportEntry(
        Guid itemId,
        string kind,
        string refusal,
        string? answer,
        DateTimeOffset lastAttemptedAt)
    {
        ItemId = itemId;
        Kind = kind;
        Refusal = refusal;
        Answer = answer;
        LastAttemptedAt = lastAttemptedAt;
    }

    /// <summary>
    /// Gets the item, as this server names it.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets its kind, as the server's enumeration spells it.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets why no key could be derived, as the enumeration spells it, or its name for none.
    /// </summary>
    public string Refusal { get; }

    /// <summary>
    /// Gets what the lookup answered where a key was derived, or null where none was.
    /// </summary>
    public string? Answer { get; }

    /// <summary>
    /// Gets when the item was last attempted.
    /// </summary>
    public DateTimeOffset LastAttemptedAt { get; }
}

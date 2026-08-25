using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// Every version this plugin has written a document at, and the ladder that carries an older
/// one forward.
///
/// This is the one place the versions are declared, which is the first thing #71 asks for.
/// <see cref="Shipped"/> is the declaration and everything else is derived from it: the version
/// this code writes is the newest entry, the number of steps the ladder owes is one fewer than
/// the number of entries, and the fixtures the suite requires are one per entry. A version added
/// here without its step or its fixture is a red suite rather than an upgrade that silently does
/// nothing.
///
/// The list is kept rather than trimmed. A document at the oldest version in it is a document an
/// operator can still have on disk, because a store is older than the code reading it exactly as
/// often as somebody restores a backup, and dropping the entry would turn that document from one
/// that is carried forward into one that is refused.
/// </summary>
public static class DocumentVersions
{
    /// <summary>
    /// Gets every version this plugin has written a document at, oldest first.
    ///
    /// There is one today. The agreed record in #14 is the first shape the store holds and the
    /// rest are #26, #36, #44 and #48, and nothing here says what a document at version 1
    /// contains: a shape is declared by the type that writes it, and what this says is that a
    /// document carrying that number was written by this plugin and is readable, which is what
    /// an upgrade needs to know.
    /// </summary>
    public static IReadOnlyList<int> Shipped { get; } = new[] { 1 };

    /// <summary>
    /// Gets the version this code writes, which is the newest shipped one.
    /// </summary>
    public static int Current => Shipped[Shipped.Count - 1];

    /// <summary>
    /// Gets the ladder, one step per version below <see cref="Current"/>.
    ///
    /// It is empty, because no document shape has changed yet. That is the state of the tree and
    /// not a hole in the mechanism: <see cref="DocumentUpgrade"/> refuses a ladder that does not
    /// carry a step for every version below the current one, so the day a second version is
    /// declared here is the day the missing step refuses to construct.
    /// </summary>
    public static IReadOnlyList<DocumentUpgradeStep> Ladder { get; } =
        Array.Empty<DocumentUpgradeStep>();

    /// <summary>
    /// The ladder as this plugin declares it, ready to carry a document forward.
    /// </summary>
    /// <returns>The upgrade over the declared versions.</returns>
    public static DocumentUpgrade Upgrade() => new DocumentUpgrade(Current, Ladder);
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// The ladder that carries a document written by an older version up to the version this code
/// writes, one rung at a time.
///
/// #69 answers a document older than this code as readable and not current, and hands the
/// carrying over to this. The rules here are the ones #71 states. An upgrade is a function from
/// one version to the next, applied in sequence, so a document three versions old goes through
/// each step rather than through a special case. An upgrade never loses a member it does not
/// understand, which <see cref="DocumentUpgradeStep"/> makes a property of the mechanism rather
/// than of each step. And a document already at the current version is answered without a step
/// running at all, because an upgrade run twice on one document is the failure that turns a
/// rename into a deletion.
///
/// The ladder is checked when it is built and not when it is used. A ladder missing the step for
/// a version it declares, or holding two steps for one version, or holding one that skips a
/// version, is refused at construction, so the day a version is declared without its step is the
/// day this refuses to construct rather than the day a document quietly comes back unchanged.
/// </summary>
public sealed class DocumentUpgrade
{
    private readonly List<DocumentUpgradeStep> _ladder;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentUpgrade"/> class.
    /// </summary>
    /// <param name="currentVersion">The version this code writes.</param>
    /// <param name="ladder">
    /// One step per version below <paramref name="currentVersion"/>, oldest first. Empty where
    /// no shape has changed yet, which is what this plugin declares today.
    /// </param>
    /// <exception cref="ArgumentNullException">The ladder is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The current version is not a whole number above zero.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The ladder does not carry exactly one step for every version from 1 up to the one below
    /// the current version, in order.
    /// </exception>
    public DocumentUpgrade(int currentVersion, IReadOnlyList<DocumentUpgradeStep> ladder)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);

        if (ladder.Count != currentVersion - 1)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Version {0} needs {1} step(s) to be reachable from version 1 and the ladder"
                    + " carries {2}.",
                    currentVersion,
                    currentVersion - 1,
                    ladder.Count),
                nameof(ladder));
        }

        for (var rung = 0; rung < ladder.Count; rung++)
        {
            var step = ladder[rung];

            if (step is null)
            {
                throw new ArgumentException(
                    "The ladder carries nothing where a step belongs.",
                    nameof(ladder));
            }

            if (step.From != rung + 1)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The ladder is one step per version, oldest first, so the step at"
                        + " position {0} reads version {1}, and this one reads version {2}.",
                        rung,
                        rung + 1,
                        step.From),
                    nameof(ladder));
            }
        }

        CurrentVersion = currentVersion;
        _ladder = new List<DocumentUpgradeStep>(ladder);
    }

    /// <summary>
    /// Gets the version this ladder carries a document up to.
    /// </summary>
    public int CurrentVersion { get; }

    /// <summary>
    /// Carries a document up to the version this code writes.
    ///
    /// A document already at that version is answered as it stands and no step runs over it,
    /// which is the first half of what #71 asks about running an upgrade twice. The second half
    /// follows from it: carrying an answer's document again reaches the same case, so it runs
    /// nothing and changes nothing.
    /// </summary>
    /// <param name="document">The document, as <see cref="StoredDocument.Read"/> answered it.</param>
    /// <returns>The document at the current version, and the route it took.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The document carries a version above the one this code writes. That is a document from the
    /// future, which #69 refuses before anything reaches here, so meeting one here is a caller
    /// that read a refusal and carried on rather than a state a store can be in.
    /// </exception>
    public DocumentUpgradeAnswer Carry(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Version > CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(document),
                document.Version,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A document at version {0} is from the future for code that writes {1}, and a"
                    + " refusal is not something to carry forward.",
                    document.Version,
                    CurrentVersion));
        }

        if (document.Version == CurrentVersion)
        {
            return DocumentUpgradeAnswer.AlreadyCurrent(document);
        }

        var fields = document.Fields.DeepClone().AsObject();
        var passedThrough = new List<int>();

        for (var version = document.Version; version < CurrentVersion; version++)
        {
            var step = _ladder[version - 1];

            fields = step.Apply(fields);
            passedThrough.Add(step.To);
        }

        return DocumentUpgradeAnswer.CarriedForward(
            StoredDocument.At(CurrentVersion, fields),
            document.Version,
            passedThrough);
    }
}

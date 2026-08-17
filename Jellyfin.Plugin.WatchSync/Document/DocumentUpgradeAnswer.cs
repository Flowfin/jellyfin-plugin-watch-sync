using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// What one attempt to carry a document forward answered.
///
/// The versions the document passed through are carried out rather than being counted inside,
/// because the property #71 asks for is about the route and not about the destination. A ladder
/// that reached the current version in one jump and one that walked every rung produce the same
/// document, and only the route separates them.
/// </summary>
public sealed class DocumentUpgradeAnswer
{
    private readonly List<int> _versionsPassedThrough;

    private DocumentUpgradeAnswer(
        DocumentUpgradeOutcome outcome,
        StoredDocument document,
        int fromVersion,
        List<int> versionsPassedThrough)
    {
        Outcome = outcome;
        Document = document;
        FromVersion = fromVersion;
        _versionsPassedThrough = versionsPassedThrough;
    }

    /// <summary>
    /// Gets what carrying the document forward came to.
    /// </summary>
    public DocumentUpgradeOutcome Outcome { get; }

    /// <summary>
    /// Gets the document at the version this code writes.
    ///
    /// It is the document that was handed over where nothing had to be carried, and a new one
    /// where something did. Nothing is written anywhere: where a document is written and how the
    /// write survives a kill in the middle of it is #70.
    /// </summary>
    public StoredDocument Document { get; }

    /// <summary>
    /// Gets the version the document carried when it arrived.
    /// </summary>
    public int FromVersion { get; }

    /// <summary>
    /// Gets the versions the document was carried to, in the order it reached them.
    ///
    /// Empty where nothing was carried. A document two versions behind names both the version in
    /// between and the current one, which is what proves it went up a rung at a time rather than
    /// being handed to a step that knew the whole distance.
    /// </summary>
    public IReadOnlyList<int> VersionsPassedThrough => _versionsPassedThrough;

    internal static DocumentUpgradeAnswer AlreadyCurrent(StoredDocument document) =>
        new DocumentUpgradeAnswer(
            DocumentUpgradeOutcome.AlreadyCurrent,
            document,
            document.Version,
            new List<int>());

    internal static DocumentUpgradeAnswer CarriedForward(
        StoredDocument document,
        int fromVersion,
        List<int> versionsPassedThrough) =>
        new DocumentUpgradeAnswer(
            DocumentUpgradeOutcome.CarriedForward,
            document,
            fromVersion,
            versionsPassedThrough);
}

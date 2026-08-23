using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What one attempt to read an envelope from a peer came back with.
///
/// A refused envelope is one this type holds nothing of. There is no member on it carrying the
/// envelope that was refused, so a caller that meant to read the changes out of it has nothing
/// to read, and the refusal is a property of the type rather than a discipline somebody keeps.
/// It is the same shape <c>DocumentReading</c> takes over a document, for the same reason.
/// </summary>
public sealed class EnvelopeReading
{
    private EnvelopeReading(
        EnvelopeAnswer answer,
        Envelope? envelope,
        int? foundVersion,
        string? missingMember,
        IReadOnlyList<int> supportedVersions)
    {
        Answer = answer;
        Envelope = envelope;
        FoundVersion = foundVersion;
        MissingMember = missingMember;
        SupportedVersions = supportedVersions;
    }

    /// <summary>
    /// Gets what the envelope turned out to be.
    /// </summary>
    public EnvelopeAnswer Answer { get; }

    /// <summary>
    /// Gets the envelope, or null where it was refused or was never one.
    /// </summary>
    public Envelope? Envelope { get; }

    /// <summary>
    /// Gets the version the envelope carried, or null where it carried none.
    /// </summary>
    public int? FoundVersion { get; }

    /// <summary>
    /// Gets the member the envelope's own version requires and did not carry, or null where
    /// that is not what was wrong.
    ///
    /// One member rather than every missing one. The first absence already stops the exchange,
    /// and an envelope missing two members is a peer sending something this plugin cannot use
    /// either way; naming them all would be a longer sentence for the same repair.
    /// </summary>
    public string? MissingMember { get; }

    /// <summary>
    /// Gets the versions this reading was made against, which is what a refusal names.
    ///
    /// It is carried out of the reading as numbers rather than as a sentence assembled here.
    /// Where those numbers are shown to an operator is #62, and that page reads the same
    /// declaration this reading was handed, so the set has one definition and not two.
    /// </summary>
    public IReadOnlyList<int> SupportedVersions { get; }

    /// <summary>
    /// Gets a value indicating whether this reading refuses the envelope.
    ///
    /// Refusing stops the exchange rather than the plugin. <c>docs/transfer.md</c> already fixes
    /// what that costs: the watermark is unmoved, the refusal is recorded with both versions,
    /// and the next exchange asks from the same point.
    /// </summary>
    public bool IsRefused => Answer is not EnvelopeAnswer.Readable;

    internal static EnvelopeReading Readable(
        Envelope envelope,
        IReadOnlyList<int> supportedVersions) =>
        new EnvelopeReading(
            EnvelopeAnswer.Readable,
            envelope,
            envelope.Version,
            null,
            supportedVersions);

    internal static EnvelopeReading VersionNotSupported(
        int foundVersion,
        IReadOnlyList<int> supportedVersions) =>
        new EnvelopeReading(
            EnvelopeAnswer.VersionNotSupported,
            null,
            foundVersion,
            null,
            supportedVersions);

    internal static EnvelopeReading MemberMissing(
        int foundVersion,
        string missingMember,
        IReadOnlyList<int> supportedVersions) =>
        new EnvelopeReading(
            EnvelopeAnswer.MemberMissing,
            null,
            foundVersion,
            missingMember,
            supportedVersions);

    internal static EnvelopeReading NotAnEnvelope(IReadOnlyList<int> supportedVersions) =>
        new EnvelopeReading(EnvelopeAnswer.NotAnEnvelope, null, null, null, supportedVersions);
}

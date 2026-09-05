namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What one attempt to read the body of an envelope off a peer came back with.
///
/// A refused body is one this type holds no text of. There is no member on it carrying the bytes
/// that were refused, so a caller that meant to parse them has nothing to parse, and the refusal
/// is a property of the type rather than a discipline somebody keeps. It is the same shape
/// <see cref="EnvelopeReading"/> takes over an envelope, for the same reason.
///
/// The two refusals by length are told apart rather than collapsed, because what is known about
/// the body differs between them. A peer that declared a length past the bound was refused on its
/// own number and nothing was read; a peer that declared nothing, or declared a length it then
/// exceeded, was refused on the bytes, and what is known is that the body is longer than the
/// bound and never how much longer. A single member holding both would be a number a reader could
/// not tell the meaning of.
/// </summary>
public sealed class EnvelopeBodyReading
{
    private EnvelopeBodyReading(
        EnvelopeBodyAnswer answer,
        string? text,
        long? bound,
        long? declaredBytes,
        long bytesRead)
    {
        Answer = answer;
        Text = text;
        Bound = bound;
        DeclaredBytes = declaredBytes;
        BytesRead = bytesRead;
    }

    /// <summary>
    /// Gets what the body turned out to be.
    /// </summary>
    public EnvelopeBodyAnswer Answer { get; }

    /// <summary>
    /// Gets the body as text, or null where it was refused.
    /// </summary>
    public string? Text { get; }

    /// <summary>
    /// Gets the bound that refused the body, or null where nothing was refused by a bound.
    ///
    /// It is carried out of the reading as a number rather than as a sentence assembled here, for
    /// the reason <see cref="EnvelopeReading.SupportedVersions"/> gives: where a refusal is shown
    /// to an operator is #62, and that page reads the same declaration this rule was held to.
    /// </summary>
    public long? Bound { get; }

    /// <summary>
    /// Gets the length the peer declared, or null where it declared none.
    ///
    /// A declaration is what the peer said about the body rather than what the body is. It is
    /// carried here so a refusal on it can be told from a refusal on the bytes, and it is never
    /// what the body was read to be: the bytes are held to the bound whatever the declaration
    /// said, so a small declaration in front of a large body is refused on the body.
    /// </summary>
    public long? DeclaredBytes { get; }

    /// <summary>
    /// Gets how many bytes were taken off the body.
    ///
    /// Zero where the declaration alone refused it, which is the reading that says nothing was
    /// read into memory to discover the size. One past
    /// <see cref="EnvelopeBounds.MaximumBytes"/> where the bytes refused it, which is the point
    /// the read stopped at rather than the length of the body.
    /// </summary>
    public long BytesRead { get; }

    /// <summary>
    /// Gets a value indicating whether this reading refuses the body.
    ///
    /// Refusing stops the exchange rather than the plugin, which is the cost
    /// <c>docs/transfer.md</c> already fixes for a refused envelope: the watermark is unmoved and
    /// the next exchange asks from the same point.
    /// </summary>
    public bool IsRefused => Answer is not EnvelopeBodyAnswer.Read;

    internal static EnvelopeBodyReading ReadAs(string text, long? declaredBytes, long bytesRead) =>
        new EnvelopeBodyReading(EnvelopeBodyAnswer.Read, text, null, declaredBytes, bytesRead);

    internal static EnvelopeBodyReading TooManyBytes(
        long bound,
        long? declaredBytes,
        long bytesRead) =>
        new EnvelopeBodyReading(
            EnvelopeBodyAnswer.TooManyBytes,
            null,
            bound,
            declaredBytes,
            bytesRead);

    internal static EnvelopeBodyReading NotText(long? declaredBytes, long bytesRead) =>
        new EnvelopeBodyReading(EnvelopeBodyAnswer.NotText, null, null, declaredBytes, bytesRead);
}

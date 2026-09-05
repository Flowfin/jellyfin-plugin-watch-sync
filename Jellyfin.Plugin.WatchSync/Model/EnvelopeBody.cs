using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// Reads the body of an envelope off a peer without reading past what it may carry, which is the
/// second condition of #19.
///
/// <see cref="EnvelopeBounds"/> declares four bounds and judges four numbers a caller hands it.
/// Three of the four are answerable only once the members of an envelope exist, so a caller that
/// asks all four at once has already read the whole body to be able to ask, and an oversized
/// envelope has been taken into memory to discover its size. This is the one bound that can be
/// asked before that, asked at the one moment it can be asked.
///
/// Two orders of refusal, and the difference between them is what a peer said versus what it
/// sent. Where the transport hands over a declared length, the declaration is judged first and
/// nothing is read from the stream at all. Where it does not, or where the declaration is inside
/// the bound and the body is not, the read itself is bounded: it stops one byte past the bound
/// and refuses, so what is allocated is the bound and never the body.
///
/// A declaration is never believed in the direction that would let something through. It can
/// refuse a body, because a peer claiming more than the bound has already said it will not be
/// read, and it cannot admit one, because the bytes are held to the bound whatever the number in
/// front of them said.
///
/// WHAT THIS DOES NOT DO. It does not say the body is an envelope; what the text turns out to be
/// is <see cref="Envelope.Read"/>'s answer, and the three bounds over the members are the
/// caller's to ask after that. It does not refuse a peer that declared a length its body then
/// disagreed with while both sit inside the bound: a wrong declaration is worth knowing about and
/// is not this bound's subject, and refusing on it here would refuse an honest transport that
/// declares an upper bound rather than a length. And it leaves a byte order mark in the text it
/// answers with rather than stripping one, so a body carrying one is refused by the parse as not
/// an envelope; stripping bytes off a peer's body before anybody has decided what they are is the
/// repair that hides which side is wrong.
/// </summary>
public static class EnvelopeBody
{
    /// <summary>
    /// The buffer this starts with, in bytes.
    ///
    /// It grows by doubling up to one byte past the bound rather than starting there, because a
    /// body of ten bytes and a body of a quarter of a mebibyte arrive on the same path and
    /// allocating for the larger one every time is a cost paid on every exchange to save an
    /// allocation on the rare one. What the growth may never do is pass the cap, which is what
    /// makes the bound the ceiling on what a peer can make this side hold.
    /// </summary>
    internal const int FirstBufferBytes = 4096;

    private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Reads the body, refusing it before it is in memory where it is past the bound.
    /// </summary>
    /// <param name="body">The bytes of the body, as the transport hands them over.</param>
    /// <param name="declaredBytes">
    /// How many bytes the transport says the body is, or null where it says nothing. A transport
    /// that carries a length is the case this rule exists for, because it is the only one in
    /// which nothing has to be read to refuse.
    /// </param>
    /// <returns>What the body is, and the text where it may be read.</returns>
    /// <exception cref="ArgumentNullException">The body is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A declared length below zero. It is not a quantity that can be negative, so it is a caller
    /// that computed it wrongly rather than a peer that sent something, and a rule that let it
    /// through would read the body as though nothing had been declared.
    /// </exception>
    public static EnvelopeBodyReading Read(Stream body, long? declaredBytes)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (declaredBytes is long declared)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(declared, nameof(declaredBytes));

            var judged = EnvelopeBounds.Judge(0, declared, 0, 0);

            if (judged.Answer is EnvelopeBoundsAnswer.TooManyBytes)
            {
                return EnvelopeBodyReading.TooManyBytes(EnvelopeBounds.MaximumBytes, declared, 0);
            }
        }

        var bound = EnvelopeBounds.MaximumBytes;
        var cap = bound + 1;
        var buffer = new byte[Math.Min(FirstBufferBytes, cap)];
        var filled = 0;

        while (filled < cap)
        {
            if (filled == buffer.Length)
            {
                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, cap));
            }

            var read = body.Read(buffer, filled, buffer.Length - filled);

            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        if (filled > bound)
        {
            return EnvelopeBodyReading.TooManyBytes(bound, declaredBytes, filled);
        }

        try
        {
            return EnvelopeBodyReading.ReadAs(
                _strictUtf8.GetString(buffer, 0, filled),
                declaredBytes,
                filled);
        }
        catch (DecoderFallbackException)
        {
            return EnvelopeBodyReading.NotText(declaredBytes, filled);
        }
    }
}

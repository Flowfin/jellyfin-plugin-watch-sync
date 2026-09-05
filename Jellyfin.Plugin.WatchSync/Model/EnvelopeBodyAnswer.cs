namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What reading the body of an envelope off a peer's stream answered.
///
/// Three values, two of which are refusals, and each refusal is its own value rather than one
/// code with a detail beside it, which is the rule <see cref="EnvelopeAnswer"/> and
/// <see cref="EnvelopeBoundsAnswer"/> are both written under. What an operator does next differs
/// per answer: a body past the bound is a peer sending more than this plugin agreed to hold, and
/// bytes that are not text at all are a transport to look at rather than a peer to talk to.
/// </summary>
public enum EnvelopeBodyAnswer
{
    /// <summary>
    /// The body is inside the byte bound and is text.
    ///
    /// It is not a statement that the text is an envelope. What the text turns out to be is
    /// <see cref="Envelope.Read"/>'s answer, and the three bounds that need the members of an
    /// envelope to exist are asked after that rather than here.
    /// </summary>
    Read,

    /// <summary>
    /// The body is past <see cref="EnvelopeBounds.MaximumBytes"/>.
    ///
    /// It is the same bound <see cref="EnvelopeBounds.Judge"/> answers, asked at the one moment
    /// it can be asked without the body already being in memory. Where the peer declared a length
    /// past the bound, nothing was read at all; where it did not, one byte past the bound is what
    /// was read and the body is at least that long rather than exactly that long.
    /// </summary>
    TooManyBytes,

    /// <summary>
    /// The bytes are not text this plugin can read, which today means they are not UTF-8.
    ///
    /// The decoding refuses rather than replacing what it cannot decode. A replacement character
    /// substituted for a byte a peer sent is this plugin inventing a character and then reading
    /// its own invention, and the refusal it hides is the one that says which side is wrong.
    /// </summary>
    NotText,
}

namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What a point offered as a watermark turned out to be.
///
/// The point is a value the far side produced, so it arrives here the way every other
/// peer-controlled string does, as bytes to be judged rather than as a value to be trusted. It
/// is refused rather than repaired, which is the opposite of what <see cref="Peer.PeerText"/>
/// does to a value on its way to a page, and the difference is what the value is for: a name
/// shown to an operator is still that name with an invisible character taken out of it, and a
/// watermark with one character taken out of it is a different point that the far side will not
/// recognise. Stripping one here would produce a resume that silently asks the wrong question.
/// </summary>
public enum WatermarkAnswer
{
    /// <summary>
    /// The point is one this record may carry.
    /// </summary>
    Readable,

    /// <summary>
    /// The point is empty, or is nothing but white space.
    ///
    /// Separate from the two below because it is the one a caller produces rather than a peer:
    /// an answer that named no point, handed on as though it had named one. The record already
    /// has a way of saying that nothing has been confirmed, which is
    /// <see cref="Watermark.NoneYet"/>, and it is not this.
    /// </summary>
    NoPointAtAll,

    /// <summary>
    /// The point is longer than a watermark may be.
    ///
    /// The bound is the one every string in an envelope is held to, because the point arrives in
    /// one and a second bound would be a second answer to one question.
    /// </summary>
    TooLong,

    /// <summary>
    /// The point carries a character that is not printable text.
    ///
    /// A control character, a format character, or a line or paragraph separator. A watermark is
    /// written into this plugin's store and read back out of it, and it is shown to an operator
    /// on the status page beside the pairing it belongs to, so a point carrying one of those is
    /// a peer choosing what a later line of that page looks like.
    /// </summary>
    NotPlainText,
}

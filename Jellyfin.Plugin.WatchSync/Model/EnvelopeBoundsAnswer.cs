namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What the bounds on one envelope answered.
///
/// Five values, four of which are refusals, and each refusal is its own value rather than one
/// shared code with a detail beside it. That is #19's own rule and the reason is what an
/// operator does next: a peer sending too many changes has a library or a sweep to look at, a
/// peer sending too many bytes for the same number of changes has something wrong with what it
/// is putting in them, a peer sending an overlong string is sending something nobody wrote by
/// hand, and a peer arriving too often is a schedule or a loop. One code for all four is a
/// refusal that names the wall rather than the thing that hit it.
///
/// None of them is a truncation. A silently shortened envelope is a partial sync nobody knows
/// happened, and the whole point of a bound stated in two layers is that the answer can be
/// reasoned about afterwards.
/// </summary>
public enum EnvelopeBoundsAnswer
{
    /// <summary>
    /// Every bound holds, so nothing here refuses the envelope.
    ///
    /// It is not a statement that the envelope is good. What it carries is judged after it is
    /// read, by the version in #18 and by the rules the changes then reach.
    /// </summary>
    Within,

    /// <summary>
    /// This peer has already sent as many envelopes inside the window as it may.
    ///
    /// It is answered before anything about the envelope is looked at, because a peer over its
    /// rate is refused whatever it is carrying and reading the envelope to find that out is
    /// the work the bound exists to avoid.
    /// </summary>
    TooManyEnvelopesInTheWindow,

    /// <summary>
    /// The envelope declares more bytes than one may carry.
    ///
    /// It is answered on the declared length rather than on the bytes, so a caller that asks
    /// this first has refused before allocating, which is what #19's second condition is about.
    /// </summary>
    TooManyBytes,

    /// <summary>
    /// The envelope carries more changes than one may carry.
    /// </summary>
    TooManyChanges,

    /// <summary>
    /// A string field in the envelope is longer than one may be.
    ///
    /// One field is enough to refuse the envelope. A reader that dropped the field and kept the
    /// rest would be truncating, one field at a time, which is the shape this issue refuses.
    /// </summary>
    AStringIsTooLong,
}

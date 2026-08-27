namespace Jellyfin.Plugin.WatchSync.Tests.Harness;

/// <summary>
/// What the link is told to do to one body handed to it.
///
/// A fault is told rather than drawn, because a case that has to be run several times before it
/// meets the state it is about is a case nobody trusts and nobody keeps. Every member here is
/// observable apart from every other one on a single delivery, which is what makes a case name
/// the fault it is about instead of asserting on whatever happened.
/// </summary>
internal enum LinkFault
{
    /// <summary>
    /// The body is carried once, on the next delivery, in the order it was handed over.
    /// </summary>
    None,

    /// <summary>
    /// The body is never carried. Nothing arrives on this delivery or on any later one, which is
    /// what a peer that went away in the middle of an exchange looks like from here.
    /// </summary>
    Drop,

    /// <summary>
    /// The body is held back and carried on the delivery after next. Its order against everything
    /// else is unchanged, so a case using this is about arriving late rather than about arriving
    /// out of order.
    /// </summary>
    Delay,

    /// <summary>
    /// The body is carried twice on one delivery, which is what a retry after an answer that was
    /// lost rather than never sent looks like to the receiver.
    /// </summary>
    Duplicate,

    /// <summary>
    /// The body is carried after the next body the same side hands over, on the same delivery.
    /// Where nothing follows it, it is carried in place, because a body cannot be reordered
    /// against something that does not exist.
    /// </summary>
    Reorder,
}

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// Why a mapped user and an item are not a transfer subject.
///
/// The reason is a value rather than a flag because what an operator can do about it
/// differs per member. A series that will never be carried and a music track that is not
/// carried in this version are opposite statements, and one word covering both would send
/// somebody to repair a library that is already right.
///
/// The five that name a disposition are the five words <c>docs/matching.md</c> uses for a
/// kind it gives no key rule to. A seventh disposition added to that table is a member
/// added here, and <c>TransferSubjectTests</c> refuses the two sets disagreeing.
/// </summary>
public enum TransferSubjectRefusal
{
    /// <summary>
    /// Nothing was refused. The pair is a transfer subject.
    /// </summary>
    None,

    /// <summary>
    /// No mapped user was named. A change belongs to the person the pairing plugin mapped,
    /// and a subject with no user is one whose watch state has nowhere to land.
    /// </summary>
    NoMappedUser,

    /// <summary>
    /// No item was named. Nothing addresses the work on this server, so there is nothing
    /// for a key derivation to read or for an apply to write to.
    /// </summary>
    NoItem,

    /// <summary>
    /// The kind is an aggregate: a series, a season, a collection or a playlist. The server
    /// derives its played state from the leaf items under it rather than storing it, so a
    /// carried aggregate has no place to land and applying one means marking every leaf the
    /// peer holds under it, including the ones the sender does not have. That is the mass
    /// marking this refusal exists against.
    /// </summary>
    KindIsAnAggregate,

    /// <summary>
    /// The kind is a container: a folder or a view. It holds no watch state of its own.
    /// </summary>
    KindIsAContainer,

    /// <summary>
    /// The kind is a facet: a genre, a studio, a person, a year. It is a way of grouping
    /// items rather than something a person watches.
    /// </summary>
    KindIsAFacet,

    /// <summary>
    /// The kind is ephemeral: live television and channel content, where the two servers do
    /// not hold the same instances and a subject over one would name a different thing on
    /// each side.
    /// </summary>
    KindIsEphemeral,

    /// <summary>
    /// The kind is deferred: a real work that this version does not carry watch state for.
    /// Widening the set of media classes is decision 2 in #1, and this is the refusal that
    /// ends on the day it is answered rather than one that is permanent.
    /// </summary>
    KindIsDeferred,

    /// <summary>
    /// The kind is one this version has no answer for, which is what a kind added to the
    /// server after this code was written looks like. It is refused rather than treated as
    /// a leaf, because the safe answer for an unknown kind is the one that moves nothing.
    /// </summary>
    KindIsUnknownToThisVersion,
}

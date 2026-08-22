using System;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// The unit one transfer is about: one mapped user and one leaf item.
///
/// <c>docs/sync-model.md</c> fixes the unit under <c>## The unit a transfer is about</c> and
/// this type is what makes it structural. There is no public constructor, so the only route
/// to an instance is <see cref="From"/>, and that route answers a reading rather than a
/// subject for every kind <c>docs/matching.md</c> gives no key rule to. An aggregate is
/// therefore refused by construction: a caller that wanted to carry a series has nothing to
/// put a series into, whatever it intends.
///
/// The failure that is worth the type is the one the prior art keeps producing. A server
/// does not store the played state of a series; it derives it from the episodes under it at
/// the moment it is asked. So a carried series-played has no place to land, and the only way
/// to apply one is to mark every episode the peer holds under that series, including the
/// episodes the peer has and the sender does not. One watched series becomes a library of
/// history nobody made. A filter somewhere on the send path would refuse the same thing
/// while it was remembered; this refuses it in the type the send path is written in terms
/// of.
///
/// It carries no watch state. What moves is <see cref="SyncedState"/>, and keeping the two
/// apart is what lets the subject be decided once, at the boundary where an item is read,
/// rather than being re-derived beside every field.
/// </summary>
public sealed class TransferSubject
{
    private TransferSubject(Guid mappedUserId, Guid itemId, BaseItemKind kind)
    {
        MappedUserId = mappedUserId;
        ItemId = itemId;
        Kind = kind;
    }

    /// <summary>
    /// Gets the mapped user this transfer is about, as this server names them.
    ///
    /// It is the local user the pairing plugin's mapping names, never one this plugin found
    /// by comparing anything, which is the inference #42 refuses.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets the item this transfer is about, as this server names it.
    ///
    /// It addresses the item here and names nothing on the peer. What travels between two
    /// servers is the match key, which is #22 and #23.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the kind of the item, which is always one <c>docs/matching.md</c> gives a key
    /// rule to.
    ///
    /// It is carried rather than dropped because the key derivation differs per kind, and a
    /// caller that had to ask the library again for something already read on the way in
    /// would be a second read that can disagree with the first.
    /// </summary>
    public BaseItemKind Kind { get; }

    /// <summary>
    /// Reads a mapped user and an item as a transfer subject, or answers why they are not
    /// one.
    ///
    /// The two identifiers are judged before the kind. An empty identifier is a caller that
    /// asked about nothing, which is a mistake one step earlier than the one this type is
    /// about, and reporting the kind for it would send a reader at the library instead of at
    /// the call.
    /// </summary>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <param name="itemId">The item, as this server names it.</param>
    /// <param name="kind">The kind of the item, as the server classified it.</param>
    /// <returns>The subject, or the reason there is none.</returns>
    public static TransferSubjectReading From(Guid mappedUserId, Guid itemId, BaseItemKind kind)
    {
        if (mappedUserId == Guid.Empty)
        {
            return TransferSubjectReading.Refused(TransferSubjectRefusal.NoMappedUser);
        }

        if (itemId == Guid.Empty)
        {
            return TransferSubjectReading.Refused(TransferSubjectRefusal.NoItem);
        }

        var refusal = RefusalFor(kind);

        return refusal == TransferSubjectRefusal.None
            ? TransferSubjectReading.Subject(new TransferSubject(mappedUserId, itemId, kind))
            : TransferSubjectReading.Refused(refusal);
    }

    /// <summary>
    /// What a kind on its own is refused for, and <see cref="TransferSubjectRefusal.None"/>
    /// where it is a leaf item.
    ///
    /// The arms are the table in <c>docs/matching.md</c>, one arm per disposition rather than
    /// one per kind, and <c>TransferSubjectTests</c> drives every member of the server's own
    /// enumeration through this and refuses an answer that table disagrees with. So a kind
    /// moved from one disposition to another in that document reddens the suite rather than
    /// leaving this quietly answering what used to be true.
    ///
    /// The last arm is the kind this version has never heard of. It answers refused rather
    /// than throwing, because a library holding a kind added upstream is a library this
    /// plugin should walk past rather than a server it should fault on.
    /// </summary>
    /// <param name="kind">The kind of the item.</param>
    /// <returns>The refusal, or none.</returns>
    private static TransferSubjectRefusal RefusalFor(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Episode or BaseItemKind.Movie =>
            TransferSubjectRefusal.None,

        BaseItemKind.BoxSet or BaseItemKind.MusicAlbum or BaseItemKind.Playlist
            or BaseItemKind.Season or BaseItemKind.Series =>
            TransferSubjectRefusal.KindIsAnAggregate,

        BaseItemKind.AggregateFolder or BaseItemKind.BasePluginFolder
            or BaseItemKind.ChannelFolderItem or BaseItemKind.CollectionFolder
            or BaseItemKind.Folder or BaseItemKind.ManualPlaylistsFolder
            or BaseItemKind.PhotoAlbum or BaseItemKind.PlaylistsFolder
            or BaseItemKind.UserRootFolder or BaseItemKind.UserView =>
            TransferSubjectRefusal.KindIsAContainer,

        BaseItemKind.Genre or BaseItemKind.MusicArtist or BaseItemKind.MusicGenre
            or BaseItemKind.Person or BaseItemKind.Studio or BaseItemKind.Year =>
            TransferSubjectRefusal.KindIsAFacet,

        BaseItemKind.Channel or BaseItemKind.LiveTvChannel or BaseItemKind.LiveTvProgram
            or BaseItemKind.Program or BaseItemKind.Recording or BaseItemKind.TvChannel
            or BaseItemKind.TvProgram =>
            TransferSubjectRefusal.KindIsEphemeral,

        BaseItemKind.Audio or BaseItemKind.AudioBook or BaseItemKind.Book
            or BaseItemKind.MusicVideo or BaseItemKind.Photo or BaseItemKind.Trailer
            or BaseItemKind.Video =>
            TransferSubjectRefusal.KindIsDeferred,

        _ => TransferSubjectRefusal.KindIsUnknownToThisVersion,
    };
}

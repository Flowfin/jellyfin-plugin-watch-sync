using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Records;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// What this plugin's store holds, declared in one place.
///
/// #74 asks that the report of everything held about one person be driven by the store's own
/// type list, so that a record kind added later cannot be missed, and its removal asks that a
/// scan afterwards find nothing naming that person in any document. Both of those readings are
/// worth exactly what the list they walk is worth: a report that enumerates kinds by hand proves
/// the hand-written list rather than the store, and it looks complete on the day it is written.
/// The reading on #74 places that property here rather than there, because it is a property of
/// how the store declares what it holds, and this is that declaration.
///
/// <para>
/// It is a list a person maintains and it is closed against the tree in both directions.
/// <c>StoredKindsTests</c> finds every type in this plugin that writes a document and refuses one
/// that is not named here, and refuses an entry naming a type that does not write one. So the
/// cost of forgetting is a red suite at the moment the third kind is written rather than a
/// silence a person discovers when they ask what is held about them and are handed two thirds of
/// it.
/// </para>
///
/// <para>
/// The prefixes are what makes a name readable back into a kind. Every document is named for what
/// it is about rather than counted, so the identifiers that follow a prefix say which pairing and
/// which person, and a walk over the store can therefore answer both of #74's operations without
/// opening a document to find out what it is. That is why the prefixes are held distinct, and
/// held so that none is a prefix of another: two kinds whose names begin alike would make one
/// kind's documents readable as the other's, and a removal driven by that reading would either
/// miss documents or delete somebody else's.
/// </para>
///
/// <para>
/// What this does not do: nothing walks the store yet. There is no enumeration of the documents
/// in the folder, no report and no removal, and this is the declaration those will be written
/// against rather than a claim that they exist. The operations themselves are #74 and sit behind
/// the administrator surface in #57 and the authorisation in #66.
/// </para>
/// </summary>
public static class StoredKinds
{
    /// <summary>
    /// Gets every kind of document this plugin's store holds.
    ///
    /// Five today. What each of them is for is argued at the type that declares it rather than
    /// restated here, because a description kept beside a list is the drift this list exists to
    /// refuse, one level in.
    /// </summary>
    public static IReadOnlyList<StoredKind> All { get; } = new[]
    {
        new StoredKind(AgreedRecords.NamePrefix, typeof(AgreedRecords)),
        new StoredKind(ConflictRecords.NamePrefix, typeof(ConflictRecords)),
        new StoredKind(ProvenanceRecords.NamePrefix, typeof(ProvenanceRecords)),
        new StoredKind(UnmatchedRecords.NamePrefix, typeof(UnmatchedRecords)),
        new StoredKind(StoppedRun.NamePrefix, typeof(StoppedRun)),
    };
}

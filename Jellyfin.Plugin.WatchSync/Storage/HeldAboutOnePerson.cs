using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Document;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// What this plugin's store holds about one person, and the removal of it, which is #74.
///
/// A person who asks what is held about them, or asks for it to go, has to be answerable without
/// an administrator reading files by hand. This is the rule that answers both, over every pairing
/// at once, because a person asks about themselves rather than about a pairing they have never
/// heard of.
///
/// <para>
/// It is driven by <see cref="StoredKinds.All"/> and never by a list of its own. #74's second
/// condition asks exactly that, so a kind added later cannot be missed, and the reason is that a
/// walk enumerating kinds by hand proves the hand-written list rather than the store and looks
/// complete on the day it is written. The declaration is closed against the tree in both
/// directions by <c>StoredKindsTests</c>, so what this walks is what the store holds.
/// </para>
///
/// <para>
/// A name that belongs to no kind is left alone, in both operations. The store folder is a folder
/// on somebody's server: a file a write left in flight sits there, an operator may have copied
/// something in, and a removal that deleted what it could not name would be deleting on the
/// strength of not recognising it. What that costs is stated rather than hidden: a document this
/// plugin wrote under a naming scheme an older version used would not be recognised, would not be
/// reported, and would not be removed. Nothing has shipped, so no such scheme exists yet, and the
/// day one does it is a migration in #71 rather than a case for this walk to guess at.
/// </para>
///
/// <para>
/// Neither operation touches the server's own user data, which is #74's fourth condition. Nothing
/// here takes the adapter or reaches the server at all: what a person watched belongs to the
/// server and stays there, and removing this plugin's records about somebody is not removing
/// their watch history. That sentence is the deliverable of #74 rather than a note around it, and
/// it is in the privacy note as well, which is #107.
/// </para>
/// </summary>
public static class HeldAboutOnePerson
{
    /// <summary>
    /// How many characters a document name carries per identifier.
    ///
    /// Every kind names its documents with two identifiers in the same spelling, without hyphens
    /// and lower case, which is what the store composes a path out of. The length is what lets a
    /// name be read back without knowing which kind wrote it.
    ///
    /// What the comparison against it does is make the two substrings safe to take. What refuses
    /// a name of the wrong shape is the pair of parses, and the two are not independent: a name
    /// carrying a third identifier is refused by the length before a parse sees it, and by the
    /// parse of the rest if the length is written as at least rather than exactly. That was run
    /// rather than reasoned about, and the near miss for it stays green until both are weakened.
    /// </summary>
    private const int IdentifierLength = 32;

    /// <summary>
    /// Every document the store holds about one person.
    /// </summary>
    /// <param name="store">The store to walk.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>The documents, in no promised order.</returns>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    /// <exception cref="ArgumentException">The person is nobody.</exception>
    public static IReadOnlyList<HeldDocument> Held(DocumentStore store, Guid mappedUserId)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (mappedUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "A walk over the store about nobody would answer with every document naming an empty identifier, and a removal driven by it would delete them.",
                nameof(mappedUserId));
        }

        var held = new List<HeldDocument>();

        foreach (var name in store.Names())
        {
            if (Reads(name, out var document) && document!.MappedUserId == mappedUserId)
            {
                held.Add(document);
            }
        }

        return held;
    }

    /// <summary>
    /// Everything the store holds about one person, as the documents themselves.
    ///
    /// The document is read after the name has put it in scope, so a document about somebody else
    /// is never opened on the way to answering about this person.
    ///
    /// <para>
    /// A document that is gone between the walk and the read is left out rather than answered as
    /// an absence, and one this code may not read is left out too. Both are the store answering
    /// that there is nothing to hand over under that name, and a report that carried an entry
    /// with no contents would be handing somebody a row saying that something about them exists
    /// and cannot be shown.
    /// </para>
    /// </summary>
    /// <param name="store">The store to read.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>Each document that is about the person, with what it holds.</returns>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    /// <exception cref="ArgumentException">The person is nobody.</exception>
    public static IReadOnlyList<KeyValuePair<HeldDocument, StoredDocument>> Report(
        DocumentStore store,
        Guid mappedUserId)
    {
        var report = new List<KeyValuePair<HeldDocument, StoredDocument>>();

        foreach (var held in Held(store, mappedUserId))
        {
            var reading = store.Read(held.Name);

            if (reading?.Document is StoredDocument document)
            {
                report.Add(new KeyValuePair<HeldDocument, StoredDocument>(held, document));
            }
        }

        return report;
    }

    /// <summary>
    /// Removes every document the store holds about one person, and answers how many went.
    ///
    /// The count is what #74 asks be recorded, and it is the count of documents that were removed
    /// rather than of documents that were found. A document that had gone between the walk and
    /// the removal is not counted, because counting it would tell somebody this plugin deleted
    /// something it did not.
    ///
    /// <para>
    /// A filesystem that refuses a removal leaves that refusal here rather than being counted as
    /// a document that was not there. Somebody told their record is gone has been told something
    /// specific, and a removal that swallowed a refusal would be telling them that about a file
    /// still on the disk.
    /// </para>
    ///
    /// <para>
    /// WHAT SEPARATES REMOVED FROM FOUND IS NOT PROVEN AND THIS SAYS SO. The two counts differ
    /// only for a document that goes between the walk and the removal, and both are inside one
    /// call here, so nothing in a headless suite can force that interleaving without a seam this
    /// type would carry for no caller. Taking the condition off the increment, so that the count
    /// is the count of documents found, reddens nothing. What the facts do prove is that a
    /// removal over a store holding nothing about the person answers zero, and that the documents
    /// counted are gone from the store afterwards; the arm that discriminates is carried on the
    /// argument above rather than on a run, and a later editor moving it will not be caught.
    /// </para>
    /// </summary>
    /// <param name="store">The store to remove from.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>How many documents were removed.</returns>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    /// <exception cref="ArgumentException">The person is nobody.</exception>
    public static int Remove(DocumentStore store, Guid mappedUserId)
    {
        var removed = 0;

        foreach (var held in Held(store, mappedUserId))
        {
            if (store.Remove(held.Name))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Reads one document name back into the kind, the pairing and the person it is about.
    ///
    /// The prefixes are held so that none is a prefix of another, which <c>StoredKindsTests</c>
    /// refuses the opposite of, so the first kind whose prefix a name begins with is the only one
    /// it could be. Without that, one kind's documents would be readable as another's and a
    /// removal driven by the reading would either miss documents or delete somebody else's.
    /// </summary>
    /// <param name="name">The document's name, without a suffix.</param>
    /// <param name="document">What the name turned out to be about.</param>
    /// <returns>Whether the name is one this plugin composed.</returns>
    private static bool Reads(string name, out HeldDocument? document)
    {
        document = null;

        foreach (var kind in StoredKinds.All)
        {
            if (!name.StartsWith(kind.NamePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = name.Substring(kind.NamePrefix.Length);

            if (rest.Length != (IdentifierLength * 2) + 1
                || rest[IdentifierLength] != '-'
                || !Guid.TryParseExact(rest.Substring(0, IdentifierLength), "N", out var pairingId)
                || !Guid.TryParseExact(
                    rest.Substring(IdentifierLength + 1),
                    "N",
                    out var mappedUserId))
            {
                return false;
            }

            document = new HeldDocument(name, kind, pairingId, mappedUserId);

            return true;
        }

        return false;
    }
}

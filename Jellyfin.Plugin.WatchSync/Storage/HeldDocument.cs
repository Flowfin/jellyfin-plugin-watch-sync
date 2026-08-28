using System;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// One document in this plugin's store, read back out of its own name.
///
/// Every document is named for what it is about rather than counted, which is the shape
/// <see cref="StoredKinds"/> argues: the prefix says which kind it is and the two identifiers
/// after it say which pairing and which person. So the question #74 asks, what is held about one
/// person, is answered by reading names rather than by opening documents, and a person is never
/// told about a document that turned out to be somebody else's because it was opened to find out.
///
/// <para>
/// It carries no contents. What is held about somebody is handed over as the documents
/// themselves, read through the store, and a type that carried both the name and the bytes would
/// make the walk that decides which documents are in scope hold copies of what everybody watched
/// while it decided. The report reads a document once it is in scope and not before.
/// </para>
/// </summary>
public sealed class HeldDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeldDocument"/> class.
    /// </summary>
    /// <param name="name">The document's name in the store, without a suffix.</param>
    /// <param name="kind">Which kind of document it is.</param>
    /// <param name="pairingId">The pairing it is about.</param>
    /// <param name="mappedUserId">The person it is about, as this server names them.</param>
    /// <exception cref="ArgumentNullException">The name or the kind is null.</exception>
    internal HeldDocument(string name, StoredKind kind, Guid pairingId, Guid mappedUserId)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(kind);

        Name = name;
        Kind = kind;
        PairingId = pairingId;
        MappedUserId = mappedUserId;
    }

    /// <summary>
    /// Gets the document's name in the store, without a suffix.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets which kind of document it is, as the store declares its kinds.
    /// </summary>
    public StoredKind Kind { get; }

    /// <summary>
    /// Gets the pairing this document is about.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the person this document is about, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }
}

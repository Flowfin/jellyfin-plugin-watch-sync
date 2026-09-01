using System;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// One document this plugin's store holds about a person.
/// </summary>
public sealed class HeldRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeldRecord"/> class.
    /// </summary>
    /// <param name="name">The document's name in the store.</param>
    /// <param name="kind">The prefix of the kind that wrote it.</param>
    /// <param name="pairingId">The pairing the document is about.</param>
    /// <param name="version">The version the document was written at.</param>
    /// <param name="document">The document, as the store holds it.</param>
    public HeldRecord(string name, string kind, Guid pairingId, int version, string document)
    {
        Name = name;
        Kind = kind;
        PairingId = pairingId;
        Version = version;
        Document = document;
    }

    /// <summary>
    /// Gets the document's name in the store.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the prefix of the kind that wrote it, which is what names the record type without
    /// this file carrying a list of them.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the pairing the document is about.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the version the document was written at.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the document as the store holds it, which is the whole of what is held rather than a
    /// selection made here.
    /// </summary>
    public string Document { get; }
}

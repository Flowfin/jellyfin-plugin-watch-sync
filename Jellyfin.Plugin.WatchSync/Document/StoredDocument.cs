using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// One document in this plugin's store, as a version and the members that sit beside it.
///
/// Every document this plugin keeps carries a version, and this type is what that sentence
/// means in the tree. The store folder is #68 and answers where a document sits; this answers
/// what one is, which is a whole number above zero under <c>version</c> and whatever else the
/// document that carried it holds.
///
/// The members are kept as they were read rather than as a shape this code declares, and that
/// is the whole of the fourth rule in #69. A reader that deserializes into a type of its own
/// and serializes that type back drops every member the type does not declare, so an older
/// version reading a document it is allowed to read still destroys what a newer version put
/// there. Nothing is dropped here because nothing is interpreted here: a caller reads the
/// members it knows out of <see cref="Fields"/> and writes back the same object, and the
/// members it never looked at are still in it.
///
/// What is not here is the writing. Where a document is written, and how a write survives a
/// kill in the middle of it, is #70, and this type produces the bytes rather than putting them
/// anywhere.
/// </summary>
public sealed class StoredDocument
{
    /// <summary>
    /// The one member this type reads. Everything else is carried and not interpreted.
    /// </summary>
    internal const string VersionMember = "version";

    private readonly JsonObject _fields;

    private StoredDocument(int version, JsonObject fields)
    {
        Version = version;
        _fields = fields;
    }

    /// <summary>
    /// Gets the version the document carries.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the members of the document other than its version.
    ///
    /// It is the object that was read, so a member this code has never heard of is in it and
    /// stays in it. A caller that removes one is deleting it on purpose, which is a different
    /// thing from a reader that never knew it was there.
    /// </summary>
    public JsonObject Fields => _fields;

    /// <summary>
    /// A document at a version, out of members that have already been read.
    ///
    /// This is what an upgrade answers with, and it is deliberately not a way to parse anything:
    /// the members arrive as an object rather than as text, so the only route from bytes to a
    /// document stays <see cref="Read"/> and the version decision cannot be walked around by
    /// assembling one here.
    /// </summary>
    /// <param name="version">The version the document is at.</param>
    /// <param name="fields">The members beside the version.</param>
    /// <returns>The document.</returns>
    internal static StoredDocument At(int version, JsonObject fields) =>
        new StoredDocument(version, fields);

    /// <summary>
    /// Reads the version off a document and decides whether it may be read at all.
    ///
    /// Nothing is deserialized into a shape before the version has been decided on, because
    /// deciding afterwards is deciding too late: a document from the future has already been
    /// read into a type that does not know half of it by the time anything compares two
    /// numbers.
    /// </summary>
    /// <param name="json">The bytes of the document, as text.</param>
    /// <param name="versionThisCodeWrites">
    /// The version this code writes. Where the versions are declared, and how a document three
    /// of them behind is carried forward one step at a time, is #71; this rule is handed the
    /// number rather than reaching for it, so it takes no decision about where it lives.
    /// </param>
    /// <returns>What the document is, and the document itself where it may be read.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A version this code writes that is not a whole number above zero, which is a defect in
    /// the caller rather than a state a document can be in.
    /// </exception>
    public static DocumentReading Read(string json, int versionThisCodeWrites)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentOutOfRangeException.ThrowIfLessThan(versionThisCodeWrites, 1);

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return DocumentReading.NotADocument(versionThisCodeWrites);
        }

        if (parsed is not JsonObject members
            || !members.TryGetPropertyValue(VersionMember, out var member)
            || member is not JsonValue value
            || !value.TryGetValue<int>(out var version)
            || version < 1)
        {
            return DocumentReading.NotADocument(versionThisCodeWrites);
        }

        if (version > versionThisCodeWrites)
        {
            return DocumentReading.FromTheFuture(version, versionThisCodeWrites);
        }

        var fields = new JsonObject();

        foreach (var pair in members)
        {
            if (string.Equals(pair.Key, VersionMember, StringComparison.Ordinal))
            {
                continue;
            }

            fields[pair.Key] = pair.Value?.DeepClone();
        }

        var document = new StoredDocument(version, fields);

        return version == versionThisCodeWrites
            ? DocumentReading.Current(document, versionThisCodeWrites)
            : DocumentReading.OlderThanThisCode(document, versionThisCodeWrites);
    }

    /// <summary>
    /// The bytes this document is written as, with the version first and every member that was
    /// read beside it.
    /// </summary>
    /// <returns>The document as text.</returns>
    public string ToJson()
    {
        var members = new JsonObject
        {
            [VersionMember] = JsonValue.Create(Version),
        };

        foreach (var pair in _fields)
        {
            members[pair.Key] = pair.Value?.DeepClone();
        }

        return members.ToJsonString();
    }
}

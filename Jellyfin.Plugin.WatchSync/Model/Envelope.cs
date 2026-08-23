using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// One envelope from a peer, as a version and the members that sit beside it, which is #18.
///
/// Every envelope this plugin sends or reads carries its own version, and this type is what that
/// sentence means in the tree. <see cref="EnvelopeBounds"/> answers what one envelope may carry
/// and is asked before anything is read, so that a refusal happens before the allocation; this
/// answers what one is, which is a whole number above zero under <c>version</c> and whatever
/// else the envelope that carried it holds.
///
/// The members are kept as they were read rather than as a shape this code declares. Nothing
/// here interprets a change: what a change is is the match key in #22 and #23 beside the fields
/// in <see cref="SyncedState"/>, and reading one is the apply path in #54, none of which is in
/// this tree. Deciding a change's shape here would be this type answering three other issues,
/// and it would answer them in the place hardest to undo.
///
/// One coupling is worth having beside the type rather than being rediscovered. The version is
/// part of whatever the pairing plugin authenticates, so the bytes this reads are bytes that
/// plane has already accepted. That plane is an in-process interface and a test double rather
/// than a local surface, decided as decision 6 on <c>Flowfin/jellyfin-plugin-server-pairing#1</c>,
/// so what reaches here is a body handed over in process. Nothing about that makes the peer
/// trusted: it is a machine this server does not administer, which is the sentence
/// <see cref="EnvelopeBounds"/> opens with, and it is why the version is decided on before
/// anything is deserialized into a shape.
/// </summary>
public sealed class Envelope
{
    /// <summary>
    /// The member the version sits under.
    /// </summary>
    internal const string VersionMember = "version";

    /// <summary>
    /// The member the changes sit under.
    ///
    /// It is declared here and required from <see cref="EnvelopeVersions"/>, so the name has one
    /// spelling and the requirement has one home.
    /// </summary>
    internal const string ChangesMember = "changes";

    private readonly JsonObject _members;

    private Envelope(int version, JsonObject members)
    {
        Version = version;
        _members = members;
    }

    /// <summary>
    /// Gets the version the envelope carries.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the members of the envelope other than its version.
    ///
    /// It is the object that was read, so a member this code has never heard of is in it. That
    /// is not permissiveness: a member nothing reads changes nothing, and the rule this type is
    /// about runs the other way, over the members a version requires and did not get.
    /// </summary>
    public JsonObject Members => _members;

    /// <summary>
    /// Reads the version off an envelope and decides whether it may be read at all.
    ///
    /// Nothing is deserialized into a shape before the version has been decided on, because
    /// deciding afterwards is deciding too late: an envelope from a version this plugin does not
    /// speak has already been read into a type that does not know half of it by the time
    /// anything compares two numbers.
    ///
    /// The required members are looked up from the version the envelope declared rather than
    /// from the newest one this plugin speaks. That is the fourth rule in #18 seen from the
    /// reader's side: an older version this server still supports is read as that version
    /// throughout, so an envelope is never held to a member a later version added.
    /// </summary>
    /// <param name="json">The bytes of the envelope, as text.</param>
    /// <param name="supportedVersions">
    /// The versions this plugin speaks. Where they are declared is <see cref="EnvelopeVersions"/>;
    /// this rule is handed them rather than reaching for them, so it takes no decision about
    /// where the set lives and a caller cannot end up reading one set while a page shows another.
    /// </param>
    /// <returns>What the envelope is, and the envelope itself where it may be read.</returns>
    /// <exception cref="ArgumentNullException">The text or the set is null.</exception>
    /// <exception cref="ArgumentException">
    /// An empty set of supported versions, which is a caller that would refuse every envelope
    /// there is and is a defect one step earlier than anything an envelope can be.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A supported set naming a version <see cref="EnvelopeVersions"/> declares no required
    /// members for. It is unreachable from anything a peer sends, because the version is looked
    /// up only after it has been found in the set the caller declared, so it says the caller's
    /// set and the declaration disagree rather than that an envelope was wrong.
    /// </exception>
    public static EnvelopeReading Read(string json, IReadOnlyList<int> supportedVersions)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(supportedVersions);

        if (supportedVersions.Count == 0)
        {
            throw new ArgumentException(
                "No envelope version is supported, so nothing could be read and the refusal would name nothing.",
                nameof(supportedVersions));
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return EnvelopeReading.NotAnEnvelope(supportedVersions);
        }

        if (parsed is not JsonObject members
            || !members.TryGetPropertyValue(VersionMember, out var member)
            || member is not JsonValue value
            || !value.TryGetValue<int>(out var version)
            || version < 1)
        {
            return EnvelopeReading.NotAnEnvelope(supportedVersions);
        }

        if (!Contains(supportedVersions, version))
        {
            return EnvelopeReading.VersionNotSupported(version, supportedVersions);
        }

        foreach (var required in EnvelopeVersions.Requires(version))
        {
            if (!members.TryGetPropertyValue(required, out var carried) || carried is null)
            {
                return EnvelopeReading.MemberMissing(version, required, supportedVersions);
            }
        }

        var beside = new JsonObject();

        foreach (var pair in members)
        {
            if (string.Equals(pair.Key, VersionMember, StringComparison.Ordinal))
            {
                continue;
            }

            beside[pair.Key] = pair.Value?.DeepClone();
        }

        return EnvelopeReading.Readable(new Envelope(version, beside), supportedVersions);
    }

    private static bool Contains(IReadOnlyList<int> versions, int version)
    {
        for (var i = 0; i < versions.Count; i++)
        {
            if (versions[i] == version)
            {
                return true;
            }
        }

        return false;
    }
}

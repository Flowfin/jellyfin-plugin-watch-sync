using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What this assembly does when it is run rather than when it is tested.
///
/// #70's first condition is that a process is killed during a write, in a loop, and that every
/// survivor is a readable document. A process that is killed has to be a process, and it has to
/// be one that dies without unwinding, so it cannot be the test host: killing that kills the run.
/// This entry point is the writer that gets killed, and it lives in the suite's own assembly
/// because the write it performs has to be the one the plugin ships rather than a second one
/// arranged to be killable.
///
/// The test runner never calls it. `dotnet test` loads this assembly through the adapter and
/// starts at the facts, so nothing here runs on an ordinary run of the suite; the project turns
/// off the entry point the test SDK would otherwise generate, and this stands in its place.
/// </summary>
public static class EntryPoint
{
    /// <summary>
    /// The word the writer prints once the store holds a whole document, so the case that kills
    /// it knows there is a survivor to read rather than an empty folder.
    /// </summary>
    internal const string Ready = "ready";

    /// <summary>
    /// The argument that asks this assembly to write documents until it is killed.
    /// </summary>
    internal const string WriteUntilKilled = "write-until-killed";

    /// <summary>
    /// Runs the writer, or does nothing where nobody asked for it.
    /// </summary>
    /// <param name="args">
    /// The verb, the folder standing in for the server's data path, the document's name, and how
    /// many characters of payload each document after the first carries.
    /// </param>
    /// <returns>Zero where the run was asked for something it does not offer.</returns>
    public static int Main(string[] args)
    {
        if (args is null || args.Length != 4 || !string.Equals(args[0], WriteUntilKilled, StringComparison.Ordinal))
        {
            return 0;
        }

        var dataPath = args[1];
        var name = args[2];
        var payload = int.Parse(args[3], CultureInfo.InvariantCulture);

        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(dataPath);

        var store = new DocumentStore(new StoreFolder(paths.Object));

        store.Write(name, _ => KillableDocument.At(1, 1));

        Console.Out.WriteLine(Ready);
        Console.Out.Flush();

        var generation = 2;

        while (true)
        {
            store.Write(name, _ => KillableDocument.At(generation, payload));
            generation++;
        }
    }
}

/// <summary>
/// The document the writer writes, whose contents can be checked against themselves.
///
/// A survivor is only evidence if a mixture of two documents is recognisable, and two documents
/// that differ by one number are not: half of one and half of the other still parses. So the
/// payload is derived from the generation and its length is declared, and a reader that finds a
/// payload disagreeing with either has found bytes from two writes in one file.
/// </summary>
internal static class KillableDocument
{
    /// <summary>
    /// The member carrying which write produced the document.
    /// </summary>
    internal const string GenerationMember = "generation";

    /// <summary>
    /// The member carrying the bytes whose shape the generation decides.
    /// </summary>
    internal const string PayloadMember = "payload";

    /// <summary>
    /// A document at one generation, carrying a payload of the length asked for.
    /// </summary>
    /// <param name="generation">Which write is producing it.</param>
    /// <param name="payload">How many characters the payload carries.</param>
    /// <returns>The document.</returns>
    internal static StoredDocument At(int generation, int payload)
    {
        var members = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
            [GenerationMember] = JsonValue.Create(generation),
            [PayloadMember] = JsonValue.Create(PayloadFor(generation, payload)),
        };

        return StoredDocument.Read(members.ToJsonString(), DocumentVersions.Current).Document!;
    }

    /// <summary>
    /// The payload a generation is required to carry.
    /// </summary>
    /// <param name="generation">Which write produced it.</param>
    /// <param name="payload">How many characters it carries.</param>
    /// <returns>The payload.</returns>
    internal static string PayloadFor(int generation, int payload) =>
        new string((char)('a' + (generation % 26)), payload);

    /// <summary>
    /// Whether a document read back is a whole one of the writer's, rather than two of them.
    /// </summary>
    /// <param name="document">What was read back.</param>
    /// <returns>Whether it agrees with itself.</returns>
    internal static bool AgreesWithItself(StoredDocument document)
    {
        if (document.Fields[GenerationMember] is not JsonValue number
            || !number.TryGetValue<int>(out var generation)
            || document.Fields[PayloadMember] is not JsonValue value
            || !value.TryGetValue<string>(out var payload))
        {
            return false;
        }

        return payload.Length > 0
            && string.Equals(payload, PayloadFor(generation, payload.Length), StringComparison.Ordinal);
    }
}

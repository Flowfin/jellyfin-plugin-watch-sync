using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
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
///
/// <para>The writer stops where it is told and waits to be killed there, rather than writing in
/// a loop until somebody stops it. A kill thrown at a loop lands where the process spends its
/// time, and a write spends most of that time pushing bytes the operating system has already
/// taken, which is after the file is whole; measured against a write path with the atomicity
/// deliberately removed, a raced kill reported a whole document on every round it was tried on.
/// Told where to stop, the writer stops in the middle of the bytes on one round and after the
/// document has been replaced on the next, and the case knows which of the two it asked
/// for.</para>
/// </summary>
public static class EntryPoint
{
    /// <summary>
    /// The word the writer prints once it has reached the moment it was asked to stop at. It
    /// waits there until it is killed.
    /// </summary>
    internal const string Ready = "ready";

    /// <summary>
    /// The argument that asks this assembly to write and then wait to be killed.
    /// </summary>
    internal const string WriteUntilKilled = "write-until-killed";

    /// <summary>
    /// Stop with half the bytes of the second document down and nothing replaced.
    /// </summary>
    internal const string HalfwayThroughTheBytes = "halfway-through-the-bytes";

    /// <summary>
    /// Stop once the second document has replaced the first.
    /// </summary>
    internal const string AfterTheReplace = "after-the-replace";

    /// <summary>
    /// Which write a survivor is compared against, so it says which of the two produced it.
    /// </summary>
    internal const int SecondGeneration = 2;

    /// <summary>
    /// Runs the writer, or does nothing where nobody asked for it.
    /// </summary>
    /// <param name="args">
    /// The verb, the folder standing in for the server's data path, the document's name, how many
    /// characters of payload the second document carries, and where to stop.
    /// </param>
    /// <returns>Zero where the run was asked for something it does not offer.</returns>
    public static int Main(string[] args)
    {
        if (args is null || args.Length != 5 || !string.Equals(args[0], WriteUntilKilled, StringComparison.Ordinal))
        {
            return 0;
        }

        var dataPath = args[1];
        var name = args[2];
        var payload = int.Parse(args[3], CultureInfo.InvariantCulture);
        var halfway = string.Equals(args[4], HalfwayThroughTheBytes, StringComparison.Ordinal);

        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(dataPath);

        var opened = 0;

        var store = new DocumentStore(
            new StoreFolder(paths.Object),
            path => new StopsWhereItIsTold(
                path,
                halfway && Interlocked.Increment(ref opened) == SecondGeneration));

        store.Write(name, _ => KillableDocument.At(1, 1));
        store.Write(name, _ => KillableDocument.At(SecondGeneration, payload));

        // Only reached where the writer was asked to stop after the replace. The other way round
        // it is still inside the second write, holding half the bytes of a document that has
        // replaced nothing.
        Console.Out.WriteLine(Ready);
        Console.Out.Flush();

        WaitToBeKilled();

        return 0;
    }

    /// <summary>
    /// Stands still until somebody stops the process.
    ///
    /// Nothing here waits for a duration. What is being waited for is a kill, which is not a
    /// length of time, and a writer that woke up on its own would move off the moment the case
    /// asked it to stop at.
    /// </summary>
    internal static void WaitToBeKilled()
    {
        using var never = new ManualResetEventSlim(false);

        never.Wait();
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

/// <summary>
/// The file a write in flight goes into, which stops halfway through the bytes where it is asked
/// to and waits there.
///
/// It is a file stream rather than something wrapping one so that the write path treats it as the
/// file it is, including the flush to the device an ordinary write ends with. What it adds is the
/// stop, and the stop is the moment the whole condition is about: half a document down, nothing
/// replaced, and a process that is never going to finish.
/// </summary>
internal sealed class StopsWhereItIsTold : FileStream
{
    private readonly bool _stop;

    /// <summary>
    /// Initializes a new instance of the <see cref="StopsWhereItIsTold"/> class.
    /// </summary>
    /// <param name="path">The file the bytes go into.</param>
    /// <param name="stop">Whether this is the write to stop in the middle of.</param>
    internal StopsWhereItIsTold(string path, bool stop)
        : base(path, FileMode.Create, FileAccess.Write, FileShare.None)
    {
        _stop = stop;
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_stop)
        {
            base.Write(buffer, offset, count);
            return;
        }

        base.Write(buffer, offset, count / 2);
        base.Flush();

        Console.Out.WriteLine(EntryPoint.Ready);
        Console.Out.Flush();

        EntryPoint.WaitToBeKilled();
    }
}

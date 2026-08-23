using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The first condition of #70: a process is killed during a write, in a loop, and every survivor
/// is a whole document rather than a mixture of two.
///
/// Nothing here simulates a kill. A writer is started as a child process, it writes through the
/// plugin's own store, and it is stopped without being asked, so no finally block runs, no buffer
/// is flushed on the way out and no file is closed. That is the state a stopped container leaves
/// and it is the state this condition is about.
///
/// <para>Where the kill lands is chosen rather than raced, and that is the part worth reading
/// carefully. A kill thrown at a writing loop lands where the process spends its time, which is
/// after the bytes are down: measured against this write path with the atomicity deliberately
/// removed, a raced kill found a whole document on every round it was tried on. So the writer is
/// told where to stop, it says so, and it waits there until it is killed. Half the rounds stop
/// with half the bytes of the second document down and nothing replaced, which is the moment a
/// torn document would be made. The other half stop once the second document has replaced the
/// first, which is the moment after. Each is asserted for what it is, so a round that stopped
/// somewhere else fails rather than passing quietly.</para>
///
/// <para>What it does not reach. A killed process loses nothing the operating system already
/// holds, so this reads the replace and not the durability of the bytes underneath it. A pulled
/// power supply is the other half of the sentence #70 opens with, it needs hardware this suite
/// does not have, and the write path answers it with a flush to the device rather than with
/// anything measured here.</para>
/// </summary>
public sealed class DocumentWriteKillTests
{
    private const string Name = "agreed";

    /// <summary>
    /// Gets the rounds, as the payload the second document carries and the moment the writer is
    /// asked to stop at. The sizes run from a document that goes down in one write to one that is
    /// megabytes long, because half of a short document and half of a long one are different bytes
    /// to be left holding.
    /// </summary>
    public static TheoryData<int, string> Rounds { get; } = new TheoryData<int, string>
    {
        { 64, EntryPoint.HalfwayThroughTheBytes },
        { 64, EntryPoint.AfterTheReplace },
        { 4_096, EntryPoint.HalfwayThroughTheBytes },
        { 4_096, EntryPoint.AfterTheReplace },
        { 65_536, EntryPoint.HalfwayThroughTheBytes },
        { 65_536, EntryPoint.AfterTheReplace },
        { 1_048_576, EntryPoint.HalfwayThroughTheBytes },
        { 1_048_576, EntryPoint.AfterTheReplace },
    };

    /// <summary>
    /// Kills a writer where it was told to stop, and reads what is left.
    /// </summary>
    /// <param name="payload">How many characters the second document carries.</param>
    /// <param name="moment">Where the writer was asked to stop.</param>
    [ChildProcessTheory]
    [MemberData(nameof(Rounds))]
    public void EveryDocumentThatSurvivesAKillIsAWholeOne(int payload, string moment)
    {
        using var programData = TemporaryDirectory.Create("kill");

        var dataPath = Path.Combine(programData.FullPath, "data");
        Directory.CreateDirectory(dataPath);

        KillAWriter(Muxer()!, dataPath, payload, moment);

        var reading = Read(dataPath);

        Assert.NotNull(reading);
        Assert.Equal(DocumentAnswer.Current, reading!.Answer);
        Assert.True(
            KillableDocument.AgreesWithItself(reading.Document!),
            "The document that survived carries a payload its own generation does not produce, so a reader found bytes from two writes in one file.");

        var generation = reading.Document!.Fields[KillableDocument.GenerationMember]!.GetValue<int>();
        var inFlight = Directory
            .GetFiles(Path.Combine(dataPath, "watch-sync"), "*" + DocumentStoreNames.InFlightSuffix)
            .Select(Path.GetFileName)
            .ToArray();

        if (string.Equals(moment, EntryPoint.HalfwayThroughTheBytes, StringComparison.Ordinal))
        {
            Assert.Equal(1, generation);
            Assert.Single(inFlight);
            return;
        }

        Assert.Equal(EntryPoint.SecondGeneration, generation);
        Assert.Empty(inFlight);
    }

    /// <summary>
    /// Where the runtime that is running this suite lives, so the child is started by the same one.
    ///
    /// It is derived from the directory the running framework was loaded out of rather than found
    /// on the path, because a machine with several runtimes installed would otherwise start the
    /// child under whichever one a path lookup happened to answer with, and the child has to be
    /// the framework the assembly beside it was built for.
    /// </summary>
    /// <returns>The runtime, or null where it is not where it is expected to be.</returns>
    internal static string? Muxer()
    {
        var framework = RuntimeEnvironment.GetRuntimeDirectory();
        var root = Path.GetFullPath(Path.Combine(framework, "..", "..", ".."));
        var muxer = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        return File.Exists(muxer) ? muxer : null;
    }

    /// <summary>
    /// Reads the document a killed writer left behind, through the same store the writer used.
    /// </summary>
    /// <param name="dataPath">The folder standing in for the server's data path.</param>
    /// <returns>What the document turned out to be.</returns>
    private static DocumentReading? Read(string dataPath)
    {
        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(dataPath);

        return new DocumentStore(new StoreFolder(paths.Object)).Read(Name);
    }

    /// <summary>
    /// Starts a writer, waits for it to say it has reached the moment it was asked to stop at, and
    /// kills it there.
    ///
    /// The wait is on the writer's own output rather than on elapsed time. A case that waited for
    /// a duration would be one that passes on a fast machine and is deleted on a slow one, which
    /// is the reason this suite refuses a sleep by name.
    /// </summary>
    /// <param name="muxer">The runtime that starts the child.</param>
    /// <param name="dataPath">The folder standing in for the server's data path.</param>
    /// <param name="payload">How many characters the second document carries.</param>
    /// <param name="moment">Where the writer is asked to stop.</param>
    private static void KillAWriter(string muxer, string dataPath, int payload, string moment)
    {
        var start = new ProcessStartInfo(muxer)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(EntryPoint.WriteUntilKilled);
        start.ArgumentList.Add(dataPath);
        start.ArgumentList.Add(Name);
        start.ArgumentList.Add(payload.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(moment);

        using var writer = Process.Start(start);

        Assert.NotNull(writer);

        var said = writer!.StandardOutput.ReadLine();

        Assert.Equal(EntryPoint.Ready, said);

        writer.Kill(true);
        writer.WaitForExit();
    }
}

/// <summary>
/// What the file of a write in flight is called, as the case reading a killed writer's folder has
/// to know it.
///
/// The store keeps that name to itself, which is right: nothing outside it reads such a file, and
/// a caller that knew the name would be a caller that could go looking for one. What the case
/// needs is not the file but whether one is there, so the suffix is written here rather than
/// opened up on the type, and the two are held together by the case itself: a store that stopped
/// using this suffix would leave the halfway rounds finding no file where they require one.
/// </summary>
internal static class DocumentStoreNames
{
    /// <summary>
    /// The suffix of the file a write in flight goes into.
    /// </summary>
    internal const string InFlightSuffix = ".writing";
}

/// <summary>
/// A case that starts a child process under the runtime this suite is running on. Where that
/// runtime is not where it is expected to sit beside itself the case is skipped and the reason
/// says so, because a green here would otherwise be read as a kill having been survived on a
/// machine where no process was ever started.
/// </summary>
public sealed class ChildProcessTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChildProcessTheoryAttribute"/> class.
    /// </summary>
    public ChildProcessTheoryAttribute()
    {
        if (DocumentWriteKillTests.Muxer() is null)
        {
            Skip = "Not evaluated on this machine: the runtime this suite is running on was not found beside the framework it was loaded from, so no child process could be started under it.";
        }
    }
}

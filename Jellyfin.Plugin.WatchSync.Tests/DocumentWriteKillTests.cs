using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// plugin's own store until somebody stops it, and it is stopped without being asked, so no
/// finally block runs, no buffer is flushed on the way out and no file is closed. That is the
/// state a stopped container leaves and it is the state this condition is about.
///
/// <para>What a kill cannot be made to land on, and what is done about it instead. Which byte of
/// which write the process dies on is not a thing a case can choose, so the loop varies the size
/// of the document being written across its rounds, from one that lands in a single write to one
/// that is megabytes long. A killed writer is therefore stopped at a different point in a
/// different write each round, and the last assertion here refuses a run where every round
/// happened to kill the writer before it had written anything at all, because such a run would be
/// green having read nothing.</para>
///
/// <para>What it does not reach. A killed process loses nothing the operating system already
/// holds, so this reads the rename and not the durability of the bytes underneath it. A pulled
/// power supply is the other half of the sentence #70 opens with, it needs hardware this suite
/// does not have, and the write path answers it with a flush to the device rather than with
/// anything measured here.</para>
/// </summary>
public sealed class DocumentWriteKillTests
{
    private const string Name = "agreed";

    /// <summary>
    /// The payload each round asks the writer for, in characters. The first is small enough to go
    /// down in one write and the last is large enough to take many, so the kill lands in a
    /// different place in the sequence each time.
    /// </summary>
    private static readonly int[] _rounds =
    {
        64,
        512,
        4_096,
        16_384,
        65_536,
        262_144,
        1_048_576,
        2_097_152,
        128,
        8_192,
        524_288,
        1_572_864,
    };

    /// <summary>
    /// Kills a writer in the middle of what it is doing, twelve times, and reads what is left.
    /// </summary>
    [ChildProcessFact]
    public void EveryDocumentThatSurvivesAKillIsAWholeOne()
    {
        var muxer = Muxer()!;

        var generations = new List<int>();

        foreach (var payload in _rounds)
        {
            using var programData = TemporaryDirectory.Create("kill");

            var dataPath = Path.Combine(programData.FullPath, "data");
            Directory.CreateDirectory(dataPath);

            KillAWriter(muxer, dataPath, payload);

            var reading = Read(dataPath);

            Assert.NotNull(reading);
            Assert.Equal(DocumentAnswer.Current, reading!.Answer);
            Assert.True(
                KillableDocument.AgreesWithItself(reading.Document!),
                "A document that survived a kill carries a payload its own generation does not produce, so a reader found bytes from two writes in one file. The round asked for " + payload.ToString(CultureInfo.InvariantCulture) + " characters.");

            generations.Add(reading.Document!.Fields[KillableDocument.GenerationMember]!.GetValue<int>());
        }

        Assert.Contains(generations, generation => generation > 1);
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
    /// Starts a writer, waits for it to say the store holds a whole document, and kills it.
    ///
    /// The wait is on the writer's own output rather than on elapsed time. A case that waited for
    /// a duration would be one that passes on a fast machine and is deleted on a slow one, which
    /// is the reason this suite refuses a sleep by name.
    /// </summary>
    /// <param name="muxer">The runtime that starts the child.</param>
    /// <param name="dataPath">The folder standing in for the server's data path.</param>
    /// <param name="payload">How many characters each document after the first carries.</param>
    private static void KillAWriter(string muxer, string dataPath, int payload)
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

        using var writer = Process.Start(start);

        Assert.NotNull(writer);

        var said = writer!.StandardOutput.ReadLine();

        Assert.Equal(EntryPoint.Ready, said);

        writer.Kill(true);
        writer.WaitForExit();
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
}

/// <summary>
/// A case that starts a child process under the runtime this suite is running on. Where that
/// runtime is not where it is expected to sit beside itself the case is skipped and the reason
/// says so, because a green here would otherwise be read as a kill having been survived on a
/// machine where no process was ever started.
/// </summary>
public sealed class ChildProcessFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChildProcessFactAttribute"/> class.
    /// </summary>
    public ChildProcessFactAttribute()
    {
        if (DocumentWriteKillTests.Muxer() is null)
        {
            Skip = "Not evaluated on this machine: the runtime this suite is running on was not found beside the framework it was loaded from, so no child process could be started under it.";
        }
    }
}

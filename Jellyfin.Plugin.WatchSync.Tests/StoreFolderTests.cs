using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Where this plugin's store sits, and what creating it does.
///
/// These are the conditions of #68 that do not need a record to exist. What the store holds is
/// #14, #48, #26, #36 and #44, and the first of the five is in the tree; the assertion that every
/// one of them is in the store and nothing but settings is in the plugin configuration is the
/// condition this file deliberately does not pretend to meet, because a comparison against an
/// empty configuration type is green over nothing.
///
/// This was the first test in the suite that created a directory of its own, which is the
/// subject #86's leftover-file condition had been waiting for. That assertion is that issue's
/// and is in `LeftoverTests`; what these cases do is take their directory from the type it
/// points at, so the removal happens on the run where one of them fails as well as on the runs
/// where none does.
/// </summary>
public sealed class StoreFolderTests : IDisposable
{
    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFolderTests"/> class, with a directory
    /// of its own standing in for what a server would hand over.
    /// </summary>
    public StoreFolderTests()
    {
        _programData = TemporaryDirectory.Create("store");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    private string DataPath => Path.Combine(_programData.FullPath, "data");

    private string PluginsPath => Path.Combine(_programData.FullPath, "plugins");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>(MockBehavior.Strict);
        paths.SetupGet(applicationPaths => applicationPaths.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }

    /// <summary>
    /// The root is the server's, and exactly one name below it is this plugin's. A path assembled
    /// any other way is the thing the guard in `docs/invariants.md` refuses, and this is the
    /// positive half of it: the guard says which calls are not made, and this says which one is.
    /// </summary>
    [Fact]
    public void ThePathIsTheServersDataFolderAndTheOneNameBelowItThisPluginComposes()
    {
        Assert.Equal(Path.Combine(DataPath, "watch-sync"), Folder().FullPath);
    }

    /// <summary>
    /// The mistake this type exists against, asserted rather than described.
    ///
    /// `BasePlugin.DataFolderPath` is the property a reader reaches for, and on both supported
    /// lines it is the plugin's own install directory with the version appended. The server
    /// deletes that directory when it installs a new version and cleans up the old one, so a
    /// store kept there is a store that empties itself on every upgrade. The agreed record in
    /// #14 and the document upgrade in #71 both exist only if what they read survived one.
    /// </summary>
    [Fact]
    public void TheStoreDoesNotSitInTheDirectoryTheServerReplacesOnAnUpgrade()
    {
        var store = Folder().FullPath;

        Assert.False(
            store.StartsWith(PluginsPath + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            $"{store} is under {PluginsPath}, which the server deletes and re-extracts on an upgrade.");
    }

    /// <summary>
    /// Asking where the folder is says where it would be and creates nothing. A property that
    /// created a directory as a side effect of being read is one no caller can ask twice, and it
    /// is the shape a dry run cannot use.
    /// </summary>
    [Fact]
    public void AskingWhereTheFolderIsCreatesNothing()
    {
        var folder = Folder();

        Assert.False(Directory.Exists(folder.FullPath));
        _ = folder.FullPath;
        Assert.False(Directory.Exists(folder.FullPath));
    }

    /// <summary>
    /// Created on first use, and creating it again is not a second decision about it. The second
    /// call is what an operator produces by restarting a server, and it must not touch what the
    /// first one left.
    /// </summary>
    [Fact]
    public void TheFolderIsCreatedOnFirstUseAndASecondCallLeavesItAlone()
    {
        var folder = Folder();

        var created = folder.CreateIfAbsent();
        Assert.True(Directory.Exists(created));

        var document = Path.Combine(created, "written-before-the-second-call.txt");
        File.WriteAllText(document, "a document the second call must not disturb");

        Assert.Equal(created, folder.CreateIfAbsent());
        Assert.True(File.Exists(document));
    }

    /// <summary>
    /// The folder is created no wider than the one it sits in, and narrower where that is
    /// available. What it holds is a record of what people watched, so a mode that lets the rest
    /// of the machine read it is a disclosure rather than an inconvenience.
    ///
    /// This reads a POSIX file mode. On a platform with no such notion the case is skipped and
    /// the skip names the platform, because a case that quietly passed there would be
    /// indistinguishable from one that read a mode and found it narrow.
    /// </summary>
    [PosixFact]
    public void TheCreatedFolderIsNoWiderThanTheOneItSitsIn()
    {
        // The attribute above has already skipped this case on Windows. The check is here so the
        // platform analyzer can see it, and it fails rather than returns, because a skip that
        // stopped working would otherwise turn this case into one that passes without reading
        // anything.
        if (OperatingSystem.IsWindows())
        {
            Assert.Fail("This case reads a POSIX file mode and should have been skipped on this platform.");
            return;
        }

        var created = Folder().CreateIfAbsent();

        var parent = File.GetUnixFileMode(DataPath);
        var mode = File.GetUnixFileMode(created);

        Assert.Equal(UnixFileMode.None, mode & ~parent);
        Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute));
        Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
    }

    /// <summary>
    /// The data folder on a filesystem of its own is the usual container layout: the image
    /// carries the program and a volume is mounted over the data directory. A creation path that
    /// worked only where the two are one filesystem would pass every test on a developer's
    /// machine and fail on the deployment this plugin is written for.
    ///
    /// This case needs a second writable filesystem to exist on the machine running it. Where
    /// none is found it is skipped, and the skip says that rather than saying the property
    /// holds. Nothing is mounted to create one, because mounting a filesystem is the kind of
    /// privileged act the rule in this milestone refuses.
    /// </summary>
    [SecondFilesystemFact]
    public void TheFolderIsCreatedWhereTheDataFolderIsOnAnotherFilesystem()
    {
        var elsewhere = SecondFilesystem.Root();
        Assert.NotNull(elsewhere);

        var other = Path.Combine(elsewhere, ".watchsync-store-elsewhere-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);

        try
        {
            var dataPath = Path.Combine(other, "data");
            Directory.CreateDirectory(dataPath);

            var paths = new Mock<IApplicationPaths>(MockBehavior.Strict);
            paths.SetupGet(applicationPaths => applicationPaths.DataPath).Returns(dataPath);

            var created = new StoreFolder(paths.Object).CreateIfAbsent();

            Assert.True(Directory.Exists(created));
            Assert.StartsWith(dataPath, created, StringComparison.Ordinal);

            var document = Path.Combine(created, "written-on-the-other-filesystem.txt");
            File.WriteAllText(document, "a document on the other filesystem");
            Assert.Equal("a document on the other filesystem", File.ReadAllText(document));
        }
        finally
        {
            Directory.Delete(other, true);
        }
    }
}

/// <summary>
/// A case that reads a POSIX file mode. Where the platform has no such notion the case is
/// skipped rather than passed, and the reason names the platform, so a reader of a run on
/// Windows sees which of the three legs did not read a mode instead of seeing three greens.
/// </summary>
public sealed class PosixFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PosixFactAttribute"/> class.
    /// </summary>
    public PosixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Not evaluated on " + RuntimeInformation.OSDescription + ": this platform has no POSIX file mode, so there is no mode here to read.";
        }
    }
}

/// <summary>
/// A case that needs a second writable filesystem. Where the machine offers none the case is
/// skipped and the reason says so, because a green here would otherwise be read as the property
/// holding across filesystems on a machine that only has one.
/// </summary>
public sealed class SecondFilesystemFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecondFilesystemFactAttribute"/> class.
    /// </summary>
    public SecondFilesystemFactAttribute()
    {
        if (SecondFilesystem.Root() is null)
        {
            Skip = "Not evaluated on this machine: no second writable filesystem was found beside the one the temporary directory is on, and none is mounted to create one.";
        }
    }
}

/// <summary>
/// Finds a writable filesystem that is not the one the temporary directory sits on, by reading
/// what is already mounted. Nothing is mounted here and nothing is elevated; a machine with one
/// filesystem is a machine where the case above is skipped.
/// </summary>
internal static class SecondFilesystem
{
    /// <summary>
    /// The root of a second writable filesystem, or null where the machine offers none.
    /// </summary>
    /// <returns>The root, or null.</returns>
    internal static string? Root()
    {
        var here = Path.GetPathRoot(TemporaryDirectory.Root);

        foreach (var drive in Drives().Where(drive => !string.Equals(drive, here, StringComparison.Ordinal)))
        {
            var probe = Path.Combine(drive, ".watchsync-filesystem-probe-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(probe);
                Directory.Delete(probe);
                return drive;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }

        return null;
    }

    private static string[] Drives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }
}

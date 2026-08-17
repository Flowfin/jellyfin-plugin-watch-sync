using System;
using System.IO;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// A directory under the temporary root that belongs to one case and does not outlive it.
///
/// This is the one place in the suite that reaches the temporary root, and the guard beside it
/// refuses the same calls everywhere else. The reason it is a type rather than a convention is
/// the direction the mistake takes: a case that creates its own directory and removes it on the
/// last line of the case body is correct until the case fails on the line before, and a suite
/// that leaves a directory behind on the run where something else already went wrong is the
/// suite whose temporary root fills up on the machine nobody is watching.
///
/// Removal here is disposal, so it happens on the failing path as well as on the passing one.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>
    /// What every directory this suite creates under the temporary root is named with. It is a
    /// prefix rather than a full name so that a run outside the suite, reading the root after
    /// the suite has finished, can tell what this repository created from what the machine and
    /// everything else on it did.
    /// </summary>
    internal const string Prefix = "watchsync-";

    private TemporaryDirectory(string fullPath)
    {
        FullPath = fullPath;
    }

    /// <summary>
    /// Gets the directory, which exists from the moment it is handed over until disposal.
    /// </summary>
    internal string FullPath { get; }

    /// <summary>
    /// Gets the temporary root the runtime hands this process. Read here rather than in the
    /// cases, so the answer to "where does the suite write" is one line in one file.
    /// </summary>
    internal static string Root => Path.GetTempPath();

    /// <summary>
    /// Creates a directory under the temporary root.
    /// </summary>
    /// <param name="purpose">What the case wants it for, which lands in the directory name so a
    /// leftover found later says which case to look at.</param>
    /// <returns>The directory, to be disposed by the case that asked for it.</returns>
    internal static TemporaryDirectory Create(string purpose) =>
        new(Directory.CreateTempSubdirectory(Prefix + purpose + "-").FullName);

    /// <summary>
    /// Removes the directory and everything under it.
    ///
    /// A directory somebody else has already removed is not an error: the point is that nothing
    /// is left, and it is left to nobody whether the case removed it early or this did.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(FullPath))
        {
            Directory.Delete(FullPath, true);
        }
    }
}

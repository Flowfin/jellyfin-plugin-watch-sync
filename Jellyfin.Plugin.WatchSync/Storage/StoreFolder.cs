using System;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// Where this plugin keeps what is not a setting, and nothing about what it keeps there.
///
/// The records themselves are argued elsewhere and none of them exists yet: the agreed record is
/// #14, the outbound queue is #48, the unmatched and conflict records are #26 and #36, and the
/// provenance is #44. What #68 asks for before any of them is a folder that is the server's to
/// give and this plugin's to fill, so this type answers where and refuses to answer what.
///
/// The root is <see cref="IApplicationPaths.DataPath"/>, which the server creates and keeps across
/// plugin versions. It is not the plugin's own data folder, and that distinction is the whole
/// reason this type exists rather than a call to the property that looks right.
/// </summary>
public sealed class StoreFolder
{
    /// <summary>
    /// The one name this type composes. Everything above it comes from the server.
    ///
    /// It is a constant because there is nothing else available: the plugin's identifier lives on
    /// the plugin instance, and reaching for that instance is what `static-instance-not-read`
    /// refuses. A leaf name under a root the server gives is a different thing from a path the
    /// plugin composes, and the guard named in `docs/invariants.md` is written around that
    /// difference rather than around the word constant.
    /// </summary>
    internal const string FolderName = "watch-sync";

    private readonly IApplicationPaths _applicationPaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFolder"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths the server hands to the plugin.</param>
    public StoreFolder(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);

        _applicationPaths = applicationPaths;
    }

    /// <summary>
    /// Gets the folder this plugin's store lives in. Reading this creates nothing.
    /// </summary>
    public string FullPath => Path.Combine(_applicationPaths.DataPath, FolderName);

    /// <summary>
    /// Gets the mode the folder is created with where the platform has modes.
    /// </summary>
    internal static UnixFileMode OwnerOnly =>
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Creates the folder if it is not there, and returns it either way.
    ///
    /// On a platform with POSIX file modes the folder is created readable, writable and
    /// enterable by its owner and by nobody else. What it will hold is a record of what people
    /// watched, so the narrow mode is the one to start from: a folder created wide and narrowed
    /// afterwards is wide for the moment between the two, and the first document may already be
    /// in it. On a platform without that notion nothing is set, which is not the same as setting
    /// something permissive, and the test that would read a mode there says which platform it
    /// did not read one on.
    ///
    /// Creating a folder that is already there changes neither its mode nor its contents, so a
    /// second call is not a second decision about either.
    /// </summary>
    /// <returns>The folder.</returns>
    public string CreateIfAbsent()
    {
        var path = FullPath;

        if (Directory.Exists(path))
        {
            return path;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, OwnerOnly);
        }

        return path;
    }
}

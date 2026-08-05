using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Builds a <see cref="Plugin"/> for a test without reaching a server.
/// </summary>
internal static class PluginFactory
{
    /// <summary>
    /// Creates a plugin instance.
    /// </summary>
    /// <returns>The plugin.</returns>
    internal static Plugin Create()
    {
        var applicationPaths = new Mock<IApplicationPaths>();

        // The base constructor derives its data folder from PluginsPath and probes it with
        // Directory.Exists. A path under the test binaries that is never created keeps that
        // probe inside the test's own output and away from any server's data directory.
        applicationPaths
            .SetupGet(paths => paths.PluginsPath)
            .Returns(Path.Combine(AppContext.BaseDirectory, "plugins-absent"));

        return new Plugin(applicationPaths.Object, Mock.Of<IXmlSerializer>());
    }
}

using System;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the page the plugin offers the server dashboard.
/// </summary>
public class PluginPagesTests
{
    /// <summary>
    /// <see cref="Plugin.GetPages"/> builds the resource name from the runtime namespace of the
    /// plugin type, and the server hands that name straight to the assembly's manifest resource
    /// reader. Nothing in the build refuses a name that resolves to nothing: renaming the
    /// namespace, moving the page, or dropping the EmbeddedResource item all leave a plugin that
    /// compiles, loads, and serves a configuration page that is not there.
    /// </summary>
    [Fact]
    public void GetPagesNamesAResourceTheAssemblyActuallyCarries()
    {
        var plugin = CreatePlugin();

        var page = Assert.Single(plugin.GetPages());

        Assert.Equal(plugin.Name, page.Name);
        Assert.Contains(page.EmbeddedResourcePath, typeof(Plugin).Assembly.GetManifestResourceNames());
    }

    private static Plugin CreatePlugin()
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

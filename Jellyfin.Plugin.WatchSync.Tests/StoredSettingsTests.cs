using System;
using System.IO;
using Jellyfin.Plugin.WatchSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The settings as the server holds them, read off the plugin instance the server's manager
/// hands over, which is how a rule that runs on its own reaches the configuration without the
/// plugin's static instance and without a copy taken at start.
///
/// What is held is that the reading is the configuration as it stands, that a refused
/// configuration is read as refused rather than as the defaults, and that a server holding no
/// instance of this plugin is refused rather than answered with the defaults. The last is the
/// one with the worst quiet failure: a rule running on the defaults where an operator set
/// something else.
/// </summary>
public class StoredSettingsTests
{
    /// <summary>
    /// The configuration read is the one the instance holds, not a default.
    /// </summary>
    [Fact]
    public void TheSettingsAreReadOffThePluginTheServerHolds()
    {
        var configuration = new PluginConfiguration { SweepIntervalMinutes = 45 };

        var reading = new StoredSettings(ManagerHolding(configuration)).Read();

        Assert.True(reading.IsRead);
        Assert.Equal(TimeSpan.FromMinutes(45), reading.SweepInterval);
    }

    /// <summary>
    /// A stored value outside what its rule accepts is read as refused, naming the setting, which
    /// is <c>ServerWideSettings</c>'s answer passed through rather than softened.
    /// </summary>
    [Fact]
    public void ARefusedConfigurationIsReadAsRefused()
    {
        var configuration = new PluginConfiguration { ConflictRetentionDays = 0 };

        var reading = new StoredSettings(ManagerHolding(configuration)).Read();

        Assert.False(reading.IsRead);
        Assert.Contains(reading.Refusals, refusal => refusal.Setting == nameof(PluginConfiguration.ConflictRetentionDays));
    }

    /// <summary>
    /// A manager holding no instance of this plugin, or holding only somebody else's, is refused.
    /// </summary>
    [Fact]
    public void AServerHoldingNoInstanceOfThisPluginIsRefused()
    {
        var empty = new Mock<IPluginManager>();
        empty.SetupGet(manager => manager.Plugins).Returns(Array.Empty<LocalPlugin>());

        Assert.Throws<InvalidOperationException>(() => new StoredSettings(empty.Object).Read());

        var somebodyElse = new Mock<IPluginManager>();
        somebodyElse.SetupGet(manager => manager.Plugins).Returns(new[]
        {
            new LocalPlugin(PluginsPath, true, new PluginManifest()) { Instance = new Mock<IPlugin>().Object },
        });

        Assert.Throws<InvalidOperationException>(() => new StoredSettings(somebodyElse.Object).Read());
    }

    /// <summary>
    /// A manager is required.
    /// </summary>
    [Fact]
    public void AManagerIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new StoredSettings(null!));
    }

    /// <summary>
    /// A plugin manager holding one instance of this plugin, with the configuration given, which
    /// is what a running server's manager holds. The instance is constructed the way
    /// <see cref="PluginFactory"/> constructs one, with the configuration set before anything
    /// reads it so that no file is looked for.
    /// </summary>
    /// <param name="configuration">The configuration the instance holds.</param>
    /// <returns>The manager.</returns>
    internal static IPluginManager ManagerHolding(PluginConfiguration configuration)
    {
        var applicationPaths = new Mock<IApplicationPaths>();

        applicationPaths.SetupGet(paths => paths.PluginsPath).Returns(PluginsPath);

        var plugin = new ConfiguredPlugin(applicationPaths.Object, Mock.Of<IXmlSerializer>(), configuration);
        var local = new LocalPlugin(PluginsPath, true, new PluginManifest()) { Instance = plugin };

        var manager = new Mock<IPluginManager>();

        manager.SetupGet(each => each.Plugins).Returns(new[] { local });

        return manager.Object;
    }

    /// <summary>
    /// A path under the test binaries that is never created, which keeps the base constructor's
    /// probe inside the test's own output and away from any server's data directory.
    /// </summary>
    private static string PluginsPath => Path.Combine(AppContext.BaseDirectory, "plugins-absent");

    /// <summary>
    /// The plugin with its configuration set through the base class's own protected setter, so
    /// that reading it looks for no file and serialises nothing.
    /// </summary>
    private sealed class ConfiguredPlugin : Plugin
    {
        internal ConfiguredPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, PluginConfiguration configuration)
            : base(applicationPaths, xmlSerializer)
        {
            Configuration = configuration;
        }
    }
}

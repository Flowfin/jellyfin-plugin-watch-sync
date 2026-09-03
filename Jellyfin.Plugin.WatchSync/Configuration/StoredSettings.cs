using System;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.WatchSync.Configuration;

/// <summary>
/// The settings as the server holds them, read at the moment a rule needs them.
///
/// <see cref="ServerWideSettings"/> turns a configuration into the values the rules take, and
/// until the sweep in #55 nothing asked it to, because nothing ran. A rule that runs on its
/// own has to reach the configuration somehow, and the two obvious routes are both refused by
/// the invariant register: the plugin's static instance, which is the shape #8 is against, and
/// a copy of the values taken at start, which is the setting an operator changes to no effect.
///
/// <para>
/// So this asks the server. The plugin manager holds the instance the server constructed, and
/// that instance holds the configuration the page saves into, so a read here is the document
/// as it stands rather than as it stood. It is a registered service and takes the manager in
/// its constructor, which is the same arrangement every other type here that needs something
/// of the server's is held to.
/// </para>
///
/// <para>
/// A server holding no instance of this plugin is not a state a running server reaches, since
/// the server that constructed this type constructed the plugin first. It is refused rather
/// than answered with the defaults, because a rule reading the defaults on a server whose
/// operator set something else is the failure this type exists against.
/// </para>
/// </summary>
public sealed class StoredSettings
{
    private readonly IPluginManager _plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredSettings"/> class.
    /// </summary>
    /// <param name="plugins">The server's plugin manager, which holds this plugin's instance.</param>
    public StoredSettings(IPluginManager plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _plugins = plugins;
    }

    /// <summary>
    /// Reads the settings as they stand.
    /// </summary>
    /// <returns>
    /// The reading, which is refused where a stored value is outside what its rule accepts,
    /// exactly as <see cref="ServerWideSettings.Read"/> answers it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The server holds no instance of this plugin, so there is no configuration to read.
    /// </exception>
    public ServerWideSettingsReading Read()
    {
        foreach (var local in _plugins.Plugins)
        {
            if (local.Instance is Plugin plugin)
            {
                return ServerWideSettings.Read(plugin.Configuration);
            }
        }

        throw new InvalidOperationException(
            "The server holds no instance of this plugin, so there is no configuration to read. The defaults are not read in its place, because a rule running on the defaults where an operator set something else is the failure a reading of the configuration exists against.");
    }
}

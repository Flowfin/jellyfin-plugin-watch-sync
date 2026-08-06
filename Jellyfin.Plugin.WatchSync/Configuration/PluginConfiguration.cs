using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchSync.Configuration;

/// <summary>
/// Plugin configuration.
///
/// Deliberately empty. Which settings this plugin needs and where each one is stored is decided
/// in #58, and the page that carries them is #57. A setting invented before then is one an
/// operator can change with no effect.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
}

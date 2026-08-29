using System;

namespace Jellyfin.Plugin.WatchSync.Configuration;

/// <summary>
/// One setting the configuration document carries a value for that the rule consuming it does
/// not accept.
///
/// It names the setting, what was found and what the value had to satisfy, because those three
/// are what an operator needs to repair it and none of them is derivable from the other two. A
/// refusal saying only that a value was out of range sends somebody back to a document to find
/// out which range, and the range is declared on a type they cannot open from the dashboard.
/// </summary>
public sealed class SettingRefusal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingRefusal"/> class.
    /// </summary>
    /// <param name="setting">The member of the configuration document that was refused.</param>
    /// <param name="found">The value the document carried.</param>
    /// <param name="bound">What the value had to satisfy, in the units the setting is in.</param>
    public SettingRefusal(string setting, int found, string bound)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(bound);

        Setting = setting;
        Found = found;
        Bound = bound;
    }

    /// <summary>
    /// Gets the member of the configuration document that was refused, by the name it has on
    /// <see cref="PluginConfiguration"/>, which is the name the page binds to.
    /// </summary>
    public string Setting { get; }

    /// <summary>
    /// Gets the value the configuration document carried.
    /// </summary>
    public int Found { get; }

    /// <summary>
    /// Gets what the value had to satisfy, written in the setting's own unit.
    /// </summary>
    public string Bound { get; }
}

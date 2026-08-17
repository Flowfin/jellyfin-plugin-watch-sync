using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Configuration;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The controls on the configuration page and the settings the configuration type declares, held
/// to each other in both directions, which is the second condition of #57.
///
/// The two failures this refuses are opposite and an operator meets them differently. A setting
/// with no control is one they cannot change: it exists, it decides something, and the only way
/// to reach it is to edit a file the server owns. A control with no setting is worse, because it
/// looks like it worked: the page saves, the value goes nowhere, and nothing says so.
///
/// The comparison is a function over the page's markup and a set of names, so both directions are
/// proven on markup written here rather than only on the page as it stands. The plugin declares no
/// setting today, which is #58's to change, so the real pair is empty against empty and a fact
/// resting only on that would pass whatever the rule did.
/// </summary>
public class ConfigurationPageControlsTests
{
    /// <summary>
    /// The elements an operator changes a value with. A page can carry any number of other
    /// identifiers, and the page in the tree does: the page element itself and the paragraph
    /// saying there is nothing to set. Restricting the subject to the three form elements is what
    /// keeps those out of it without naming either of them.
    /// </summary>
    private static readonly Regex _control = new Regex(
        "<(input|select|textarea)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _identifier = new Regex(
        "\\bid\\s*=\\s*[\"']([^\"']*)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The page this plugin serves and the settings it declares name the same set.
    ///
    /// This is the fact that fails on the day a setting is added without a control, which is what
    /// the condition asks for in those words. It is empty against empty today and it is the
    /// comparison rather than an assertion that there is nothing, so it starts biting the moment
    /// #58 declares the first setting.
    /// </summary>
    [Fact]
    public void ThePageAndTheConfigurationTypeNameTheSameSettings()
    {
        Assert.Empty(Disagreements(DeclaredSettings(), ThePage()));
    }

    /// <summary>
    /// A setting the page carries no control for is refused.
    ///
    /// The near miss is the shape somebody actually produces: a setting added to the type while
    /// the page is edited for the one beside it, so the page is a real page carrying a real
    /// control and is missing exactly one.
    /// </summary>
    [Fact]
    public void ASettingWithNoControlOnThePageIsRefused()
    {
        const string Page =
            "<div><input is=\"emby-input\" type=\"number\" id=\"PositionThresholdSeconds\" /></div>";

        var found = Disagreements(new[] { "PositionThresholdSeconds", "FinishThresholdSeconds" }, Page);

        Assert.Contains("FinishThresholdSeconds", Assert.Single(found), StringComparison.Ordinal);
    }

    /// <summary>
    /// A control bound to a setting the type does not declare is refused.
    ///
    /// The same page one rename later. The control saves, the value reaches a member nothing
    /// reads, and the page an operator is looking at says the setting took.
    /// </summary>
    [Fact]
    public void AControlBoundToNoSettingIsRefused()
    {
        const string Page =
            "<div><input is=\"emby-input\" type=\"number\" id=\"PositionThresholdSecond\" /></div>";

        var found = Disagreements(new[] { "PositionThresholdSeconds" }, Page);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, finding => finding.Contains("PositionThresholdSecond\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// An identifier on something that is not a control is not a setting and is not asked to be
    /// one.
    ///
    /// Without this the rule would refuse the page in the tree for the two identifiers it carries
    /// on a division and a paragraph, and the repair somebody would make is a list of exceptions
    /// naming them, which goes stale the first time either is renamed.
    /// </summary>
    [Fact]
    public void AnIdentifierOnSomethingThatIsNotAControlIsNotASetting()
    {
        const string Page =
            "<div id=\"WatchSyncConfigPage\"><p id=\"WatchSyncNoSettings\">nothing to set</p></div>";

        Assert.Empty(Disagreements(Array.Empty<string>(), Page));
    }

    /// <summary>
    /// The page in the tree is the page the server is handed.
    ///
    /// Read out of the assembly as an embedded resource rather than off disk, because that is what
    /// a running server receives, and a file that stopped being embedded would leave a test
    /// reading the disk perfectly happy.
    /// </summary>
    /// <returns>The page's markup.</returns>
    private static string ThePage()
    {
        var resource = typeof(Plugin).Namespace + ".Configuration.configPage.html";

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The settings this plugin's configuration type declares.
    ///
    /// Declared on the type itself rather than inherited, because what an operator sets on this
    /// page is what this plugin decided to have. A member the server's own base type carries is
    /// not a setting of this plugin's and a control for it is not owed.
    /// </summary>
    /// <returns>The names.</returns>
    private static IReadOnlyList<string> DeclaredSettings() =>
        typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => property.Name)
            .ToList();

    /// <summary>
    /// The identifiers the page's form controls carry.
    /// </summary>
    /// <param name="html">The page's markup.</param>
    /// <returns>The identifiers, in the order the page carries them.</returns>
    private static IReadOnlyList<string> ControlsOn(string html) =>
        _control
            .Matches(html)
            .Select(control => _identifier.Match(control.Value))
            .Where(identifier => identifier.Success && identifier.Groups[1].Value.Length > 0)
            .Select(identifier => identifier.Groups[1].Value)
            .ToList();

    /// <summary>
    /// Where a set of settings and a page disagree, one line per disagreement.
    ///
    /// A function over both rather than an assertion inside a fact, so the same rule judges the
    /// page in the tree and the near misses above, and so a rule that stopped refusing either
    /// direction is a red suite rather than a green one.
    /// </summary>
    /// <param name="settings">The settings the configuration type declares.</param>
    /// <param name="html">The page's markup.</param>
    /// <returns>The disagreements.</returns>
    private static IReadOnlyList<string> Disagreements(
        IReadOnlyCollection<string> settings,
        string html)
    {
        var controls = ControlsOn(html);
        var findings = new List<string>();

        foreach (var setting in settings)
        {
            if (!controls.Contains(setting, StringComparer.Ordinal))
            {
                findings.Add(
                    $"the configuration declares {setting} and the page carries no control with that id");
            }
        }

        foreach (var control in controls)
        {
            if (!settings.Contains(control, StringComparer.Ordinal))
            {
                findings.Add(
                    $"the page carries a control with id \"{control}\" and the configuration declares no such setting");
            }
        }

        return findings;
    }
}

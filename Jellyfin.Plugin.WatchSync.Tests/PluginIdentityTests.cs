using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the one identifier that three separate artefacts have to agree on.
/// </summary>
public class PluginIdentityTests
{
    /// <summary>
    /// The identifier the plugin template ships. Any build that keeps it collides with every other
    /// unedited template build installed on the same server.
    /// </summary>
    private const string TemplateId = "eb5d7894-8eef-4b36-aa6f-5d124e828ce1";

    /// <summary>
    /// The server reads the identifier off the plugin class, the configuration page sends it back
    /// to the server to ask for this plugin's configuration, and the packaging reads it out of
    /// build.yaml. Nothing in the build compares them. A build where they disagree produces a
    /// plugin whose configuration page reads nobody's configuration and reports no error while
    /// doing it.
    ///
    /// Each of the three is read from the artefact that carries it and none of them is restated
    /// here, so changing any one on its own leaves the other two disagreeing with it.
    /// </summary>
    [Fact]
    public void ThePluginClassTheConfigurationPageAndBuildYamlNameOneIdentifier()
    {
        var fromPluginClass = IdentityFacts.FromPluginClass();
        var fromConfigurationPage = IdentityFacts.FromConfigurationPage();
        var fromBuildManifest = IdentityFacts.FromBuildManifest();

        Assert.Equal(fromPluginClass, fromConfigurationPage);
        Assert.Equal(fromPluginClass, fromBuildManifest);
    }

    /// <summary>
    /// Agreement alone is satisfied by three copies of the template's own identifier, which is the
    /// state this repository started in. This refuses that value by name.
    /// </summary>
    [Fact]
    public void TheIdentifierIsNotTheOneTheTemplateShips()
    {
        Assert.NotEqual(Guid.Parse(TemplateId), IdentityFacts.FromPluginClass());
    }

    private static class IdentityFacts
    {
        /// <summary>
        /// Reads the value off a constructed plugin, which is the value the server reads when it
        /// registers the plugin and the value the configuration endpoint is keyed on.
        /// </summary>
        internal static Guid FromPluginClass() => PluginFactory.Create().Id;

        /// <summary>
        /// Reads the embedded page the server serves, not the source file next to it, because the
        /// embedded copy is what a browser receives.
        /// </summary>
        internal static Guid FromConfigurationPage()
        {
            var resource = typeof(Plugin).Namespace + ".Configuration.configPage.html";
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);

            using var reader = new StreamReader(stream!);
            var html = reader.ReadToEnd();

            var match = Regex.Match(html, @"pluginUniqueId:\s*'(?<id>[^']+)'");
            Assert.True(match.Success, "configPage.html declares no pluginUniqueId.");

            return Guid.Parse(match.Groups["id"].Value);
        }

        /// <summary>
        /// Reads the guid entry out of build.yaml, which the test project copies into its output.
        /// The read is one anchored line rather than a parse: the key sits at column zero in a
        /// hand-maintained manifest, and a YAML dependency would be a whole parser for one field.
        /// The bound is that a guid moved into a nested mapping would not be found, and the
        /// assertion below turns that into a failure rather than a silent pass.
        /// </summary>
        internal static Guid FromBuildManifest()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "build.yaml");
            Assert.True(File.Exists(path), $"build.yaml was not copied to {AppContext.BaseDirectory}.");

            var match = Regex.Match(
                File.ReadAllText(path),
                @"^guid:\s*""(?<id>[^""]+)""\s*$",
                RegexOptions.Multiline);
            Assert.True(match.Success, "build.yaml declares no guid at the top level.");

            return Guid.Parse(match.Groups["id"].Value);
        }
    }
}

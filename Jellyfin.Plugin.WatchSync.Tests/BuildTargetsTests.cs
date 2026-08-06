using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Plugins;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the two server lines this plugin supports to one another across the three artefacts
/// that have to agree about them: the project that chooses the target frameworks, the server
/// assemblies each target compiles against, and the manifest that tells a server which line an
/// artifact is for.
///
/// A plugin whose declared ABI does not match what it was built against is the failure this
/// exists for. It compiles, it packages, it installs, and the server either refuses to load it
/// with a version message that names no cause, or loads it and throws on the first call into an
/// API that moved. Neither symptom points at the manifest.
/// </summary>
public class BuildTargetsTests
{
    /// <summary>
    /// The line the template as copied declared, which is neither of the two this plugin
    /// supports. Refusing it by name means the value cannot come back by a copy from an older
    /// manifest without the suite saying so.
    /// </summary>
    private const string UnsupportedLine = "10.9.";

    /// <summary>
    /// Every target the project builds needs an entry, because the entry is the only place that
    /// says which server line the artifact from that target is for. A target with no entry
    /// produces an artifact whose ABI is whatever the single top-level pair happens to say,
    /// which is the wrong answer for every target but one.
    /// </summary>
    [Fact]
    public void TheManifestDeclaresOneTargetForEveryFrameworkTheProjectBuilds()
    {
        var built = BuildFacts.FrameworksTheProjectBuilds();
        var declared = BuildFacts.DeclaredTargets().Select(target => target.Framework).ToList();

        Assert.Equal(built.OrderBy(name => name, StringComparer.Ordinal), declared.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The one that has to be run once per target, and the reason the suite multi-targets. Under
    /// each target the plugin binds to a different server assembly, and this asserts that the ABI
    /// written beside that target in the manifest is the version of the assembly it actually
    /// bound to. Nothing here restates either number.
    ///
    /// So a package reference raised to a newer line without the manifest being updated fails
    /// here, which is the mistake that otherwise ships.
    /// </summary>
    [Fact]
    public void TheAbiDeclaredForThisTargetIsTheVersionItCompiledAgainst()
    {
        var framework = BuildFacts.FrameworkThisAssemblyWasBuiltFor();
        var target = Assert.Single(BuildFacts.DeclaredTargets(), entry => entry.Framework == framework);

        Assert.Equal(BuildFacts.ServerAssemblyVersion(), Version.Parse(target.TargetAbi));
    }

    /// <summary>
    /// The packaging tool reads one framework and one ABI off the top level of the manifest and
    /// knows nothing about the targets list. Those two keys are therefore a copy, and a copy that
    /// nothing compares is a third answer waiting to be produced. This refuses the pair naming a
    /// combination the targets list does not carry.
    /// </summary>
    [Fact]
    public void ThePairThePackagingReadsIsOneOfTheDeclaredTargets()
    {
        var text = BuildFacts.ManifestText();
        var pair = new BuildFacts.Target(
            BuildFacts.ScalarAtColumnZero(text, "framework"),
            BuildFacts.ScalarAtColumnZero(text, "targetAbi"));

        Assert.Contains(pair, BuildFacts.DeclaredTargets());
    }

    /// <summary>
    /// Agreement alone is satisfied by every artefact naming a line this plugin does not support,
    /// which is the state this repository started in: the manifest as copied declared 10.9 while
    /// the project referenced 10.9 packages, and the two agreed with each other and with nothing
    /// a supported server runs.
    /// </summary>
    [Fact]
    public void NoDeclaredTargetNamesTheLineThisPluginDoesNotSupport()
    {
        Assert.Empty(BuildFacts.DeclaredTargets()
            .Where(target => target.TargetAbi.StartsWith(UnsupportedLine, StringComparison.Ordinal))
            .Select(target => $"{target.Framework} declares the ABI {target.TargetAbi}, which is a line this plugin does not support."));
    }

    /// <summary>
    /// The reads of the build manifest and of what the project actually built. Internal rather
    /// than private because the compatibility matrix is held to the same manifest, and a second
    /// parse of the same file would be a second answer waiting to disagree with this one.
    /// </summary>
    internal static class BuildFacts
    {
        /// <summary>
        /// One entry of the manifest's targets list.
        /// </summary>
        /// <param name="Framework">The target framework moniker.</param>
        /// <param name="TargetAbi">The server ABI declared for it.</param>
        internal sealed record Target(string Framework, string TargetAbi);

        /// <summary>
        /// The manifest as the test project copies it into its output, which is the same file the
        /// packaging reads.
        /// </summary>
        /// <returns>The manifest text.</returns>
        internal static string ManifestText()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "build.yaml");
            Assert.True(File.Exists(path), $"build.yaml was not copied to {AppContext.BaseDirectory}.");

            return File.ReadAllText(path);
        }

        /// <summary>
        /// Reads one quoted scalar written at column zero. The read is an anchored line rather
        /// than a parse, matching the guid, version and artifact reads next to it: the keys sit at
        /// column zero in a hand-maintained manifest and a YAML parser would be a dependency for
        /// four fields. A key moved into a nested mapping is not found and fails here.
        /// </summary>
        /// <param name="text">The manifest text.</param>
        /// <param name="key">The key to read.</param>
        /// <returns>The value.</returns>
        internal static string ScalarAtColumnZero(string text, string key)
        {
            var match = Regex.Match(text, $@"^{key}:\s*""(?<value>[^""]+)""\s*$", RegexOptions.Multiline);
            Assert.True(match.Success, $"build.yaml declares no {key} at the top level.");

            return match.Groups["value"].Value;
        }

        /// <summary>
        /// Reads the targets list. The list ends at the first line that is not one of its own two
        /// keys, so a truncated or reordered block yields fewer entries rather than silently
        /// absorbing the rest of the file.
        /// </summary>
        /// <returns>The declared targets, in the order the manifest writes them.</returns>
        internal static IReadOnlyList<Target> DeclaredTargets()
        {
            var lines = ManifestText().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var start = lines.FindIndex(line => Regex.IsMatch(line, @"^targets:\s*$"));
            Assert.True(start >= 0, "build.yaml declares no targets list at the top level.");

            var declared = new List<Target>();
            for (var i = start + 1; i + 1 < lines.Count; i += 2)
            {
                var framework = Regex.Match(lines[i], @"^-\s*framework:\s*""(?<value>[^""]+)""\s*$");
                var abi = Regex.Match(lines[i + 1], @"^\s+targetAbi:\s*""(?<value>[^""]+)""\s*$");
                if (!framework.Success || !abi.Success)
                {
                    break;
                }

                declared.Add(new Target(framework.Groups["value"].Value, abi.Groups["value"].Value));
            }

            Assert.NotEmpty(declared);

            return declared;
        }

        /// <summary>
        /// Reads the target frameworks off the plugin project itself rather than off this test
        /// project, because the plugin is what is packaged and shipped.
        /// </summary>
        /// <returns>The frameworks the project builds.</returns>
        internal static IReadOnlyList<string> FrameworksTheProjectBuilds()
        {
            var path = Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync",
                "Jellyfin.Plugin.WatchSync.csproj");
            Assert.True(File.Exists(path), $"The plugin project was not found at {path}.");

            var match = Regex.Match(File.ReadAllText(path), @"<TargetFrameworks>(?<value>[^<]+)</TargetFrameworks>");
            Assert.True(match.Success, "The plugin project declares no TargetFrameworks element.");

            return match.Groups["value"].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>
        /// The moniker of the target this run is executing, taken from the plugin assembly that
        /// was built for it.
        /// </summary>
        /// <returns>The target framework moniker.</returns>
        internal static string FrameworkThisAssemblyWasBuiltFor()
        {
            var attribute = typeof(Plugin).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(attribute);

            var match = Regex.Match(attribute!.FrameworkName, @"^\.NETCoreApp,Version=v(?<version>\d+\.\d+)$");
            Assert.True(match.Success, $"The plugin assembly reports the framework {attribute.FrameworkName}, which this test cannot turn into a moniker.");

            return "net" + match.Groups["version"].Value;
        }

        /// <summary>
        /// The version of the server assembly this target bound to, read off the assembly that
        /// declares the plugin base class. That is the assembly the plugin inherits from, so it is
        /// the one whose shape decides whether a server can load this build at all.
        /// </summary>
        /// <returns>The server assembly version.</returns>
        internal static Version ServerAssemblyVersion()
        {
            var version = typeof(BasePlugin).Assembly.GetName().Version;
            Assert.NotNull(version);

            return version!;
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the version, which the build manifest holds and everything else derives.
/// </summary>
public class PluginVersionTests
{
    /// <summary>
    /// The manifest is the version a server reads to decide whether an install is an upgrade.
    /// The assembly carries three more, and a build where the manifest advertises one number and
    /// the assembly reports another is a plugin an operator cannot reason about: the dashboard
    /// shows one thing and the file on disk another, with nothing saying which is wrong.
    ///
    /// Each number is read from the artefact that carries it. Nothing here restates the version,
    /// so a project file that sets its own overrides the derivation and fails this test, which is
    /// the mistake the derivation is otherwise silent about.
    /// </summary>
    [Fact]
    public void TheManifestAndTheAssemblyNameOneVersion()
    {
        var fromManifest = VersionFacts.FromBuildManifest();

        Assert.Equal(fromManifest, VersionFacts.FromAssemblyVersion());
        Assert.Equal(fromManifest, VersionFacts.FromFileVersion());
        Assert.Equal(fromManifest, VersionFacts.FromInformationalVersion());
    }

    /// <summary>
    /// An assembly version is four numbers. A manifest carrying two of them builds, because the
    /// missing parts are filled in on the way to the assembly, and the two artefacts then hold
    /// values that are equal to a server and unequal to everything that compares them. Refusing
    /// the short form here is cheaper than explaining that difference later.
    /// </summary>
    [Fact]
    public void TheManifestVersionCarriesTheFourPartsAnAssemblyVersionHas()
    {
        var declared = VersionFacts.DeclaredInBuildManifest();

        Assert.Equal(4, declared.Split('.').Length);
    }

    private static class VersionFacts
    {
        /// <summary>
        /// Reads the version entry out of build.yaml, which the test project copies into its
        /// output. The read is one anchored line rather than a parse, matching the guid read next
        /// to it and the read the build itself makes: the key sits at column zero in a
        /// hand-maintained manifest. A version moved into a nested mapping would not be found,
        /// and the assertion turns that into a failure rather than a silent pass.
        /// </summary>
        /// <returns>The version the manifest declares.</returns>
        internal static string DeclaredInBuildManifest()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "build.yaml");
            Assert.True(File.Exists(path), $"build.yaml was not copied to {AppContext.BaseDirectory}.");

            var match = Regex.Match(
                File.ReadAllText(path),
                @"^version:\s*""(?<version>[^""]+)""\s*$",
                RegexOptions.Multiline);
            Assert.True(match.Success, "build.yaml declares no version at the top level.");

            return match.Groups["version"].Value;
        }

        internal static Version FromBuildManifest() => Version.Parse(DeclaredInBuildManifest());

        /// <summary>
        /// The version the runtime binds against and the one a server sees on the loaded assembly.
        /// </summary>
        /// <returns>The assembly version.</returns>
        internal static Version FromAssemblyVersion()
        {
            var version = typeof(Plugin).Assembly.GetName().Version;
            Assert.NotNull(version);

            return version!;
        }

        /// <summary>
        /// The version the file properties show, which is what somebody reads off the dll when a
        /// server will not load it.
        /// </summary>
        /// <returns>The file version.</returns>
        internal static Version FromFileVersion()
        {
            var attribute = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            Assert.NotNull(attribute);

            return Version.Parse(attribute!.Version);
        }

        /// <summary>
        /// The informational version, which is the one build metadata is appended to. Nothing in
        /// this tree appends any today; the suffix is cut anyway so that adding source linking
        /// later fails the build for its own reasons rather than by reddening this test.
        /// </summary>
        /// <returns>The informational version with any build metadata removed.</returns>
        internal static Version FromInformationalVersion()
        {
            var attribute = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Assert.NotNull(attribute);

            var informational = attribute!.InformationalVersion;
            var metadata = informational.IndexOf('+', StringComparison.Ordinal);

            return Version.Parse(metadata < 0 ? informational : informational.Substring(0, metadata));
        }
    }
}

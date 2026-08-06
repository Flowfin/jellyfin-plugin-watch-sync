using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the artifact list in the build manifest, which is the list the packaging step copies
/// out of the publish output and into the archive an operator installs.
/// </summary>
public class BuildManifestArtifactsTests
{
    /// <summary>
    /// The packaging copies the files build.yaml names and nothing else. A name that no longer
    /// matches the assembly the build produces is not a build failure and not a packaging
    /// failure: the archive is produced, it is published, and it carries no plugin. The server
    /// then installs a release that adds nothing, with no error anywhere on the way.
    ///
    /// The produced name is read off the loaded assembly rather than restated here, so setting
    /// AssemblyName, renaming the project or adding a second assembly to the output leaves the
    /// manifest disagreeing with the build and fails here. The comparison is exact rather than a
    /// containment check, because an artifact the manifest omits is the same lost file as one it
    /// misspells.
    /// </summary>
    [Fact]
    public void TheManifestArtifactListNamesTheAssemblyTheBuildProduces()
    {
        var location = typeof(Plugin).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(location), "The plugin assembly reports no file on disk.");

        var produced = new[] { Path.GetFileName(location) };

        Assert.Equal(produced, ArtifactFacts.DeclaredInBuildManifest());
    }

    private static class ArtifactFacts
    {
        /// <summary>
        /// Reads the artifact entries out of build.yaml, which the test project copies into its
        /// output. The read is anchored lines rather than a parse, matching the guid and version
        /// reads next to it and the read the build itself makes: the keys sit at column zero in a
        /// hand-maintained manifest, and a YAML parser would be a whole dependency for one field.
        /// The bound is that a list moved into a nested mapping or written inline would not be
        /// found, and the assertion below turns that into a failure rather than an empty list
        /// that quietly agrees with nothing.
        /// </summary>
        /// <returns>The artifact file names the manifest declares, in the order it declares them.</returns>
        internal static IReadOnlyList<string> DeclaredInBuildManifest()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "build.yaml");
            Assert.True(File.Exists(path), $"build.yaml was not copied to {AppContext.BaseDirectory}.");

            var lines = File.ReadAllLines(path);
            var start = Array.FindIndex(lines, line => Regex.IsMatch(line, @"^artifacts:\s*$"));
            Assert.True(start >= 0, "build.yaml declares no artifacts key at the top level.");

            var declared = new List<string>();
            for (var i = start + 1; i < lines.Length; i++)
            {
                var entry = Regex.Match(lines[i], @"^-\s*""(?<file>[^""]+)""\s*$");
                if (!entry.Success)
                {
                    break;
                }

                declared.Add(entry.Groups["file"].Value);
            }

            Assert.NotEmpty(declared);

            return declared;
        }
    }
}

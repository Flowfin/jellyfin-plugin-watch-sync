using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the checksum files <c>docs/publication-route.md</c> tells an operator to verify a
/// download with against the checksum files the publish route actually writes.
///
/// That document is read by somebody who cannot reach the manifest address, which is the one
/// moment nobody can look anything up. A list there naming a sidecar no run produces sends them
/// to a file that is not on the release page, and a run that starts producing a sidecar the list
/// does not name leaves a verification they were never told they could make. Both directions are
/// silent: the route publishes, the suite is green, and the two disagree until somebody in that
/// exact position reads them side by side.
/// </summary>
public class PublicationFallbackTests
{
    /// <summary>
    /// Every checksum file the route writes is one the fallback document names.
    ///
    /// This is the direction that costs an operator a verification they could have made and were
    /// not told about.
    /// </summary>
    [Fact]
    public void EverySidecarThePublishRouteWritesIsOneTheFallbackNames()
    {
        var route = Sidecars.OfThePublishRoute();
        var document = Sidecars.OfTheFallbackDocument();

        Assert.Empty(route
            .Except(document, StringComparer.Ordinal)
            .Select(extension => $"The publish route writes a .{extension} beside the archive and docs/publication-route.md does not name it."));
    }

    /// <summary>
    /// Every checksum file the fallback document names is one the route writes.
    ///
    /// This is the direction that sends somebody who has already lost the index to a file that is
    /// not on the release page, which is the worse of the two.
    /// </summary>
    [Fact]
    public void EverySidecarTheFallbackNamesIsOneThePublishRouteWrites()
    {
        var route = Sidecars.OfThePublishRoute();
        var document = Sidecars.OfTheFallbackDocument();

        Assert.Empty(document
            .Except(route, StringComparer.Ordinal)
            .Select(extension => $"docs/publication-route.md names a .{extension} beside the archive and the publish route writes no such file."));
    }

    /// <summary>
    /// The guard proven on the route moving underneath the document. The near-miss is the
    /// checksum step with the second sidecar written as a different digest, which is what a
    /// change to a stronger one looks like when the document is left alone. The repair is that
    /// one word.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARouteTheDocumentNoLongerDescribesAndPassesItsRepair()
    {
        var document = Sidecars.OfTheFallbackDocument();

        var moved = Sidecars.InRoute(Sidecars.Fixture("fallback-sidecar-route-near-miss.txt"));

        Assert.Equal(new[] { "sha512" }, moved.Except(document, StringComparer.Ordinal).ToArray());

        var repaired = Sidecars.InRoute(Sidecars.Fixture("fallback-sidecar-route-near-miss-repaired.txt"));

        Assert.Empty(repaired.Except(document, StringComparer.Ordinal));
        Assert.Empty(document.Except(repaired, StringComparer.Ordinal));
    }

    /// <summary>
    /// The guard proven on the document moving underneath the route, which is the direction a
    /// writer takes without touching a workflow at all. The near-miss is the install section with
    /// a sidecar nothing produces added to the list.
    /// </summary>
    [Fact]
    public void TheGuardRefusesADocumentNamingASidecarNothingWritesAndPassesItsRepair()
    {
        var route = Sidecars.OfThePublishRoute();

        var invented = Sidecars.InDocument(Sidecars.Fixture("fallback-sidecar-document-near-miss.txt"));

        Assert.Equal(new[] { "sha512" }, invented.Except(route, StringComparer.Ordinal).ToArray());

        var repaired = Sidecars.InDocument(Sidecars.Fixture("fallback-sidecar-document-near-miss-repaired.txt"));

        Assert.Empty(repaired.Except(route, StringComparer.Ordinal));
        Assert.Empty(route.Except(repaired, StringComparer.Ordinal));
    }

    /// <summary>
    /// Reads the checksum files each side declares. Both reads are anchored on the shape the line
    /// has rather than on a parse of YAML or of Markdown, which is what the other reads of these
    /// two files in this suite do, and both fail loudly on finding nothing: a pattern that stopped
    /// matching would otherwise turn every assertion above into a comparison of two empty sets.
    /// </summary>
    internal static class Sidecars
    {
        private const string Workflow = ".github/workflows/publish.yaml";

        private const string Document = "docs/publication-route.md";

        /// <summary>
        /// Reads the sidecars the publish route this repository ships writes, rather than a copy
        /// of it.
        /// </summary>
        /// <returns>The extension of each sidecar, without its dot.</returns>
        internal static IReadOnlyList<string> OfThePublishRoute() =>
            InRoute(File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Workflow)));

        /// <summary>
        /// Reads the sidecars the fallback document this repository ships names, rather than a
        /// copy of it.
        /// </summary>
        /// <returns>The extension of each sidecar, without its dot.</returns>
        internal static IReadOnlyList<string> OfTheFallbackDocument() =>
            InDocument(File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Document)));

        /// <summary>
        /// Reads the sidecars a piece of workflow text writes beside the archive.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The extension of each sidecar, without its dot.</returns>
        internal static IReadOnlyList<string> InRoute(string text)
        {
            // The checksum step names the archive by stripping .zip off it and appending the
            // digest's own extension, which is the one place in the route where a sidecar's
            // name is decided.
            var extensions = Regex
                .Matches(text, "\\$\\{zip%\\.zip\\}\\.(?<extension>[a-z0-9]+)\"")
                .Select(match => match.Groups["extension"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(extension => extension, StringComparer.Ordinal)
                .ToList();

            if (extensions.Count == 0)
            {
                Assert.Fail($"No checksum file was found in {Workflow}. The read is anchored on the step that names a sidecar after the archive, and that shape has changed, so nothing here is judging the route.");
            }

            return extensions;
        }

        /// <summary>
        /// Reads the sidecars a piece of document text tells an operator to verify against.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The extension of each sidecar, without its dot.</returns>
        internal static IReadOnlyList<string> InDocument(string text)
        {
            // One bullet per sidecar, each opening with the extension in code marks. Anchored on
            // the bullet rather than on the extension anywhere in the prose, so a sentence
            // mentioning one in passing is not read as an instruction to verify against it.
            var extensions = Regex
                .Matches(text, "(?m)^- `\\.(?<extension>[a-z0-9]+)`")
                .Select(match => match.Groups["extension"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(extension => extension, StringComparer.Ordinal)
                .ToList();

            if (extensions.Count == 0)
            {
                Assert.Fail($"No checksum file was found in {Document}. The read is anchored on a bullet opening with the extension in code marks, and that shape has changed, so nothing here is judging the document.");
            }

            return extensions;
        }

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory,
        /// because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Release",
                name));
    }
}

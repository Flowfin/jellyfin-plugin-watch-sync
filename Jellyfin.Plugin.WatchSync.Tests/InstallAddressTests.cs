using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the files that print the address an operator adds to their server to one address.
///
/// The address is not a link somebody follows and finds broken. It is a value pasted into a
/// server's repository list, after which that server polls it and nothing else. A server given
/// an address that is wrong, or one of two addresses where only one is served, raises no error
/// and shows no warning: it goes quiet, and the operator finds out when somebody notices that a
/// version has stood still. So the failure this refuses is two operator-facing files disagreeing
/// about which address to paste, and it is invisible from inside either file because nobody has
/// both open.
///
/// What it cannot ask is whether the address is the right one. Nothing in this tree declares it -
/// the name belongs to the project that serves the catalogue - so two files agreeing on a wrong
/// address pass here, and the request that decides it is written where the address is argued.
/// </summary>
public class InstallAddressTests
{
    /// <summary>
    /// Every file that prints an install address prints the same one.
    ///
    /// The set is asserted non-empty first, with the read named in the message. A pattern that
    /// stopped matching would otherwise satisfy a comparison of one address against nothing at
    /// all, which is the shape a guard over prose fails in.
    /// </summary>
    [Fact]
    public void EveryFileThatPrintsAnInstallAddressPrintsTheSameOne()
    {
        var printers = InstallAddress.Printers();
        var printed = printers.SelectMany(path => InstallAddress.InFile(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            printed.Count > 0,
            "No install address was found anywhere in the readme or in docs/. The read is anchored on an address whose last segment is manifest.json, and that shape has changed, so nothing here is judging any file.");

        Assert.True(
            printed.Count == 1,
            $"{string.Join(", ", printers)} print {printed.Count} install addresses between them: {string.Join(", ", printed)}. An operator pastes one of them, and a server given an address nothing serves goes quiet rather than failing.");
    }

    /// <summary>
    /// Each of those files prints one.
    ///
    /// The other direction of the same rule, and the one a tidy-up takes. An address that
    /// survives in one file only is not a disagreement, so the check above passes it, and the
    /// operator who reads the other file is left without the one value they came for.
    /// </summary>
    [Fact]
    public void TheFrontDoorAndTheDocumentBehindItBothPrintOne()
    {
        Assert.Empty(InstallAddress.TheTwoThatCarryIt
            .Where(path => InstallAddress.InFile(path).Count == 0)
            .Select(path => $"{path} prints no install address, so a reader of it is left without the value an operator pastes."));
    }

    /// <summary>
    /// The guard proven on a second address arriving. The near-miss is the pre-release address
    /// out of `docs/RELEASING.md`, which describes a second manifest address this project's
    /// catalogue does not serve, carried into an operator-facing file the way a writer working
    /// from that document would carry it. The repair is the sentence that says there is no
    /// second one.
    /// </summary>
    [Fact]
    public void TheGuardRefusesASecondAddressAndPassesItsRepair()
    {
        var refused = InstallAddress.In(InstallAddress.Fixture("install-address-near-miss.txt"));

        Assert.Equal(
            new[] { "https://flowfin.dev/manifest.json", "https://flowfin.dev/prerelease-manifest.json" },
            refused.ToArray());

        var repaired = InstallAddress.In(InstallAddress.Fixture("install-address-near-miss-repaired.txt"));

        Assert.Equal(new[] { "https://flowfin.dev/manifest.json" }, repaired.ToArray());
    }

    /// <summary>
    /// The guard proven on a file that stopped printing one. The near-miss is the paragraph that
    /// sends the reader one document further along instead of carrying the value, which is what
    /// somebody removing a duplication writes.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAFileThatPrintsNoAddressAndPassesItsRepair()
    {
        Assert.Empty(InstallAddress.In(InstallAddress.Fixture("install-address-absent-near-miss.txt")));

        var repaired = InstallAddress.In(InstallAddress.Fixture("install-address-absent-near-miss-repaired.txt"));

        Assert.Equal(new[] { "https://flowfin.dev/manifest.json" }, repaired.ToArray());
    }

    /// <summary>
    /// Reads the addresses each operator-facing file prints, off the tracked tree.
    ///
    /// Both files are read from the repository root rather than from a copy in the output
    /// directory, because the subject is what a visitor and an operator see and a copy would
    /// prove the state of the copy.
    /// </summary>
    internal static class InstallAddress
    {
        /// <summary>
        /// The file a visitor reads first, which carries the value to paste.
        /// </summary>
        private const string FrontDoor = "README.md";

        /// <summary>
        /// The file behind it, where the address is argued and where the day it stops answering
        /// is written down.
        /// </summary>
        private const string Behind = "docs/publication-route.md";

        /// <summary>
        /// A manifest address, anchored on the last segment rather than on a host, so an address
        /// moved to another name is still read as one and compared rather than skipped.
        /// </summary>
        private static readonly Regex Printed = new(@"https://[^\s`)""']*manifest\.json", RegexOptions.Compiled);

        /// <summary>
        /// Gets the two files that have to carry the value, which is what the readme and the
        /// document behind it are for.
        /// </summary>
        internal static IReadOnlyList<string> TheTwoThatCarryIt { get; } = new[] { FrontDoor, Behind };

        /// <summary>
        /// Every file that prints an address, derived from the tree rather than listed here.
        ///
        /// A list would leave a document written tomorrow outside the comparison, and a second
        /// address is most likely to arrive in a file nobody thought of when this was written.
        /// </summary>
        /// <returns>The path of each file that prints one, relative to the repository root.</returns>
        internal static IReadOnlyList<string> Printers()
        {
            var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();

            var candidates = new[] { FrontDoor }
                .Concat(Directory
                    .EnumerateFiles(Path.Combine(root, "docs"), "*.md")
                    .Select(path => "docs/" + Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            return candidates.Where(path => InFile(path).Count > 0).ToList();
        }

        /// <summary>
        /// The addresses a piece of text prints, deduplicated and ordered.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>Each distinct address, in ordinal order.</returns>
        internal static IReadOnlyList<string> In(string text) =>
            Printed.Matches(text)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// The addresses one tracked file prints.
        /// </summary>
        /// <param name="path">The path, relative to the repository root.</param>
        /// <returns>Each distinct address, in ordinal order.</returns>
        internal static IReadOnlyList<string> InFile(string path)
        {
            var full = Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                path.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(full), $"No {path} at {full}.");

            return In(File.ReadAllText(full));
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

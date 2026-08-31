using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the gate that asks what this repository is allowed to distribute.
///
/// The component inventory records what went into the build. Until the check beside it
/// existed, nothing asked what any of it was licensed under, so a package arriving under
/// terms a GPL-3.0 work cannot carry landed in the graph, was written into the inventory,
/// and would be published beside the archive as a description of a distribution nobody was
/// allowed to make. That is #118's third condition and it is the one of its four that was
/// waiting on nothing.
///
/// The verdict is made by `.github/check-component-licences.py`, which runs on a runner and
/// never here: a fact that shelled out to it would make this suite need Python on every
/// machine it runs on, which is the headless rule's own subject. So what is held here is the
/// route rather than the verdict — that the packaging run makes the check, that the run
/// proves the check bites, and that the register the check reads says what a register has to
/// say. Each of those is one deletion away from a packaging run that produces an inventory
/// and asks nothing of it, in silence.
/// </summary>
public class ComponentLicenceTests
{
    /// <summary>
    /// The packaging run makes the check, over the inventory it has just written. This is the
    /// one that catches the step being removed or renamed away, which would leave the
    /// inventory produced and unjudged again with every other check on this repository green.
    /// </summary>
    [Fact]
    public void ThePackagingRunChecksTheLicenceOfEveryComponent()
    {
        var package = ComponentLicenceRoute.Package();

        Assert.Contains("INVENTORY: inventory/components.cdx.json", package, StringComparison.Ordinal);
        Assert.Contains(
            "python3 " + ComponentLicenceRoute.Checker + " < \"${INVENTORY}\"",
            package,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The check is proven on every packaging run. It is green on every graph that has never
    /// carried a licence it refuses, which is every graph this repository has had, so a
    /// version of it that had been gutted into a pass would look exactly like the one that
    /// works until the day it was needed.
    ///
    /// Each fixture is named rather than the step being asserted to exist, because a step
    /// still running one of them proves nothing about the arms the others are for.
    /// </summary>
    /// <param name="fixture">A path the proof step has to name.</param>
    [Theory]
    [InlineData(".github/inventory-fixtures/an-incompatible-licence.cdx.json")]
    [InlineData(".github/inventory-fixtures/every-licence-permitted.cdx.json")]
    [InlineData(".github/inventory-fixtures/a-licence-the-inventory-does-not-state.cdx.json")]
    [InlineData(".github/inventory-fixtures/nothing-declared.txt")]
    [InlineData(".github/inventory-fixtures/the-unstated-licence-read.txt")]
    public void TheCheckIsProvenOnEveryPackagingRun(string fixture)
    {
        var package = ComponentLicenceRoute.Package();

        Assert.Contains(ComponentLicenceRoute.Checker, package, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(fixture), package, StringComparison.Ordinal);
        Assert.True(
            File.Exists(ComponentLicenceRoute.Absolute(fixture)),
            $"{fixture} is named by the proof step and is not in the tree, so that step fails on the runner rather than proving anything.");
    }

    /// <summary>
    /// The proof runs each arm and each repair. A step that only ran the refusing fixtures
    /// would pass while the check refused everything, including the graph, and a step that
    /// only ran the passing ones is the state this whole class exists against.
    /// </summary>
    [Fact]
    public void TheProofRunsBothDirections()
    {
        var package = ComponentLicenceRoute.Package();

        Assert.Contains("refuses \"${fixtures}/an-incompatible-licence.cdx.json\"", package, StringComparison.Ordinal);
        Assert.Contains("refuses \"${fixtures}/a-licence-the-inventory-does-not-state.cdx.json\"", package, StringComparison.Ordinal);
        Assert.Contains("refuses \"${fixtures}/every-licence-permitted.cdx.json\" \"${fixtures}/the-unstated-licence-read.txt\"", package, StringComparison.Ordinal);
        Assert.Contains("passes \"${fixtures}/every-licence-permitted.cdx.json\"", package, StringComparison.Ordinal);
        Assert.Contains("passes \"${fixtures}/a-licence-the-inventory-does-not-state.cdx.json\"", package, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every declaration carries its three fields. The check refuses a malformed one on the
    /// runner; this is the cheaper half of that failure, which says which line is wrong here
    /// rather than in a workflow log after a packaging run.
    /// </summary>
    [Fact]
    public void EveryDeclarationCarriesItsThreeFields()
    {
        var wrong = new List<string>();

        foreach (var (line, number) in ComponentLicenceRoute.Declarations())
        {
            var parts = line.Split(" :: ");
            if (parts.Length != 3 || parts.Any(part => part.Trim().Length == 0))
            {
                wrong.Add($"line {number}: {line}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// A declaration is keyed on the exact name and version. A licence can change between two
    /// releases of one package, so a key without a version would carry one reading forward
    /// over every release after it, and the register would go on looking answered.
    /// </summary>
    [Fact]
    public void EveryDeclarationNamesTheVersionItWasReadAt()
    {
        var unversioned = ComponentLicenceRoute.Declarations()
            .Select(entry => entry.Line.Split(" :: ")[0].Trim())
            .Where(component => !component.Contains('@', StringComparison.Ordinal))
            .ToList();

        Assert.Empty(unversioned);
    }

    /// <summary>
    /// Every identifier the register declares is one the checker permits. The register is a
    /// record of a reading and never a way to accept an incompatible licence, and the two
    /// files are the pair that would drift: somebody adds a licence here to get a red run
    /// green, and the table the check decides against never hears about it.
    ///
    /// The permitted set is read out of the checker rather than restated here, so this cannot
    /// be the copy that goes stale.
    /// </summary>
    [Fact]
    public void EveryDeclaredLicenceIsOneTheCheckPermits()
    {
        var permitted = ComponentLicenceRoute.PermittedIdentifiers();

        Assert.NotEmpty(permitted);

        var outside = ComponentLicenceRoute.Declarations()
            .Select(entry => entry.Line.Split(" :: ")[1].Trim())
            .Where(identifier => !permitted.Contains(identifier))
            .ToList();

        Assert.Empty(outside);
    }

    /// <summary>
    /// The permitted set is a property of the licence this repository ships under, so the
    /// check reads that licence instead of assuming it. Without the read, a repository
    /// relicensed to something else would keep being judged against the inbound set of a
    /// licence it no longer carries, and every run would stay green while doing it.
    /// </summary>
    [Fact]
    public void TheCheckReadsWhatThisRepositoryShipsUnder()
    {
        var checker = ComponentLicenceRoute.CheckerText();

        Assert.Contains("GNU GENERAL PUBLIC LICENSE", checker, StringComparison.Ordinal);
        Assert.Contains("Version 3, 29 June 2007", checker, StringComparison.Ordinal);
        Assert.Contains("default=\"LICENSE\"", checker, StringComparison.Ordinal);
    }

    /// <summary>
    /// The licence file the check reads is the one it expects to find. This is the other end
    /// of the read above: it fails here, where the repair is obvious, rather than on a
    /// packaging run that refuses every component for a reason about neither of them.
    /// </summary>
    [Fact]
    public void TheLicenceFileIsTheOneTheCheckKnowsTheInboundSetFor()
    {
        var licence = File.ReadAllText(ComponentLicenceRoute.Absolute("LICENSE"));

        Assert.Contains("GNU GENERAL PUBLIC LICENSE", licence, StringComparison.Ordinal);
        Assert.Contains("Version 3, 29 June 2007", licence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The files this class reads.
    /// </summary>
    internal static class ComponentLicenceRoute
    {
        /// <summary>
        /// The check, as the packaging run and the proof both name it.
        /// </summary>
        internal const string Checker = ".github/check-component-licences.py";

        /// <summary>
        /// The register of licences read out of a component itself.
        /// </summary>
        internal const string Register = ".github/component-licences.txt";

        /// <summary>
        /// The packaging workflow, which both runs the check and proves it.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Package() => Read(".github/workflows/package.yaml");

        /// <summary>
        /// The check itself.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string CheckerText() => Read(Checker);

        /// <summary>
        /// The declarations in the register, without its comments and blank lines.
        /// </summary>
        /// <returns>Each declaration with the line number it was written on.</returns>
        internal static IReadOnlyList<(string Line, int Number)> Declarations()
        {
            var entries = new List<(string, int)>();
            var number = 0;

            foreach (var raw in Read(Register).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                number++;
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                entries.Add((raw, number));
            }

            Assert.NotEmpty(entries);

            return entries;
        }

        /// <summary>
        /// The SPDX identifiers the check permits, read off its own table.
        /// </summary>
        /// <returns>The identifiers.</returns>
        internal static IReadOnlyCollection<string> PermittedIdentifiers()
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var inside = false;

            foreach (var raw in CheckerText().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = raw.Trim();

                if (line.StartsWith("GPL3_INBOUND = {", StringComparison.Ordinal))
                {
                    inside = true;
                    continue;
                }

                if (!inside)
                {
                    continue;
                }

                if (line.StartsWith('}'))
                {
                    break;
                }

                var quote = line.IndexOf('"', StringComparison.Ordinal);
                var closing = quote < 0 ? -1 : line.IndexOf('"', quote + 1);
                if (quote >= 0 && closing > quote)
                {
                    identifiers.Add(line[(quote + 1)..closing]);
                }
            }

            return identifiers;
        }

        /// <summary>
        /// Resolves a repository-relative path.
        /// </summary>
        /// <param name="relative">The path below the repository root.</param>
        /// <returns>The absolute path.</returns>
        internal static string Absolute(string relative) => Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            relative.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// Reads a repository-relative file.
        /// </summary>
        /// <param name="relative">The path below the repository root.</param>
        /// <returns>Its text.</returns>
        private static string Read(string relative)
        {
            var path = Absolute(relative);

            Assert.True(File.Exists(path), $"{relative} is not in the tree, and the licence gate is a check, a register and a step that only work together.");

            return File.ReadAllText(path);
        }
    }
}

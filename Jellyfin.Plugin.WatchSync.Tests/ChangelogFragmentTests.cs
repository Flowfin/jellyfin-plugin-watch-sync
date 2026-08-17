using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds every changelog fragment to the format `docs/changelog.md` writes down.
///
/// The entry an operator most needs is the one that says their existing watch state will be
/// treated differently after the upgrade, and that entry is the one a writer is most likely to
/// file as an ordinary change. So the marking is a field with no default rather than a request
/// to remember, and a fragment that omits it is refused instead of being read as harmless.
///
/// The field set is read out of the document rather than restated here. Two lists of the same
/// fields drift, and the one that drifts is the one nothing runs.
///
/// `changelog.d/` holds no fragment today, so the leg that reads the tree passes over an empty
/// set and proves nothing on its own. What carries this guard is the pair of near misses below,
/// which are fixtures on disk and are read whatever the directory holds.
/// </summary>
public class ChangelogFragmentTests
{
    /// <summary>
    /// Every fragment the directory holds is one the format accepts. This is the leg that runs
    /// against the tree as it is, and its subject is whatever has been written by the time it
    /// runs rather than a list anybody maintains.
    /// </summary>
    [Fact]
    public void EveryFragmentTheDirectoryHoldsIsWellFormed()
    {
        var findings = ChangelogGuard.ScanTheTree();

        Assert.Empty(findings.Select(finding => $"{finding.Path}: {finding.Detail} ({finding.Id})"));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake somebody actually makes. The near miss is
    /// an entry whose writer marked the change correctly and then did not say what it means for
    /// the data that is already there. Everything else about the fragment is right, and the
    /// repair is the one line that was missing.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAMarkedChangeWithNoEffectAndPassesItsRepair()
    {
        var refused = ChangelogGuard.Inspect(
            "changelog.d/0001-near-miss.md",
            ChangelogGuard.Fixture("near-miss.txt"));

        var finding = Assert.Single(refused);
        Assert.Equal("change-without-an-effect", finding.Id);

        var repaired = ChangelogGuard.Inspect(
            "changelog.d/0001-near-miss.md",
            ChangelogGuard.Fixture("near-miss-repaired.txt"));

        Assert.Empty(repaired);
    }

    /// <summary>
    /// The exact-name leg proven the same way, on the other mistake somebody actually makes. One
    /// letter of one field name is the wrong case, which a tolerant read would accept. The guard
    /// reports it twice, because a name nothing knows and a required field that is now absent are
    /// two different things to tell the writer, and the repair is the one character.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAMiscasedFieldNameTwiceAndPassesItsRepair()
    {
        var refused = ChangelogGuard.Inspect(
            "changelog.d/0001-marking-near-miss.md",
            ChangelogGuard.Fixture("marking-near-miss.txt"));

        Assert.Equal(
            new[] { "missing-field", "unknown-field" },
            refused.Select(finding => finding.Id).OrderBy(id => id, StringComparer.Ordinal));

        var repaired = ChangelogGuard.Inspect(
            "changelog.d/0001-marking-near-miss.md",
            ChangelogGuard.Fixture("marking-near-miss-repaired.txt"));

        Assert.Empty(repaired);
    }

    /// <summary>
    /// The remaining rules, each on a document built here rather than on a fixture. These are the
    /// legs whose exact bytes carry nothing: none of them turns on a line ending or on trailing
    /// space, so a file on disk would buy the same assertion and one more thing to keep in step.
    /// </summary>
    /// <param name="id">The rule expected to refuse the document.</param>
    /// <param name="fragment">The fragment text.</param>
    [Theory]
    [InlineData("effect-without-a-change", "Issue: #17\nExisting-Data: unchanged\nEffect: nothing moves\n\nA line for the operator.\n")]
    [InlineData("unknown-marking", "Issue: #17\nExisting-Data: maybe\n\nA line for the operator.\n")]
    [InlineData("names-no-issue", "Issue: 17\nExisting-Data: unchanged\n\nA line for the operator.\n")]
    [InlineData("field-written-twice", "Issue: #17\nExisting-Data: unchanged\nExisting-Data: unchanged\n\nA line for the operator.\n")]
    [InlineData("no-entry-text", "Issue: #17\nExisting-Data: unchanged\n\n\n")]
    [InlineData("header-not-a-field", "Issue: #17\nExisting-Data: unchanged\nthis line is not a field\n\nA line for the operator.\n")]
    public void EachRemainingRuleRefusesTheDocumentItIsAbout(string id, string fragment)
    {
        var finding = Assert.Single(ChangelogGuard.Inspect("changelog.d/0001-a-fragment.md", fragment));

        Assert.Equal(id, finding.Id);
    }

    /// <summary>
    /// A fragment whose name does not follow the convention is refused, because the ordinal is
    /// what makes a directory listing read in the order the entries were written and a fragment
    /// outside it sorts wherever its first letter falls.
    /// </summary>
    /// <param name="path">The path to judge.</param>
    /// <param name="refused">Whether the name is expected to be refused.</param>
    [Theory]
    [InlineData("changelog.d/0002-refuse-a-silent-version-bump.md", false)]
    [InlineData("changelog.d/refuse-a-silent-version-bump.md", true)]
    [InlineData("changelog.d/0002-Refuse-A-Silent-Bump.md", true)]
    [InlineData("changelog.d/0002-refuse-a-silent-version-bump.txt", true)]
    public void AFragmentNameOutsideTheConventionIsRefused(string path, bool refused)
    {
        var findings = ChangelogGuard.InspectName(path);

        Assert.Equal(refused, findings.Any(finding => finding.Id == "name-not-a-fragment-name"));
    }

    /// <summary>
    /// The document and the guard name the same fields, in both directions, and agree about which
    /// of them is required on every entry. A field added to one and not the other is a format
    /// with two definitions, and the definition a writer reads is the one that is not enforced.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheGuardNameTheSameFields()
    {
        var documented = ChangelogGuard.FieldsTheDocumentNames();
        var carried = ChangelogGuard.FieldsTheGuardCarries();

        Assert.NotEmpty(documented);
        Assert.Empty(documented.Keys.Except(carried.Keys, StringComparer.Ordinal));
        Assert.Empty(carried.Keys.Except(documented.Keys, StringComparer.Ordinal));
        Assert.Empty(documented.Where(entry => carried[entry.Key] != entry.Value)
            .Select(entry => $"{entry.Key} is \"{(entry.Value ? "always" : "sometimes")}\" in the document and the other way round in the guard."));
    }

    /// <summary>
    /// Reads a changelog fragment and says what is wrong with it.
    ///
    /// The parse is a header of name and value lines above a blank line, which is the shape the
    /// document describes. It is not a YAML parse: a parser would be a dependency for three
    /// fields, and the value that matters here is one of two words rather than a structure.
    /// </summary>
    internal static class ChangelogGuard
    {
        /// <summary>
        /// The directory the fragments live in, relative to the repository root.
        /// </summary>
        internal const string Directory = "changelog.d";

        /// <summary>
        /// The test project, for reaching the fixtures beside this file.
        /// </summary>
        private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

        /// <summary>
        /// The heading the field table sits under in the document.
        /// </summary>
        private const string FieldSection = "## The fields";

        /// <summary>
        /// The marking that says an entry reaches watch state that has already been synced.
        /// </summary>
        private const string Changed = "changed";

        /// <summary>
        /// The marking that says it does not.
        /// </summary>
        private const string Unchanged = "unchanged";

        /// <summary>
        /// A header line, which is a name at column zero and the value after the colon.
        /// </summary>
        private static readonly Regex HeaderLine = new(@"^(?<name>[A-Za-z][A-Za-z-]*):[ \t]*(?<value>.*)$");

        /// <summary>
        /// A fragment file name: the ordinal that orders the directory, a lower case slug, and
        /// the suffix.
        /// </summary>
        private static readonly Regex FragmentName = new(@"^[0-9]{4}-[a-z0-9]+(-[a-z0-9]+)*\.md$");

        /// <summary>
        /// One thing wrong with one fragment.
        /// </summary>
        /// <param name="Path">The fragment the finding is about.</param>
        /// <param name="Id">What rule refused it.</param>
        /// <param name="Detail">What the writer has to change.</param>
        internal sealed record Finding(string Path, string Id, string Detail);

        /// <summary>
        /// The fields the guard knows, and whether each is required on every entry.
        /// </summary>
        /// <returns>Each field name against whether it is always required.</returns>
        internal static IReadOnlyDictionary<string, bool> FieldsTheGuardCarries() =>
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["Issue"] = true,
                ["Existing-Data"] = true,
                ["Effect"] = false,
            };

        /// <summary>
        /// The fields `docs/changelog.md` names, read out of its table rather than restated.
        /// </summary>
        /// <returns>Each field name against whether the document says it is always required.</returns>
        internal static IReadOnlyDictionary<string, bool> FieldsTheDocumentNames()
        {
            var fields = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (var cells in TableRows())
            {
                var name = Regex.Match(cells[0], "`(?<name>[A-Za-z][A-Za-z-]*)`");

                if (!name.Success)
                {
                    continue;
                }

                Assert.True(
                    cells[1] is "always" or "sometimes",
                    $"docs/changelog.md says \"{cells[1]}\" for {name.Groups["name"].Value}, and the guard reads that column as one of two words.");

                fields[name.Groups["name"].Value] = cells[1] == "always";
            }

            return fields;
        }

        /// <summary>
        /// Runs the guard over every fragment the repository tracks.
        /// </summary>
        /// <returns>Every finding against every fragment.</returns>
        internal static IReadOnlyList<Finding> ScanTheTree()
        {
            var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
            var directory = Path.Combine(root, Directory);
            var findings = new List<Finding>();

            if (!System.IO.Directory.Exists(directory))
            {
                return findings;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                findings.AddRange(InspectName(relative));
                findings.AddRange(Inspect(relative, File.ReadAllText(file)));
            }

            return findings;
        }

        /// <summary>
        /// Judges a fragment's name.
        /// </summary>
        /// <param name="path">The repository-relative path.</param>
        /// <returns>What is wrong with the name.</returns>
        internal static IReadOnlyList<Finding> InspectName(string path)
        {
            if (FragmentName.IsMatch(Path.GetFileName(path)))
            {
                return Array.Empty<Finding>();
            }

            return new[]
            {
                new Finding(
                    path,
                    "name-not-a-fragment-name",
                    "the name is not a four digit ordinal, a lower case slug and the .md suffix, so it does not sort where it was written"),
            };
        }

        /// <summary>
        /// Judges a fragment's contents.
        /// </summary>
        /// <param name="path">The repository-relative path, for the finding to name.</param>
        /// <param name="text">The fragment.</param>
        /// <returns>What is wrong with it.</returns>
        internal static IReadOnlyList<Finding> Inspect(string path, string text)
        {
            var findings = new List<Finding>();
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var known = FieldsTheGuardCarries();
            var last = string.Empty;
            var index = 0;

            for (; index < lines.Length; index++)
            {
                var line = lines[index];

                if (line.Length == 0)
                {
                    break;
                }

                if (last.Length > 0 && (line.StartsWith(' ') || line.StartsWith('\t')))
                {
                    fields[last] = fields[last] + " " + line.Trim();
                    continue;
                }

                var header = HeaderLine.Match(line);

                if (!header.Success)
                {
                    findings.Add(new Finding(path, "header-not-a-field", $"line {index + 1} is above the blank line and is not a field, so it is neither header nor entry"));
                    last = string.Empty;
                    continue;
                }

                var name = header.Groups["name"].Value;

                if (fields.ContainsKey(name))
                {
                    findings.Add(new Finding(path, "field-written-twice", $"{name} is written twice, so one of the two values is the one nothing reads"));
                    continue;
                }

                if (!known.ContainsKey(name))
                {
                    findings.Add(new Finding(path, "unknown-field", $"{name} is not a field docs/changelog.md names, and the names are compared exactly"));
                }

                fields[name] = header.Groups["value"].Value.Trim();
                last = name;
            }

            foreach (var field in known.Where(entry => entry.Value)
                .Select(entry => entry.Key)
                .Where(name => !fields.ContainsKey(name))
                .OrderBy(name => name, StringComparer.Ordinal))
            {
                findings.Add(new Finding(path, "missing-field", $"{field} is required on every entry and has no default, because the default would be the permissive one every time"));
            }

            findings.AddRange(Markings(path, fields));

            if (fields.TryGetValue("Issue", out var issue) && !Regex.IsMatch(issue, @"#[0-9]+"))
            {
                findings.Add(new Finding(path, "names-no-issue", "Issue carries no number written with the hash that links it"));
            }

            if (string.Join("\n", lines.Skip(index)).Trim().Length == 0)
            {
                findings.Add(new Finding(path, "no-entry-text", "there is nothing under the header, so the entry is a marking with no sentence for the operator"));
            }

            return findings;
        }

        /// <summary>
        /// Reads a fixture beside this file.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), TestProject, "Changelog", name));

        /// <summary>
        /// The rules over the marking and the sentence that has to come with it.
        /// </summary>
        /// <param name="path">The fragment the findings are about.</param>
        /// <param name="fields">The header as read.</param>
        /// <returns>What is wrong with the pair.</returns>
        private static IEnumerable<Finding> Markings(string path, IReadOnlyDictionary<string, string> fields)
        {
            if (!fields.TryGetValue("Existing-Data", out var marking))
            {
                yield break;
            }

            if (marking is not (Changed or Unchanged))
            {
                yield return new Finding(path, "unknown-marking", $"Existing-Data reads \"{marking}\", and the two values are {Unchanged} and {Changed}");
                yield break;
            }

            var effect = fields.TryGetValue("Effect", out var written) && written.Length > 0;

            if (marking == Changed && !effect)
            {
                yield return new Finding(path, "change-without-an-effect", "the entry is marked as reaching data that has already been synced and says nothing about what it does to it");
            }

            if (marking == Unchanged && effect)
            {
                yield return new Finding(path, "effect-without-a-change", "the entry carries an Effect and is marked as reaching no existing data, so one of the two is wrong");
            }
        }

        /// <summary>
        /// The rows of the field table in the document.
        /// </summary>
        /// <returns>One row per field, as cells.</returns>
        private static IReadOnlyList<string[]> TableRows()
        {
            var document = File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "docs", "changelog.md"));

            var start = document.IndexOf(FieldSection, StringComparison.Ordinal);

            Assert.True(start >= 0, $"docs/changelog.md carries no section headed \"{FieldSection}\", so there is nothing for the guard to read the field set out of.");

            var rest = document[(start + FieldSection.Length)..];
            var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
            var section = end < 0 ? rest : rest[..end];

            return section.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('|') && !line.Contains("---", StringComparison.Ordinal))
                .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
                .Where(cells => cells.Length >= 3 && cells[0] != "field")
                .ToList();
        }
    }
}

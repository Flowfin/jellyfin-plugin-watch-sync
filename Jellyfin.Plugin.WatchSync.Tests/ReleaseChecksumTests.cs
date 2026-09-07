using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the release route and the runbook to one answer about which files carry a checksum,
/// which is #385.
///
/// The checksum step said every asset other than the archive gets a sha256 and wrote two
/// sidecars, both over the archive. Two of the six files a release attaches had no checksum
/// beside them and the step said they did. Nothing failed and nothing could: a comment is not
/// a rule, and this one is the paragraph a later change reads before deciding what to write
/// beside a new asset, so the drift produced a wrong action rather than a wrong sentence.
///
/// <para>
/// The answer taken is that the archive is the only file with a checksum beside it. It is
/// what `docs/RELEASING.md` already argued, and it is the direction that costs nothing an
/// operator wanted: the manifest and the inventory are read rather than installed, and a
/// sidecar written by the run that wrote the file it describes proves transport and nothing
/// else. The `.md5` is different because a Jellyfin catalog serves it as the plugin checksum
/// to something that installs the archive.
/// </para>
///
/// <para>
/// WHAT THIS SUITE READS IS TEXT AND NEVER A RELEASE. It has no network and no runner, so it
/// reads the step out of the workflow, the sentence out of both files, and the attestation's
/// subject out of the job that signs it. A green run here has read three files and never a
/// published release.
/// </para>
/// </summary>
public class ReleaseChecksumTests
{
    /// <summary>
    /// The step writes a sidecar for the archive and for no other file. This is the half that
    /// is about the route rather than about the sentence: the sentence is true because this is
    /// what the step does, and a sidecar added beside a second asset reddens here first.
    /// </summary>
    [Fact]
    public void TheStepWritesASidecarForTheArchiveAndForNoOtherFile()
    {
        var step = ChecksumStep.Read(ChecksumStep.Text(ChecksumStep.PublishRoute));

        Assert.True(step.HasStep, $"{ChecksumStep.PublishRoute} carries no step named `{ChecksumStep.StepName}`, so a release publishes an archive a catalog has no checksum for.");
        Assert.False(string.IsNullOrEmpty(step.Archive), $"{ChecksumStep.PublishRoute} carries the step and nothing in it names the single archive, so what the sidecars are written over cannot be read from the step itself.");

        Assert.True(
            step.Subjects.Count == 1,
            $"The step writes sidecars for {step.Subjects.Count} files: {string.Join(", ", step.Subjects)}. The archive is the only file with a checksum beside it, and a second subject here is an answer to #385 that `docs/RELEASING.md` does not carry.");

        Assert.Equal(step.Archive, step.Subjects[0]);

        Assert.Equal(
            new[] { ".md5", ".sha256" },
            step.Sidecars.Select(sidecar => sidecar.Extension).OrderBy(extension => extension, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The route and the runbook say one thing about which files carry a checksum, as one
    /// string rather than as two paraphrases. Two paraphrases is the state #385 found: each
    /// one reads as correct on its own and a reader cannot tell which of the two is the rule.
    /// </summary>
    [Fact]
    public void TheRouteAndTheRunbookCarryOneSentenceAboutWhichFilesCarryAChecksum()
    {
        var route = ChecksumStep.Text(ChecksumStep.PublishRoute);
        var runbook = ChecksumStep.Text(ChecksumStep.Runbook);

        Assert.True(
            ChecksumStep.CarriesTheSentence(route),
            $"{ChecksumStep.PublishRoute} does not carry the sentence `{ChecksumStep.Sentence}`, so the step states its own rule about which files carry a checksum and the runbook states another.");

        Assert.True(
            ChecksumStep.CarriesTheSentence(runbook),
            $"{ChecksumStep.Runbook} does not carry the sentence `{ChecksumStep.Sentence}`, so whoever publishes a release reads a different answer from the one the route keeps.");
    }

    /// <summary>
    /// The provenance statement's subject is the one file that carries a checksum. The two
    /// answers move together: a subject reaching the manifest and the inventory would name
    /// them as things this run built, and the reason to widen it would have been a sidecar
    /// beside each, which the answer above does not add.
    /// </summary>
    [Fact]
    public void TheProvenanceSubjectIsTheOneFileThatCarriesAChecksum()
    {
        var route = ChecksumStep.Text(ChecksumStep.PublishRoute);

        Assert.Equal(ChecksumStep.ArchiveGlob, ChecksumStep.AttestationSubject(route));

        Assert.True(
            ChecksumStep.Text(ChecksumStep.Runbook).Contains(ChecksumStep.ArchiveGlob, StringComparison.Ordinal),
            $"{ChecksumStep.Runbook} does not name `{ChecksumStep.ArchiveGlob}`, so what the attestation covers is decided in a workflow and written down nowhere.");
    }

    /// <summary>
    /// The guard proven by the repair the drifted sentence asks for: the route made to carry
    /// out its own claim, with a sha256 beside the manifest and beside the inventory. Every
    /// file is written, the run is green, and the release publishes eight assets under a rule
    /// nobody argued. Its repair is the shape the route carries.
    /// </summary>
    [Fact]
    public void TheGuardRefusesASidecarBesideAnAssetNobodyInstallsAndPassesItsRepair()
    {
        var mistake = ChecksumStep.Read(ChecksumStep.Fixture("checksum-for-every-asset-near-miss.txt"));

        Assert.True(mistake.HasStep);
        Assert.Equal(3, mistake.Subjects.Count);
        Assert.Contains("build.yaml", mistake.Subjects);
        Assert.Contains("components.cdx.json", mistake.Subjects);

        var repaired = ChecksumStep.Read(ChecksumStep.Fixture("checksum-for-every-asset-near-miss-repaired.txt"));

        Assert.True(repaired.HasStep);
        Assert.Single(repaired.Subjects);
        Assert.Equal(repaired.Archive, repaired.Subjects[0]);
    }

    /// <summary>
    /// The guard proven by the defect itself: the step as the mainline carried it, whose
    /// sidecars are right and whose sentence is not. Nothing about that step fails, which is
    /// why the arm is written against the text, and it is the arm that catches the sentence
    /// being reworded back into a rule the route does not keep.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheClaimThatDriftedFromTheStepAndPassesItsRepair()
    {
        var drifted = ChecksumStep.Fixture("checksum-claim-drift-near-miss.txt");

        Assert.Single(ChecksumStep.Read(drifted).Subjects);
        Assert.False(ChecksumStep.CarriesTheSentence(drifted));

        Assert.True(ChecksumStep.CarriesTheSentence(ChecksumStep.Fixture("checksum-for-every-asset-near-miss-repaired.txt")));
    }

    /// <summary>
    /// The guard proven by the other half of #385's third question answered on its own: the
    /// attestation widened to every asset without a sidecar being added beside any of them.
    /// It reads as generosity and it is a claim, and the subject moves with the checksum
    /// answer rather than by itself.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAnAttestationWidenedPastTheArchive()
    {
        var widened = ChecksumStep.AttestationSubject(ChecksumStep.Fixture("attestation-widened-near-miss.txt"));

        Assert.False(string.IsNullOrEmpty(widened));
        Assert.NotEqual(ChecksumStep.ArchiveGlob, widened);
    }

    /// <summary>
    /// Reads the checksum step out of workflow text. Anchored on the step's name and on the
    /// lines under it rather than on a YAML parse, which is what every other read of this
    /// route in the suite does and for the same reason: one dependency for one step, in a file
    /// its readers read by eye.
    /// </summary>
    internal sealed class ChecksumStep
    {
        /// <summary>
        /// The one sentence the route and the runbook both carry. Reworded in one of the two,
        /// it is two paraphrases again and this is the guard that says so.
        /// </summary>
        internal const string Sentence = "The archive is the only file with a checksum beside it, one .md5 and one .sha256.";

        /// <summary>
        /// The step's name on the release route. A rename removes it from this guard, so change
        /// it deliberately or not at all.
        /// </summary>
        internal const string StepName = "Write the checksums";

        /// <summary>
        /// What the attest job signs a provenance statement for.
        /// </summary>
        internal const string ArchiveGlob = "./*.zip";

        /// <summary>
        /// The route that publishes a release.
        /// </summary>
        internal const string PublishRoute = ".github/workflows/publish.yaml";

        /// <summary>
        /// The runbook whoever publishes a release reads.
        /// </summary>
        internal const string Runbook = "docs/RELEASING.md";

        private ChecksumStep(bool hasStep, string archive, IReadOnlyList<Sidecar> sidecars)
        {
            HasStep = hasStep;
            Archive = archive;
            Sidecars = sidecars;
        }

        /// <summary>
        /// Gets a value indicating whether a step with the name is in the text.
        /// </summary>
        internal bool HasStep { get; }

        /// <summary>
        /// Gets the expression the step names its single archive by, read out of the assignment
        /// the step makes rather than typed here, so what the sidecars are compared against comes
        /// from the step itself.
        /// </summary>
        internal string Archive { get; }

        /// <summary>
        /// Gets every sidecar the step writes, in file order.
        /// </summary>
        internal IReadOnlyList<Sidecar> Sidecars { get; }

        /// <summary>
        /// Gets the distinct files the step writes a sidecar for, in file order.
        /// </summary>
        internal IReadOnlyList<string> Subjects =>
            Sidecars.Select(sidecar => sidecar.Subject).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>
        /// Reads a file this repository ships rather than a copy of it.
        /// </summary>
        /// <param name="relative">The path, relative to the repository root.</param>
        /// <returns>The text.</returns>
        internal static string Text(string relative)
        {
            var path = Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                relative.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"{relative} is not in the tree, and the answer to which files carry a checksum is written in two files that have to agree.");

            return File.ReadAllText(path);
        }

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory,
        /// because a copy proves the state of the file on the day it was written.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Release",
                name));

        /// <summary>
        /// Whether the text carries the one sentence, on one line and verbatim.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>Whether it does.</returns>
        internal static bool CarriesTheSentence(string text) =>
            text.Contains(Sentence, StringComparison.Ordinal);

        /// <summary>
        /// The path the attest job signs a statement for, or an empty string where the text
        /// names none.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The subject.</returns>
        internal static string AttestationSubject(string text)
        {
            var subject = Regex.Match(
                text.Replace("\r\n", "\n", StringComparison.Ordinal),
                "^[ ]+subject-path:[ ]*\"(?<path>[^\"]*)\"[ \t]*$",
                RegexOptions.Multiline);

            return subject.Success ? subject.Groups["path"].Value : string.Empty;
        }

        /// <summary>
        /// Reads the step out of workflow text.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The step.</returns>
        internal static ChecksumStep Read(string text)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            var start = Array.FindIndex(lines, line => Regex.IsMatch(line, "^[ ]+- name: " + Regex.Escape(StepName) + "[ \t]*$"));

            if (start < 0)
            {
                return new ChecksumStep(false, string.Empty, Array.Empty<Sidecar>());
            }

            // The step's lines run until the next step at the same indentation.
            var indent = lines[start].Length - lines[start].TrimStart().Length;
            var body = new List<string>();

            for (var index = start + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                var lineIndent = line.Length - line.TrimStart().Length;

                if (line.Trim().Length > 0 && lineIndent <= indent && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                if (line.Trim().Length > 0 && lineIndent < indent)
                {
                    break;
                }

                body.Add(line);
            }

            var archive = string.Empty;
            var sidecars = new List<Sidecar>();

            foreach (var line in body)
            {
                if (line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                var named = Regex.Match(line, "^[ ]*(?<name>[a-z][a-z0-9_]*)=\"\\$\\{zips\\[0\\]");

                if (named.Success)
                {
                    archive = "${" + named.Groups["name"].Value + "}";
                }

                var written = Regex.Match(line, "^[ ]*(?<tool>[a-z0-9]+sum)[ ]+(?<subject>\"[^\"]+\"|[^ \">]+)[ ]*>[ ]*(?<target>\"[^\"]+\"|[^ \">]+)[ \t]*$");

                if (written.Success)
                {
                    sidecars.Add(new Sidecar(
                        written.Groups["tool"].Value,
                        Unquote(written.Groups["subject"].Value),
                        Unquote(written.Groups["target"].Value)));
                }
            }

            return new ChecksumStep(true, archive, sidecars);
        }

        /// <summary>
        /// Drops the shell quoting around a word, which is written at some call sites and not at
        /// others and says nothing about what the word is.
        /// </summary>
        /// <param name="word">The word as the line carries it.</param>
        /// <returns>The word without its quotes.</returns>
        private static string Unquote(string word) => word.Trim('"');

        /// <summary>
        /// One checksum the step writes: the tool, the file it reads and the file it writes.
        /// </summary>
        internal sealed class Sidecar
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Sidecar"/> class.
            /// </summary>
            /// <param name="tool">The command that computes it.</param>
            /// <param name="subject">The file it is computed over.</param>
            /// <param name="target">The file it is written to.</param>
            internal Sidecar(string tool, string subject, string target)
            {
                Tool = tool;
                Subject = subject;
                Target = target;
            }

            /// <summary>
            /// Gets the command that computes the checksum.
            /// </summary>
            internal string Tool { get; }

            /// <summary>
            /// Gets the file the checksum is computed over.
            /// </summary>
            internal string Subject { get; }

            /// <summary>
            /// Gets the file the checksum is written to.
            /// </summary>
            internal string Target { get; }

            /// <summary>
            /// Gets the extension the sidecar is written with, which is what a catalog generator
            /// picking a checksum by filename reads.
            /// </summary>
            internal string Extension =>
                Target.LastIndexOf('.') < 0 ? string.Empty : Target[Target.LastIndexOf('.')..];
        }
    }
}

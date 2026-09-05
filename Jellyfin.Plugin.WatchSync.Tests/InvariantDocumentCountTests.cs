using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds every count <c>docs/invariants.md</c> pastes to the file it was counted from.
///
/// That page hands the reader a command and pastes what it answered, which is this repository's
/// own rule about a figure carrying the command that produced it. What the rule does not buy on
/// its own is that the paste still reproduces: the command stays right while the file it counts
/// moves under it, and the number beside it goes on being read as derived. #361 is the second
/// time a count on that page was wrong in the same direction, and the paragraph carrying it says
/// in its own words that a count in a document is what the page has already been wrong about.
///
/// So the command is re-run rather than trusted. The subject is narrow on purpose: a pasted
/// transcript whose command COUNTS the lines of one tracked file, which is the shape every figure
/// on that page about a data file takes. Everything else the document pastes is a listing, a
/// reach or an exit code, and is left where <c>docs/invariants.md</c> already leaves it - read by
/// somebody rather than by this.
///
/// The command is interpreted rather than executed. A test that shelled out to <c>grep</c> would
/// be a test of what is installed on the machine, and the headless rule this suite is held to is
/// the reason there is no second answer available. What cannot be interpreted is REFUSED and
/// never skipped, because a subject silently dropped is a figure that reads as checked while
/// nothing stands behind it.
/// </summary>
public class InvariantDocumentCountTests
{
    private const string Document = "docs/invariants.md";

    /// <summary>
    /// The whole point. Every count the document pastes is counted again out of the file its own
    /// command names, and a paste that no longer reproduces is refused with both numbers.
    /// </summary>
    [Fact]
    public void EveryCountTheDocumentPastesReExtractsToWhatItSays()
    {
        var findings = PastedCounts.Judge(Read(Document), TrackedFile);

        Assert.Empty(findings.Select(finding => $"{Document}: {finding}"));
    }

    /// <summary>
    /// The comparison read out of the tree rather than over an empty set. A document that lost
    /// its transcripts, or a reader that stopped recognising them, passes every assertion above
    /// while holding nothing, which is the failure this shape invites.
    /// </summary>
    [Fact]
    public void TheDocumentStillPastesCountsForSomethingToHold()
    {
        var subjects = PastedCounts.Extract(Read(Document));

        Assert.NotEmpty(subjects);
        Assert.All(subjects, subject => Assert.NotEmpty(subject.Path));
    }

    /// <summary>
    /// The refusals proven one at a time, on the mistakes that are actually made. Each case is
    /// one change away from a transcript this reader accepts, and the accepted one is asserted
    /// beside it, so a reader that refused everything would fail here rather than look strict.
    /// </summary>
    [Fact]
    public void EachWayAPastedCountGoesWrongIsRefusedAndTheHonestOneIsNot()
    {
        var file = new[] { "# comment", string.Empty, "alpha", "beta" };

        IReadOnlyList<string>? Resolve(string path) =>
            string.Equals(path, "data.txt", StringComparison.Ordinal) ? file : null;

        Assert.Empty(PastedCounts.Judge(Transcript("grep -vc '^#\\|^$' data.txt", "2"), Resolve));

        Assert.Contains(
            "re-extracts to 2",
            Single(PastedCounts.Judge(Transcript("grep -vc '^#\\|^$' data.txt", "3"), Resolve)),
            StringComparison.Ordinal);

        Assert.Contains(
            "names no file in this tree",
            Single(PastedCounts.Judge(Transcript("grep -vc '^#\\|^$' moved.txt", "2"), Resolve)),
            StringComparison.Ordinal);

        Assert.Contains(
            "matches no line",
            Single(PastedCounts.Judge(Transcript("grep -c 'gamma' data.txt", "0"), Resolve)),
            StringComparison.Ordinal);

        Assert.Contains(
            "cannot be read",
            Single(PastedCounts.Judge(Transcript("grep -c 'al(pha)' data.txt", "1"), Resolve)),
            StringComparison.Ordinal);

        Assert.Contains(
            "cannot be read",
            Single(PastedCounts.Judge(Transcript("grep -rc 'alpha' data.txt", "1"), Resolve)),
            StringComparison.Ordinal);

        Assert.Contains(
            "pastes no count",
            Single(PastedCounts.Judge(Transcript("grep -c 'alpha' data.txt", "not a number"), Resolve)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other spelling the document uses, where the forge's own tool prints the path in front
    /// of the number. A reader that took the whole line for the count would refuse every one of
    /// those, and a reader that ignored the path would accept a count pasted against a different
    /// file than the command named.
    /// </summary>
    [Fact]
    public void TheSpellingThatPrintsThePathIsReadAndItsPathIsHeld()
    {
        var file = new[] { "alpha", "alpha", "beta" };

        IReadOnlyList<string>? Resolve(string path) =>
            string.Equals(path, "some/data.txt", StringComparison.Ordinal) ? file : null;

        Assert.Empty(PastedCounts.Judge(
            Transcript("git grep -c 'alpha' -- some/data.txt", "some/data.txt:2"),
            Resolve));

        Assert.Contains(
            "against other/data.txt",
            Single(PastedCounts.Judge(
                Transcript("git grep -c 'alpha' -- some/data.txt", "other/data.txt:2"),
                Resolve)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What the reader deliberately does not judge, asserted so that the boundary is a decision
    /// rather than an accident. A command that lists, reaches or reports an exit code carries no
    /// count, and one that reads more than a single file answers a question this cannot pose to
    /// one path.
    /// </summary>
    [Fact]
    public void ATranscriptThatCountsNothingIsNotASubject()
    {
        IReadOnlyList<string>? Resolve(string path) => null;

        Assert.Empty(PastedCounts.Extract(Transcript("grep -rn 'DataPath' --include=*.cs Jellyfin.Plugin.WatchSync/", "nothing")));
        Assert.Empty(PastedCounts.Extract(Transcript("git grep -l 'IUserDataManager' -- 'Jellyfin.Plugin.WatchSync/**/*.cs'", "a.cs")));
        Assert.Empty(PastedCounts.Judge(Transcript("grep -rn 'TimeProvider' --include=*.cs Jellyfin.Plugin.WatchSync/ ; echo \"exit=$?\"", "exit=1"), Resolve));
    }

    private static string Single(IReadOnlyList<string> findings)
    {
        Assert.Single(findings);

        return findings[0];
    }

    private static string Transcript(string command, string output) =>
        new StringBuilder()
            .AppendLine("Some prose above the block.")
            .AppendLine()
            .Append("    ").AppendLine(command)
            .Append("    ").AppendLine(output)
            .AppendLine()
            .AppendLine("Some prose below it.")
            .ToString();

    private static IReadOnlyList<string>? TrackedFile(string path)
    {
        var full = Path.Combine(
            InvariantGuardTests.InvariantGuard.RepositoryRoot(),
            path.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(full) ? File.ReadAllLines(full) : null;
    }

    private static string Read(string path) =>
        File.ReadAllText(Path.Combine(
            InvariantGuardTests.InvariantGuard.RepositoryRoot(),
            path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Reads the counting transcripts out of a document and re-extracts each one.
    ///
    /// Pure over the document text and over a resolver for the files it names, so the fixtures
    /// above run through the same code the page does rather than through a second implementation
    /// of it.
    /// </summary>
    internal static class PastedCounts
    {
        private static readonly Regex CommandLine = new(
            "^ {4,}(?<git>git )?grep (?<rest>.*)$",
            RegexOptions.CultureInvariant);

        private static readonly Regex OutputLine = new(
            "^ {4,}(?:(?<path>[^\\s:]+):)?(?<count>[0-9]+)\\s*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex Pasted = new(
            "^ {4,}\\S",
            RegexOptions.CultureInvariant);

        private static readonly Regex Invocation = new(
            "^(?<flags>-[A-Za-z]+) '(?<pattern>[^']*)'(?: --)? (?<path>[^\\s']+)$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// A transcript that pastes a count: the command as written, and the line under it.
        /// </summary>
        /// <param name="Command">The command line, with the leading indent removed.</param>
        /// <param name="Output">The line under it, with the leading indent removed.</param>
        /// <param name="Path">The file the command counts, as the command names it.</param>
        internal sealed record Subject(string Command, string Output, string Path);

        /// <summary>
        /// The transcripts in a document whose command counts the lines of one file.
        ///
        /// A command is a subject when it is a <c>grep</c> carrying a count flag AND something is
        /// pasted under it, whatever else is wrong with either. Recognising the subject and being
        /// able to read it are separate questions on purpose: the first decides what is judged and
        /// the second is a refusal, so a count pasted under a command this cannot interpret fails
        /// rather than disappears.
        ///
        /// A command handed to the reader with nothing under it asserts no number and is not a
        /// subject. That is the shape the departures paragraph on that page takes, and it is the
        /// repair #361 asks for rather than a gap in this.
        /// </summary>
        /// <param name="document">The document text.</param>
        /// <returns>One entry per counting transcript, in the order the document carries them.</returns>
        internal static IReadOnlyList<Subject> Extract(string document)
        {
            var subjects = new List<Subject>();
            var lines = document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (var index = 0; index < lines.Length - 1; index++)
            {
                var command = CommandLine.Match(lines[index]);

                if (!command.Success)
                {
                    continue;
                }

                var rest = command.Groups["rest"].Value.Trim();

                if (!CountsLines(rest))
                {
                    continue;
                }

                if (!Pasted.IsMatch(lines[index + 1]))
                {
                    continue;
                }

                var output = lines[index + 1].Trim();
                var named = Invocation.Match(rest);

                subjects.Add(new Subject(rest, output, named.Success ? named.Groups["path"].Value : string.Empty));
            }

            return subjects;
        }

        /// <summary>
        /// Re-extracts every count a document pastes and reports the ones that do not reproduce.
        /// </summary>
        /// <param name="document">The document text.</param>
        /// <param name="resolve">Reads the lines of a file the document names, or null where the tree has no such file.</param>
        /// <returns>One finding per transcript that is wrong, empty where every one reproduces.</returns>
        internal static IReadOnlyList<string> Judge(string document, Func<string, IReadOnlyList<string>?> resolve)
        {
            var findings = new List<string>();

            foreach (var subject in Extract(document))
            {
                var finding = Judge(subject, resolve);

                if (finding is not null)
                {
                    findings.Add(finding);
                }
            }

            return findings;
        }

        private static string? Judge(Subject subject, Func<string, IReadOnlyList<string>?> resolve)
        {
            var named = Invocation.Match(subject.Command);

            if (!named.Success)
            {
                return $"`grep {subject.Command}` cannot be read as a count over one file, so the number under it is held by nothing.";
            }

            var flags = named.Groups["flags"].Value[1..];

            if (flags.Any(flag => flag is not ('c' or 'v')))
            {
                return $"`grep {subject.Command}` cannot be read: this holds a plain count and an inverted one, and nothing else.";
            }

            var pattern = Translate(named.Groups["pattern"].Value);

            if (pattern is null)
            {
                return $"the pattern in `grep {subject.Command}` cannot be read as one this repository's regular expressions answer the same way, so it is refused rather than guessed at.";
            }

            var output = OutputLine.Match("    " + subject.Output);

            if (!output.Success)
            {
                return $"`grep {subject.Command}` pastes no count under it, and a transcript that counts has to show what it counted.";
            }

            var path = output.Groups["path"].Value;

            if (path.Length > 0 && !string.Equals(path, named.Groups["path"].Value, StringComparison.Ordinal))
            {
                return $"`grep {subject.Command}` pastes its count against {path}, which is not the file the command names.";
            }

            var lines = resolve(named.Groups["path"].Value);

            if (lines is null)
            {
                return $"`grep {subject.Command}` names no file in this tree, so its count is against something that is not here.";
            }

            var matcher = new Regex(pattern, RegexOptions.CultureInvariant);
            var inverted = flags.Contains('v', StringComparison.Ordinal);
            var matched = lines.Count(line => matcher.IsMatch(line));
            var counted = inverted ? lines.Count - matched : matched;

            if (matched == 0)
            {
                return $"the pattern in `grep {subject.Command}` matches no line of {named.Groups["path"].Value}, so it answers the same number whatever that file holds.";
            }

            var pasted = int.Parse(output.Groups["count"].Value, CultureInfo.InvariantCulture);

            return counted == pasted
                ? null
                : $"`grep {subject.Command}` pastes {pasted} and re-extracts to {counted}.";
        }

        /// <summary>
        /// Whether a command counts rather than lists or reaches. Read off the single-letter flag
        /// groups, so a long option is never mistaken for one.
        /// </summary>
        /// <param name="rest">The command line after the word grep.</param>
        /// <returns>True where a count flag is carried.</returns>
        private static bool CountsLines(string rest) =>
            rest.Split(' ')
                .Any(token => token.Length > 1
                    && token[0] == '-'
                    && token[1] != '-'
                    && token.Skip(1).All(char.IsLetter)
                    && token.Contains('c', StringComparison.Ordinal));

        /// <summary>
        /// Turns the basic regular expression grep is given into the one this runtime answers, or
        /// refuses it.
        ///
        /// The two dialects disagree about which characters are ordinary. A bar, a brace, a
        /// parenthesis, a plus and a question mark are literal text to the command as written and
        /// are operators here, so a pattern carrying one would be answered differently by the two
        /// and the number would be right about neither. Only the escaped alternation is
        /// translated, because it is the one construct the document uses; everything else is
        /// refused rather than approximated.
        /// </summary>
        /// <param name="pattern">The pattern as the document writes it.</param>
        /// <returns>The equivalent pattern, or null where the two dialects would disagree.</returns>
        private static string? Translate(string pattern)
        {
            var translated = new StringBuilder();

            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];

                if (character == '\\')
                {
                    if (index + 1 >= pattern.Length || pattern[index + 1] != '|')
                    {
                        return null;
                    }

                    translated.Append('|');
                    index++;
                    continue;
                }

                if (character is '|' or '(' or ')' or '{' or '}' or '+' or '?')
                {
                    return null;
                }

                translated.Append(character);
            }

            return translated.ToString();
        }
    }
}

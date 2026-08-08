using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the front door to the tree it points at, and to the manifest a catalogue shows next to
/// it.
///
/// A broken link in the README is the one broken link nobody is told about: a visitor who follows
/// it and lands on nothing leaves rather than reporting it, and the file is the last one anybody
/// reads again after it is written. Checking it by reading is how it stays broken, so the check
/// is here.
/// </summary>
public class ReadmeLinkTests
{
    /// <summary>
    /// Every relative link in the README resolves to something that exists.
    ///
    /// The set is asserted non-empty first. A regular expression that stopped matching would
    /// otherwise leave this test green over nothing, which is the failure a link check exists to
    /// prevent rather than one it may have.
    /// </summary>
    [Fact]
    public void EveryRelativeLinkInTheReadmeResolvesToAPathInTheTree()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var links = Readme.Links(Readme.Text(root)).Where(target => !Readme.IsAbsolute(target)).ToList();

        Assert.NotEmpty(links);

        Assert.Empty(links
            .Where(target => !Readme.Resolves(root, target))
            .Select(target => $"README.md links {target}, which is not a path in this repository."));
    }

    /// <summary>
    /// The README links nothing over the network.
    ///
    /// A link check that may not reach the network cannot judge an address, and the suite is held
    /// to a rule that refuses the network for reasons that have nothing to do with this file. An
    /// address nothing judges, in the document a visitor trusts most, is the case this check
    /// exists for, so the front door carries only links this check can follow. Where an outside
    /// address genuinely has to be named, it is named in a document behind this one and the reason
    /// travels with it.
    ///
    /// A note above the heading is inside the front door and its links are the front door's links.
    /// The visitor who follows one is the same visitor, on the same page, and an aside is not a
    /// place an unfollowed address becomes acceptable. So this check reaches into a note and its
    /// text is unchanged by the question having been asked.
    /// </summary>
    [Fact]
    public void TheReadmeLinksNothingThisCheckCannotFollow()
    {
        var absolute = Readme.Links(Readme.Text(HeadlessGuardTests.HeadlessGuard.RepositoryRoot()))
            .Where(Readme.IsAbsolute)
            .ToList();

        Assert.Empty(absolute
            .Select(target => $"README.md links {target}, which is off this repository and which nothing here checks."));
    }

    /// <summary>
    /// The sentence a catalogue shows and the sentence the README opens with are the same bytes.
    ///
    /// The two are read in the same minute by the same person deciding whether to install this,
    /// and they are held in two files. Two statements of one thing drift, and the drift is
    /// invisible from either side because nobody has both open.
    ///
    /// The sentence this compares is the first thing the document says in its own voice. A note
    /// above the heading speaks about the document rather than as it, in the same way the heading
    /// does, so neither one is the opening sentence and both are stepped over. What this covers is
    /// therefore narrower than the first block of the file and unchanged in what it is for: the
    /// first paragraph of prose still has to be the manifest's overview, byte for byte, and no
    /// wording placed above it moves that comparison onto something else.
    /// </summary>
    [Fact]
    public void TheReadmeOpensWithTheSentenceTheBuildManifestPublishes()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var overview = Readme.ManifestOverview(root);

        Assert.Equal(overview, Readme.OpeningSentence(Readme.Text(root)));
    }

    /// <summary>
    /// A note above the heading is stepped over, and the paragraph below it is the answer.
    ///
    /// The three checks above judge README.md as it stands, so on the day the file changes they
    /// stop saying anything about this rule. The rule is the thing that moved, so it is held here
    /// against text written for the purpose, where a later edit to the front door cannot make it
    /// vacuous.
    /// </summary>
    [Fact]
    public void ANoteAboveTheHeadingIsNotTheOpeningSentence()
    {
        var document = string.Join(
            "\n",
            "> [!NOTE]",
            ">",
            "> **Part of something.** A sentence no catalogue shows.",
            string.Empty,
            "# Title",
            string.Empty,
            "The sentence the manifest publishes.",
            string.Empty,
            "A paragraph after it.");

        Assert.Equal("The sentence the manifest publishes.", Readme.OpeningSentence(document));
    }

    /// <summary>
    /// Stepping over a note never leaves the check with nothing to compare.
    ///
    /// A skip that widens until it reaches the end of the file is how this rule would go green
    /// over a document holding no prose at all, which is the mistake worth catching in the rule
    /// that was just widened rather than the one that was not.
    /// </summary>
    [Fact]
    public void ADocumentOfNothingButHeadingsAndNotesHasNoOpeningSentence()
    {
        var document = string.Join(
            "\n",
            "> [!NOTE]",
            ">",
            "> A note and nothing else.",
            string.Empty,
            "# Title",
            string.Empty,
            "## Another heading");

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Readme.OpeningSentence(document));
    }

    /// <summary>
    /// An address inside a note is an address the link check sees.
    ///
    /// This is the other half of the same question, answered the other way and held here for the
    /// same reason: the front door carries the note, so a link check that stopped at the quote
    /// marker would leave the one place a branding line is most likely to put an outside address.
    /// </summary>
    [Fact]
    public void ANoteIsNotAWayToCarryAnAddressPastTheLinkCheck()
    {
        var document = string.Join(
            "\n",
            "> [!NOTE]",
            ">",
            "> **Part of [somewhere](https://example.invalid/somewhere).**",
            string.Empty,
            "# Title",
            string.Empty,
            "A paragraph.");

        Assert.Equal(
            new[] { "https://example.invalid/somewhere" },
            Readme.Links(document).Where(Readme.IsAbsolute).ToArray());
    }

    /// <summary>
    /// Reads the README and the build manifest off the tracked tree.
    ///
    /// Both are read from the repository root rather than from a copy in the output directory,
    /// because the subject is the file a visitor sees on the forge and a copy would prove the
    /// state of the copy.
    /// </summary>
    internal static class Readme
    {
        /// <summary>
        /// Inline Markdown links, which is the only form this file uses.
        /// </summary>
        private static readonly Regex InlineLink = new(@"\[[^\]]*\]\((?<target>[^)\s]+)\)", RegexOptions.Compiled);

        /// <summary>
        /// The manifest key a catalogue listing shows, read as an anchored line rather than
        /// parsed, matching how the other manifest facts are read in this project.
        /// </summary>
        private static readonly Regex Overview = new(@"^overview:\s*""(?<value>[^""]+)""\s*$", RegexOptions.Multiline);

        /// <summary>
        /// Reads the README.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The README text.</returns>
        internal static string Text(string root)
        {
            var path = Path.Combine(root, "README.md");
            Assert.True(File.Exists(path), $"No README.md at {path}.");

            return File.ReadAllText(path);
        }

        /// <summary>
        /// Every inline link target in the document, in the order they appear.
        /// </summary>
        /// <param name="text">The README text.</param>
        /// <returns>The link targets.</returns>
        internal static IEnumerable<string> Links(string text) =>
            InlineLink.Matches(text).Select(match => match.Groups["target"].Value);

        /// <summary>
        /// Whether a target names something outside this repository. A scheme or a leading double
        /// slash is the whole of the test; anything else is a path in the tree.
        /// </summary>
        /// <param name="target">The link target.</param>
        /// <returns><c>true</c> where the target is not a path in this repository.</returns>
        internal static bool IsAbsolute(string target) =>
            target.Contains("://", StringComparison.Ordinal)
            || target.StartsWith("//", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.Ordinal);

        /// <summary>
        /// Whether a relative target resolves to a file or a directory in the tree. A fragment is
        /// cut before the lookup, because the path is what this check can judge.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <param name="target">The link target.</param>
        /// <returns><c>true</c> where the path exists.</returns>
        internal static bool Resolves(string root, string target)
        {
            var withoutFragment = target.Split('#')[0];
            if (withoutFragment.Length == 0)
            {
                return false;
            }

            var path = Path.Combine(root, withoutFragment.Replace('/', Path.DirectorySeparatorChar));

            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>
        /// The first paragraph of the document that is neither a title nor a note.
        ///
        /// A block quote is stepped over for the same reason a heading is: it is not a paragraph
        /// the document says in its own voice, and a note placed above the heading would otherwise
        /// be read as the sentence a catalogue publishes.
        /// </summary>
        /// <param name="text">The README text.</param>
        /// <returns>The opening sentence, with its line breaks flattened to single spaces.</returns>
        internal static string OpeningSentence(string text)
        {
            var paragraph = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(block => block.Trim())
                .FirstOrDefault(block => block.Length > 0 && !block.StartsWith('#') && !block.StartsWith('>'));

            Assert.NotNull(paragraph);

            return string.Join(' ', paragraph!.Split('\n').Select(line => line.Trim()));
        }

        /// <summary>
        /// The overview sentence out of the build manifest.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The overview.</returns>
        internal static string ManifestOverview(string root)
        {
            var path = Path.Combine(root, "build.yaml");
            Assert.True(File.Exists(path), $"No build.yaml at {path}.");

            var match = Overview.Match(File.ReadAllText(path));
            Assert.True(match.Success, "build.yaml declares no overview at the top level.");

            return match.Groups["value"].Value;
        }
    }
}

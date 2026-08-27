using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the note a contributor reads first to the tree it describes, which is what #114 asks of
/// it.
///
/// The note's whole design is that it points rather than restates: the headless rule, the settings
/// table and the parity table each live in one place, and a second copy of any of them drifts from
/// the copy that is enforced while a reader cannot tell which of the two they are holding. A
/// document built out of pointers is only worth what the pointers are worth, and a pointer that
/// stopped resolving is the one nobody is told about, because a contributor who follows it and
/// lands on nothing leaves rather than reporting it.
///
/// So the pointers are checked, and beside them sit the two facts that refuse the deletion of what
/// the note says in its own voice. Those two are weaker than the pointers and are worth having
/// anyway, for the reason <c>OptOutDocumentTests</c> gives about the same shape: they refuse a
/// section being removed, which is how a document loses the half that says no, and they refuse
/// nothing about a rewrite that keeps the heading and changes what sits under it. Whether the
/// wording still says what it is here to say is a judgement no reading of this tree makes, and the
/// review is where a drifted one is caught.
/// </summary>
public class ContributingNoteTests
{
    /// <summary>
    /// Where the note lives, relative to the repository root.
    /// </summary>
    private const string Document = "CONTRIBUTING.md";

    /// <summary>
    /// Every relative link in the note resolves to something in the tree.
    ///
    /// The set is asserted non-empty first. A pattern that stopped matching would otherwise leave
    /// this green over nothing, which is the failure a link check exists to prevent rather than
    /// one it may have.
    /// </summary>
    [Fact]
    public void EveryRelativeLinkInTheNoteResolvesToAPathInTheTree()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var links = ReadmeLinkTests.Readme
            .Links(Text(root))
            .Where(target => !ReadmeLinkTests.Readme.IsAbsolute(target))
            .ToList();

        Assert.NotEmpty(links);

        Assert.Empty(links
            .Where(target => !ReadmeLinkTests.Readme.Resolves(root, target))
            .Select(target => $"{Document} links {target}, which is not a path in this repository."));
    }

    /// <summary>
    /// The note links the headless rule rather than restating it, which is #114's second
    /// condition.
    ///
    /// The rule is enforced by a scan against a vocabulary, and the file that document names is
    /// held to that vocabulary by a test of its own. A copy of the rule in this note would be held
    /// to nothing, and the day the two disagreed the contributor would be reading the one that
    /// does not decide.
    /// </summary>
    [Fact]
    public void TheNoteLinksTheHeadlessRuleRatherThanRestatingIt()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var rule = "Jellyfin.Plugin.WatchSync.Tests/headless-rule.md";

        Assert.True(
            File.Exists(Path.Combine(root, "Jellyfin.Plugin.WatchSync.Tests", "headless-rule.md")),
            "There is no headless rule file for the note to point at.");

        Assert.Contains(
            rule,
            ReadmeLinkTests.Readme.Links(Text(root)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The note still says what an issue has to say, which is #114's third condition.
    ///
    /// Four things, and the fourth is the one that gets tidied away because it reads as a style
    /// note: a number in an issue carries the command that produced it. It is not a style note.
    /// A figure with no command behind it is a claim about a tree nobody can go and look at, and
    /// the wrong ones are wrong in the direction that made them worth quoting.
    /// </summary>
    [Fact]
    public void TheNoteStillStatesWhatAnIssueHasToSay()
    {
        var text = Text(HeadlessGuardTests.HeadlessGuard.RepositoryRoot());

        Assert.Contains("What is wrong", text, StringComparison.Ordinal);
        Assert.Contains("What the evidence is", text, StringComparison.Ordinal);
        Assert.Contains("what \"done\" means", text, StringComparison.Ordinal);
        Assert.Contains(
            "A number in an issue carries the command that produced it",
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The sign-off instruction and the check that enforces it name the same file, which is
    /// #114's fourth condition.
    ///
    /// The check's failure message is what a contributor meets at the moment they are stuck, and
    /// it sends them here. A message naming a file this note had been renamed out of would leave
    /// that contributor with a refusal, an instruction, and nowhere to go, and neither side of the
    /// pair would look wrong on its own.
    /// </summary>
    [Fact]
    public void TheSignOffInstructionAndTheCheckNameTheSameFiles()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var workflow = Path.Combine(root, ".github", "workflows", "dco.yml");

        Assert.True(File.Exists(workflow), $"No DCO workflow at {workflow}.");

        var message = File.ReadAllText(workflow);

        Assert.Contains(Document, message, StringComparison.Ordinal);
        Assert.Contains("./DCO", message, StringComparison.Ordinal);

        var text = Text(root);

        Assert.Contains("git commit -s", text, StringComparison.Ordinal);
        Assert.Contains("[DCO](DCO)", text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "DCO")), "There is no DCO for either to name.");
    }

    /// <summary>
    /// The note says what it does not carry, and names what that is waiting for.
    ///
    /// #114's first condition is a local command that runs the legs the gate runs. Two of the four
    /// contexts the mainline requires are produced by a workflow in another repository, so what
    /// they run is not in this tree to reproduce, and a command written anyway would cover less
    /// than a contributor reading it would assume. This fact refuses the deletion of that
    /// admission, which is the direction such a section is edited in: somebody adds a command,
    /// removes the paragraph saying it is incomplete, and the note then promises the thing it
    /// still cannot do.
    /// </summary>
    [Fact]
    public void TheNoteSaysWhatItDoesNotCarryAndWhatThatWaitsFor()
    {
        var text = Text(HeadlessGuardTests.HeadlessGuard.RepositoryRoot());

        Assert.Contains("What this note does not carry yet", text, StringComparison.Ordinal);
        Assert.Contains("#90", text, StringComparison.Ordinal);
        Assert.Contains("#105", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the note off the tracked tree rather than a copy in the output directory, because the
    /// subject is the file a contributor opens on the forge.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The note's text.</returns>
    private static string Text(string root)
    {
        var path = Path.Combine(root, Document);

        Assert.True(File.Exists(path), $"No {Document} at {path}.");

        return File.ReadAllText(path);
    }
}

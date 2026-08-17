using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Document;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Carrying a document written by an older version forward, one version at a time, which is #71.
///
/// Two kinds of fact are here and the difference is worth reading before either. The ones over
/// `Document/version-*.json` are about this plugin's own declaration: one fixture per version it
/// has written, each carried to the current version and compared against a document committed
/// beside it. The ones over a ladder built in this file are about the mechanism, and they are
/// built here on purpose. This plugin has shipped one version, so its own ladder is empty and
/// proves nothing about sequencing; a fixture ladder of three steps proves the rules, and a fact
/// resting on the real one would be a fact about the state of the tree instead.
///
/// What no fixture here decides is what a document contains. The shapes the store will hold are
/// #14, #26, #36, #44 and #48 and none of them is in the tree, so the members in the fixture
/// stand for a shape rather than declaring one, and every rule below is about the version and
/// about members being carried rather than about any particular member. That is the same bound
/// #69 landed under and it is stated rather than left to be noticed.
/// </summary>
public class DocumentUpgradeTests
{
    private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";
    private const string FixtureDirectory = "Document";

    /// <summary>
    /// Every version this plugin has written has a fixture, and every fixture a version.
    ///
    /// This is the second condition of #71 and it is refused in both directions, because the two
    /// failures are different. A version declared without a fixture is a shape nobody can prove
    /// an upgrade against, which is what that condition asks for in the words "shipping a version
    /// without one fails". A fixture for a version nobody declares is the opposite: a file that
    /// looks like proof and is read by nothing, left behind by a version that was renumbered or
    /// abandoned.
    /// </summary>
    [Fact]
    public void EveryShippedVersionHasAFixtureAndEveryFixtureHasAVersion()
    {
        var declared = DocumentVersions.Shipped
            .Select(version => version.ToString(CultureInfo.InvariantCulture))
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToList();

        var found = Directory
            .GetFiles(FixtureRoot(), "version-*.json")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.EndsWith("-expected.json", StringComparison.Ordinal))
            .Select(name => name!["version-".Length..^".json".Length])
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(declared, found);

        foreach (var version in DocumentVersions.Shipped)
        {
            Assert.True(
                File.Exists(Fixture(version, "expected")),
                $"version {version} has a fixture and no document saying what it upgrades to");
        }
    }

    /// <summary>
    /// Each fixture reaches the current version and is the document committed beside it.
    ///
    /// This is the third condition of #71, and the comparison is against a document rather than
    /// against the absence of an exception, because an upgrade that drops a member throws
    /// nothing.
    ///
    /// Today the plugin declares one version, so the fixture is already current, no step runs and
    /// the expected document is the same document. That is degenerate and it is not empty: it
    /// holds the fixture, the expected document and the reading of both to the current version,
    /// so the day a second version is declared the pair stops matching and this fact is what says
    /// so.
    /// </summary>
    [Fact]
    public void EachFixtureReachesTheCurrentVersionAndIsTheDocumentBesideIt()
    {
        var upgrade = DocumentVersions.Upgrade();

        foreach (var version in DocumentVersions.Shipped)
        {
            var reading = StoredDocument.Read(
                File.ReadAllText(Fixture(version, null)),
                DocumentVersions.Current);

            Assert.False(reading.IsRefused, $"the fixture for version {version} was refused");

            var document = Assert.IsType<StoredDocument>(reading.Document);

            Assert.Equal(version, document.Version);

            var answer = upgrade.Carry(document);

            Assert.Equal(DocumentVersions.Current, answer.Document.Version);

            var expected = StoredDocument.Read(
                File.ReadAllText(Fixture(version, "expected")),
                DocumentVersions.Current);

            var wanted = Assert.IsType<StoredDocument>(expected.Document);

            Assert.Equal(DocumentVersions.Current, wanted.Version);
            Assert.Equal(wanted.ToJson(), answer.Document.ToJson());
        }
    }

    /// <summary>
    /// The versions are a run from one with no gaps, and the declared ladder covers them.
    ///
    /// The ladder is checked by being built. <see cref="DocumentUpgrade"/> refuses one that does
    /// not carry a step for every version below the current one, so this fact fails on the day a
    /// version is added to the declaration without its step, and it fails at the declaration
    /// rather than at the first document that needed the missing rung.
    /// </summary>
    [Fact]
    public void TheDeclaredVersionsAreARunFromOneAndTheLadderCoversThem()
    {
        Assert.NotEmpty(DocumentVersions.Shipped);

        for (var index = 0; index < DocumentVersions.Shipped.Count; index++)
        {
            Assert.Equal(index + 1, DocumentVersions.Shipped[index]);
        }

        Assert.Equal(DocumentVersions.Shipped[^1], DocumentVersions.Current);
        Assert.Equal(DocumentVersions.Current, DocumentVersions.Upgrade().CurrentVersion);
        Assert.Equal(DocumentVersions.Current - 1, DocumentVersions.Ladder.Count);
    }

    /// <summary>
    /// A document three versions behind goes through every rung, in order.
    ///
    /// This is the rule #71 leads with. The route is asserted rather than only the destination,
    /// because a ladder that reached the top in one jump produces the same document and the two
    /// are told apart by nothing else. The value each step appends is what makes the order
    /// visible: a ladder run in any other order spells something else.
    /// </summary>
    [Fact]
    public void ADocumentThreeVersionsBehindGoesThroughEveryRungInOrder()
    {
        var upgrade = new DocumentUpgrade(4, new[] { Appending(1), Appending(2), Appending(3) });

        var answer = upgrade.Carry(At(1, """{"route":""}"""));

        Assert.Equal(DocumentUpgradeOutcome.CarriedForward, answer.Outcome);
        Assert.Equal(1, answer.FromVersion);
        Assert.Equal(new[] { 2, 3, 4 }, answer.VersionsPassedThrough);
        Assert.Equal(4, answer.Document.Version);
        Assert.Equal("""{"version":4,"route":"1-2 2-3 3-4"}""", answer.Document.ToJson());
    }

    /// <summary>
    /// A document one version behind takes the one rung that is between it and the top.
    ///
    /// The same ladder as above, entered higher up. A mechanism that walked from the oldest
    /// version it knows rather than from the version the document carries would rewrite members
    /// that were already carried, and the route is what shows it did not.
    /// </summary>
    [Fact]
    public void ADocumentOneVersionBehindTakesOnlyTheRungAboveIt()
    {
        var upgrade = new DocumentUpgrade(4, new[] { Appending(1), Appending(2), Appending(3) });

        var answer = upgrade.Carry(At(3, """{"route":"already"}"""));

        Assert.Equal(new[] { 4 }, answer.VersionsPassedThrough);
        Assert.Equal("""{"version":4,"route":"already 3-4"}""", answer.Document.ToJson());
    }

    /// <summary>
    /// A member no step declared survives the whole ladder.
    ///
    /// The second rule in #71, and the one an upgrade breaks quietly. A step is handed an object
    /// holding its declared members and nothing else, so it never sees this member and cannot
    /// drop it; the mechanism carries it across each rung. The nested value is there because a
    /// shallow copy of the top level is the repair somebody makes first and it loses everything
    /// underneath.
    /// </summary>
    [Fact]
    public void AMemberNoStepDeclaredSurvivesTheWholeLadder()
    {
        var upgrade = new DocumentUpgrade(4, new[] { Appending(1), Appending(2), Appending(3) });

        var answer = upgrade.Carry(
            At(1, """{"route":"","somethingALaterVersionAdded":{"kept":[1,2]}}"""));

        Assert.Equal(
            """{"version":4,"route":"1-2 2-3 3-4","somethingALaterVersionAdded":{"kept":[1,2]}}""",
            answer.Document.ToJson());
    }

    /// <summary>
    /// A step cannot see or remove a member it did not declare.
    ///
    /// The step here tries to delete a member that belongs to another part of the document, which
    /// is the shape of the mistake rather than an unlikely one: a step written against the whole
    /// object removes the member it is replacing and takes a neighbour with it when a name is
    /// mistyped. It is handed an object without that member, so the deletion reaches nothing.
    /// </summary>
    [Fact]
    public void AStepCannotSeeOrRemoveAMemberItDidNotDeclare()
    {
        var reaching = new DocumentUpgradeStep(
            1,
            new[] { "mine" },
            fields =>
            {
                fields.Remove("somebodyElses");
                fields["mine"] = "carried";
            });

        var answer = new DocumentUpgrade(2, new[] { reaching })
            .Carry(At(1, """{"mine":"old","somebodyElses":"kept"}"""));

        Assert.Equal("""{"version":2,"mine":"carried","somebodyElses":"kept"}""", answer.Document.ToJson());
    }

    /// <summary>
    /// A step that leaves behind a member it did not declare is refused.
    ///
    /// The other direction of the same declaration. Merging it would let a step write anywhere in
    /// the document while declaring one member, and the declaration is the only thing standing
    /// between a step and a member some other version owns. Refusing is louder than dropping it,
    /// which would lose the step's own work in silence.
    /// </summary>
    [Fact]
    public void AStepThatLeavesAMemberItDidNotDeclareIsRefused()
    {
        var overreaching = new DocumentUpgradeStep(
            1,
            new[] { "mine" },
            fields => fields["notMine"] = true);

        var upgrade = new DocumentUpgrade(2, new[] { overreaching });

        Assert.Throws<InvalidOperationException>(() => upgrade.Carry(At(1, """{"mine":1}""")));
    }

    /// <summary>
    /// A step may remove a member it declared, because that is what a rename is.
    ///
    /// The rule above must not become a rule that nothing can be deleted. A step declaring both
    /// names moves the value and drops the old member, and this is what says the mechanism allows
    /// it inside the surface the step declared.
    /// </summary>
    [Fact]
    public void AStepRenamesAMemberItDeclaredAndTheOldNameIsGone()
    {
        var renaming = new DocumentUpgradeStep(
            1,
            new[] { "agreedCount", "agreedPlayCount" },
            fields =>
            {
                if (fields.TryGetPropertyValue("agreedCount", out var value))
                {
                    fields["agreedPlayCount"] = value?.DeepClone();
                    fields.Remove("agreedCount");
                }
            });

        var answer = new DocumentUpgrade(2, new[] { renaming })
            .Carry(At(1, """{"agreedCount":2,"untouched":"kept"}"""));

        Assert.Equal("""{"version":2,"untouched":"kept","agreedPlayCount":2}""", answer.Document.ToJson());
    }

    /// <summary>
    /// A document already at the current version has no step run over it.
    ///
    /// The first half of the fourth condition of #71. The step here fails if it is ever reached,
    /// so this asserts that nothing ran rather than that nothing changed: an upgrade run a second
    /// time is what turns a rename into a deletion, because the member it looks for is gone and
    /// the member it writes is already there.
    /// </summary>
    [Fact]
    public void ADocumentAlreadyAtTheCurrentVersionHasNoStepRunOverIt()
    {
        var refusing = new DocumentUpgradeStep(
            1,
            new[] { "mine" },
            _ => throw new InvalidOperationException("a step ran on a document that was current"));

        var document = At(2, """{"mine":1}""");
        var answer = new DocumentUpgrade(2, new[] { refusing }).Carry(document);

        Assert.Equal(DocumentUpgradeOutcome.AlreadyCurrent, answer.Outcome);
        Assert.Empty(answer.VersionsPassedThrough);
        Assert.Same(document, answer.Document);
    }

    /// <summary>
    /// Carrying an answer's document again runs nothing and changes nothing.
    ///
    /// The second half of the same condition, and it follows from the first rather than being a
    /// separate mechanism: an upgraded document carries the current version, so a second carry
    /// meets the case above. Both halves are asserted because the condition asks for both, and
    /// because a mechanism that keyed on something other than the version would pass one and fail
    /// the other.
    /// </summary>
    [Fact]
    public void CarryingAnAnswerAgainRunsNothingAndChangesNothing()
    {
        var upgrade = new DocumentUpgrade(3, new[] { Appending(1), Appending(2) });

        var once = upgrade.Carry(At(1, """{"route":""}"""));
        var twice = upgrade.Carry(once.Document);

        Assert.Equal(DocumentUpgradeOutcome.CarriedForward, once.Outcome);
        Assert.Equal(DocumentUpgradeOutcome.AlreadyCurrent, twice.Outcome);
        Assert.Empty(twice.VersionsPassedThrough);
        Assert.Equal(once.Document.ToJson(), twice.Document.ToJson());
        Assert.Equal("""{"version":3,"route":"1-2 2-3"}""", twice.Document.ToJson());
    }

    /// <summary>
    /// A ladder that does not reach the current version from version one is refused.
    ///
    /// Three ladders and one reason. A version declared without its step, a step that skips a
    /// version, and two steps for one version are the three ways the declaration and the ladder
    /// come apart, and each of them leaves a document that is silently not carried where the
    /// mechanism is checked at use instead of at construction.
    /// </summary>
    [Fact]
    public void ALadderThatDoesNotReachTheCurrentVersionIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new DocumentUpgrade(3, new[] { Appending(1) }));

        Assert.Throws<ArgumentException>(
            () => new DocumentUpgrade(3, new[] { Appending(1), Appending(3) }));

        Assert.Throws<ArgumentException>(
            () => new DocumentUpgrade(3, new[] { Appending(1), Appending(1) }));

        Assert.Throws<ArgumentException>(
            () => new DocumentUpgrade(2, new[] { Appending(1), Appending(2) }));
    }

    /// <summary>
    /// The version is not one of the members, so a step may not declare it.
    ///
    /// A step that could write the version would be deciding the ladder's own arithmetic, and the
    /// document would arrive at the top carrying a number no rung agreed to. It is refused where
    /// the step is built rather than where it runs.
    /// </summary>
    [Fact]
    public void AStepMayNotDeclareTheVersionAmongItsMembers()
    {
        Assert.Throws<ArgumentException>(
            () => new DocumentUpgradeStep(1, new[] { "version" }, _ => { }));

        Assert.Throws<ArgumentException>(
            () => new DocumentUpgradeStep(1, new[] { "mine", "mine" }, _ => { }));

        Assert.Throws<ArgumentException>(
            () => new DocumentUpgradeStep(1, new[] { string.Empty }, _ => { }));
    }

    /// <summary>
    /// A document from the future is not something to carry forward.
    ///
    /// #69 refuses it before anything reaches here and carries no document out of the refusal, so
    /// a document at a version above the current one arriving at this rule is a caller that read a
    /// refusal and carried on. It is answered as the caller's mistake rather than as one of the
    /// outcomes, which is the same separation `StoredDocument` already makes for a version this
    /// code could not have written.
    /// </summary>
    [Fact]
    public void ADocumentFromTheFutureIsNotSomethingToCarryForward()
    {
        var upgrade = new DocumentUpgrade(2, new[] { Appending(1) });

        Assert.Throws<ArgumentOutOfRangeException>(() => upgrade.Carry(At(4, "{}")));
        Assert.Throws<ArgumentNullException>(() => upgrade.Carry(null!));
    }

    /// <summary>
    /// A step that appends where it came from, so a route is readable in the document it produced.
    /// </summary>
    /// <param name="from">The version the step reads.</param>
    /// <returns>The step.</returns>
    private static DocumentUpgradeStep Appending(int from) =>
        new DocumentUpgradeStep(
            from,
            new[] { "route" },
            fields =>
            {
                var soFar = fields.TryGetPropertyValue("route", out var value)
                    ? value?.GetValue<string>() ?? string.Empty
                    : string.Empty;

                var rung = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", from, from + 1);

                fields["route"] = soFar.Length == 0 ? rung : soFar + " " + rung;
            });

    /// <summary>
    /// A document at a version, read the way one arrives out of the store.
    ///
    /// It goes through <see cref="StoredDocument.Read"/> rather than being assembled, because
    /// that is the only route from bytes to a document and a fact resting on a second one would
    /// be a fact about this file.
    /// </summary>
    /// <param name="version">The version the document carries.</param>
    /// <param name="members">The members beside it, as text.</param>
    /// <returns>The document.</returns>
    private static StoredDocument At(int version, string members)
    {
        var written = new JsonObject
        {
            ["version"] = JsonValue.Create(version),
        };

        foreach (var member in JsonNode.Parse(members)!.AsObject())
        {
            written[member.Key] = member.Value?.DeepClone();
        }

        var reading = StoredDocument.Read(written.ToJsonString(), Math.Max(version, 1));

        return Assert.IsType<StoredDocument>(reading.Document);
    }

    private static string FixtureRoot() =>
        Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            TestProject,
            FixtureDirectory);

    private static string Fixture(int version, string? suffix)
    {
        var name = suffix is null
            ? string.Format(CultureInfo.InvariantCulture, "version-{0}.json", version)
            : string.Format(CultureInfo.InvariantCulture, "version-{0}-{1}.json", version, suffix);

        return Path.Combine(FixtureRoot(), name);
    }
}

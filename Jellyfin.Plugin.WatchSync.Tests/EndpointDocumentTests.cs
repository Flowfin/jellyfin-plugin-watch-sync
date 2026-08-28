using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Tests.Endpoints;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds <c>docs/endpoints.md</c> to the routes this plugin actually serves, which is #112's
/// second condition.
///
/// The issue's own sentence is that a document which drifts from the routes is worse than none,
/// because it is trusted. So the comparison fails in both directions: a route with no row is one
/// somebody automating an installation cannot find, and a row naming no route survives every
/// rename of the thing it was written for and leaves a document that reads as covering a surface
/// it has lost track of.
///
/// <para>
/// THE PLUGIN SERVES NO ENDPOINT AND THE TABLE HAS NO ROW, so the comparison over the real pair is
/// empty against empty and decides nothing on its own. It is a function over both sides for that
/// reason, and every direction is proven on the fixtures beside
/// <see cref="EndpointReflection"/>, which carry an endpoint of each shape. Without that a
/// comparison recognising no controller would pass today and would pass on the day the first
/// controller landed.
/// </para>
///
/// <para>
/// The population is <see cref="EndpointReflection"/>'s and is not re-derived here. #112's own
/// reading says why: this guard and #66's policy table reflect over the same controllers for
/// different reasons, and two reflections written separately disagree about what counts, which is
/// invisible while there is nothing to disagree over.
/// </para>
///
/// <para>
/// The authorisation cell appears in this document and in #66's table, and that is not one register
/// restated in another. Both are closed against the attribute rather than against each other, so
/// neither can drift from it or from the other, and they answer different questions: the table is
/// where somebody decides what may be exposed, and the document is what a caller reads to find out
/// what they must present. An endpoint that nothing authorises at all is #66's refusal and not
/// this one's, so the cell is compared only where the attribute names a policy.
/// </para>
/// </summary>
public class EndpointDocumentTests
{
    /// <summary>
    /// Where the document lives, relative to the repository root.
    /// </summary>
    private const string Document = "docs/endpoints.md";

    /// <summary>
    /// The identifier every example in the document is written with.
    /// </summary>
    private const string Placeholder = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// One row of the table.
    /// </summary>
    /// <param name="Name">The declaring type and method, as <c>Type.Method</c>.</param>
    /// <param name="Verb">The HTTP method the row says.</param>
    /// <param name="Route">The route the row says.</param>
    /// <param name="Authorisation">The authorisation the row says.</param>
    private sealed record Row(string Name, string Verb, string Route, string Authorisation);

    /// <summary>
    /// The plugin's routes and the document name the same set, and each row describes the route it
    /// names.
    ///
    /// This is the fact that fails on the day a route is added without an entry, which is what the
    /// condition asks for in those words. It is empty against empty today and it is a comparison
    /// rather than an assertion that there is nothing, so it starts biting the moment the first
    /// controller lands.
    /// </summary>
    [Fact]
    public void ThePluginsRoutesAndTheDocumentNameTheSameSet()
    {
        Assert.Empty(Findings(
            EndpointReflection.Discovered(EndpointReflection.ThePlugin()),
            Rows(Text())));
    }

    /// <summary>
    /// A route with no row is refused.
    ///
    /// The direction that costs an operator something: an endpoint they can call and cannot read
    /// about, which they then find by reading the source or by guessing.
    /// </summary>
    [Fact]
    public void ARouteWithNoRowIsRefused()
    {
        var findings = Findings(
            EndpointReflection.Discovered(EndpointReflection.Fixtures()),
            Array.Empty<Row>());

        Assert.Contains(
            "route-with-no-row: ElevatedFixtureController.Elevated",
            findings,
            StringComparer.Ordinal);

        Assert.Equal(4, findings.Count);
    }

    /// <summary>
    /// A row naming no route is refused.
    ///
    /// The other direction, and the one nobody notices: the endpoint was renamed or removed and
    /// the row stayed, so the document promises a call that answers nothing.
    /// </summary>
    [Fact]
    public void ARowNamingNoRouteIsRefused()
    {
        Assert.Equal(
            new[] { "row-naming-no-route: GoneController.Gone" },
            Findings(
                Array.Empty<Endpoint>(),
                new[] { new Row("GoneController.Gone", "GET", "Plugins/Gone", "RequiresElevation") }));
    }

    /// <summary>
    /// A row that describes a different call from the one the attribute declares is refused.
    ///
    /// Holding the names alone would let the verb and the route drift, and those two cells are
    /// exactly what somebody automating an installation copies. A row that has the endpoint's name
    /// right and its method wrong is worse than a missing row, because it is acted on.
    /// </summary>
    [Fact]
    public void ARowDescribingAnotherCallIsRefused()
    {
        var rows = RowsForEveryFixture()
            .Select(row => row.Name == "ElevatedFixtureController.Elevated"
                ? row with { Verb = "POST" }
                : row)
            .ToList();

        Assert.Contains(
            "row-describes-another-call: ElevatedFixtureController.Elevated is GET Plugins/WatchSyncFixture/Elevated and the row says POST Plugins/WatchSyncFixture/Elevated",
            Findings(EndpointReflection.Discovered(EndpointReflection.Fixtures()), rows),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A row that names an authorisation the attribute does not is refused.
    ///
    /// The cell a caller reads to find out what they have to present. A document saying an endpoint
    /// takes the server's default authorisation while the attribute requires elevation sends
    /// somebody to write a client that cannot work, and the failure they meet says nothing about
    /// the document.
    /// </summary>
    [Fact]
    public void ARowNamingAnotherAuthorisationIsRefused()
    {
        var rows = RowsForEveryFixture()
            .Select(row => row.Name == "ElevatedFixtureController.Elevated"
                ? row with { Authorisation = "default" }
                : row)
            .ToList();

        Assert.Contains(
            "row-names-another-authorisation: ElevatedFixtureController.Elevated is RequiresElevation and the row says default",
            Findings(EndpointReflection.Discovered(EndpointReflection.Fixtures()), rows),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An endpoint nothing authorises produces no finding here, because the guard that owns that
    /// refusal is #66's.
    ///
    /// It is the arm that keeps one refusal in one place. Without it this comparison reports an
    /// open endpoint as a row naming another authorisation, which is the same defect described
    /// wrongly: what is wrong is that nothing authorises the endpoint, and an operator reading
    /// this document's finding would go and correct the cell.
    ///
    /// Two of the fixtures are open, so the empty answer here is a statement about them rather
    /// than about a set that happens to have nothing in it.
    /// </summary>
    [Fact]
    public void AnEndpointNothingAuthorisesIsLeftToTheGuardThatOwnsIt()
    {
        var endpoints = EndpointReflection.Discovered(EndpointReflection.Fixtures());

        Assert.Equal(2, endpoints.Count(endpoint => endpoint.Policy is null));
        Assert.Empty(Findings(endpoints, RowsForEveryFixture()));
    }

    /// <summary>
    /// The comparison reads rows and not prose, so an endpoint named in a sentence does not count
    /// as documented.
    ///
    /// It is the shortcut somebody takes when the table is inconvenient: the endpoint is described
    /// in a paragraph, the guard passes, and the columns a caller needs are never written.
    /// </summary>
    [Fact]
    public void AnEndpointNamedInProseIsNotARow()
    {
        Assert.Empty(Rows(
            "The status is served by `WatchSyncController.Status`, a GET on Plugins/WatchSync/Status."));
    }

    /// <summary>
    /// No identifier in the document came from a real server.
    ///
    /// An example is copied out of whatever was to hand, and what is to hand on a working server is
    /// a real person's identifier. Once it is in the document it is in this repository's history and
    /// the person it belongs to was never asked. So the rule is held here rather than carried by
    /// whoever writes the next example.
    /// </summary>
    [Fact]
    public void NoIdentifierInTheDocumentCameFromARealServer()
    {
        Assert.Empty(Regex
            .Matches(
                Text(),
                @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(match => match.Value)
            .Where(identifier => !string.Equals(identifier, Placeholder, StringComparison.Ordinal))
            .Select(identifier =>
                $"{Document} carries the identifier {identifier}, which is not the placeholder, so it came from somewhere and nobody asked whoever it belongs to."));
    }

    /// <summary>
    /// The rule the document is written around is still in it.
    ///
    /// Weaker than the closures above and worth having anyway: it refuses the deletion of the
    /// section, which is how a document loses the half that says no. It refuses nothing about a
    /// rewrite that keeps the heading and changes what sits under it, and that bound is stated in
    /// the document itself rather than left for a reader to discover by trusting this.
    /// </summary>
    [Fact]
    public void TheDocumentStillCarriesTheRuleAboutTwoCausesThatAnswerTheSame()
    {
        var text = Text();

        Assert.Contains("deliberately answer the same", text, StringComparison.Ordinal);
        Assert.Contains("what they remove is a decision", text, StringComparison.Ordinal);
        Assert.Contains("as the caller receives it", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// What is wrong between a set of routes and a set of rows, one line per finding.
    /// </summary>
    /// <param name="endpoints">The routes.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>The findings, sorted.</returns>
    private static IReadOnlyList<string> Findings(
        IReadOnlyList<Endpoint> endpoints,
        IReadOnlyList<Row> rows)
    {
        var findings = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var row = rows.FirstOrDefault(each =>
                string.Equals(each.Name, endpoint.Name, StringComparison.Ordinal));

            if (row is null)
            {
                findings.Add($"route-with-no-row: {endpoint.Name}");
                continue;
            }

            if (!string.Equals(row.Verb, endpoint.Verb, StringComparison.Ordinal)
                || !string.Equals(row.Route, endpoint.Route, StringComparison.Ordinal))
            {
                findings.Add(
                    $"row-describes-another-call: {endpoint.Name} is {endpoint.Verb} {endpoint.Route} and the row says {row.Verb} {row.Route}");
            }

            if (endpoint.Policy is not null
                && !string.Equals(row.Authorisation, endpoint.Policy, StringComparison.Ordinal))
            {
                findings.Add(
                    $"row-names-another-authorisation: {endpoint.Name} is {endpoint.Policy} and the row says {row.Authorisation}");
            }
        }

        findings.AddRange(rows
            .Where(row => !endpoints.Any(endpoint =>
                string.Equals(endpoint.Name, row.Name, StringComparison.Ordinal)))
            .Select(row => $"row-naming-no-route: {row.Name}"));

        findings.Sort(StringComparer.Ordinal);

        return findings;
    }

    /// <summary>
    /// A row for every fixture endpoint, so that a fact about one direction is not answered by
    /// findings from another.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> RowsForEveryFixture() =>
        EndpointReflection.Discovered(EndpointReflection.Fixtures())
            .Select(endpoint => new Row(
                endpoint.Name,
                endpoint.Verb,
                endpoint.Route,
                endpoint.Policy ?? "default"))
            .ToList();

    /// <summary>
    /// The rows of the table, read as rows rather than as a search over the document.
    ///
    /// The row ends on optional whitespace rather than on the bar, which is the spelling
    /// <c>OptOutDocumentTests</c> arrived at the hard way: a checkout carries a carriage return
    /// before the newline on one of the three platforms the suite runs on, so a pattern anchored
    /// straight after the last bar matches every row on two platforms and no row on the third, and
    /// a table read as empty agrees with an empty set of routes about nothing.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Rows(string text) =>
        Regex
            .Matches(
                text,
                @"^\|\s*`(?<name>[A-Za-z]+\.[A-Za-z]+)`\s*\|\s*(?<verb>[A-Z]+)\s*\|\s*(?<route>[^|]*?)\s*\|\s*(?<authorisation>[^|]*?)\s*\|[^|]*\|[^|]*\|[^|]*\|\s*$",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(5))
            .Select(match => new Row(
                match.Groups["name"].Value,
                match.Groups["verb"].Value,
                match.Groups["route"].Value,
                match.Groups["authorisation"].Value))
            .ToList();

    /// <summary>
    /// The document as it stands in the tree, read from the tracked file rather than from a copy in
    /// the output directory, because a copy proves the state of the file on the day it was copied.
    /// </summary>
    /// <returns>The text.</returns>
    private static string Text() =>
        File.ReadAllText(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "docs",
            "endpoints.md"));
}

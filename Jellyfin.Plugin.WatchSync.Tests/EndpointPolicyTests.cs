using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Every endpoint this plugin serves, the policy the server authorises it with, and the table
/// that has to name both, which is #66's first and second conditions.
///
/// An endpoint that forgot its attribute is indistinguishable from one that has it until
/// somebody calls it, and that is the sentence the issue opens on. What refuses it here is a
/// reflection rather than a reading: an endpoint with no authorisation is a finding, an endpoint
/// with no row is a finding, and a row naming no endpoint is a finding.
///
/// <para>
/// THE PLUGIN SERVES NO ENDPOINT TODAY AND THE TABLE HAS NO ROW, so the comparison over the real
/// pair is empty against empty and decides nothing on its own. That is the trap #66's first
/// reading names and it is why the comparison is a function over a set of types rather than a
/// search over one assembly: the fixtures carry an endpoint of each shape, so the reflection is
/// proven to find one, to refuse an open one and to refuse a disagreeing row before the plugin
/// has any. Without that a reflection that recognised nothing would pass today and would go on
/// passing on the day the first controller arrived.
/// </para>
///
/// <para>
/// WHAT AN ENDPOINT IS is decided here, because #112 wants a reflection over the same controllers
/// to hold a document against the routes and two reflections written separately disagree about
/// the population. It is a public method of a public type deriving from
/// <see cref="ControllerBase"/> that carries an attribute implementing
/// <see cref="IActionHttpMethodProvider"/>, which is what every <c>HttpGet</c>, <c>HttpPost</c>,
/// <c>HttpPut</c> and <c>HttpDelete</c> is. Naming the interface rather than the four attributes
/// is what keeps a verb nobody has used yet inside the population.
/// </para>
///
/// <para>
/// What this does not reach is #66's third and fourth conditions. A non-elevated caller being
/// refused, and a user-scoped endpoint ignoring an identifier in the request, are assertions
/// about a call rather than about a declaration, and there is nothing to call.
/// </para>
/// </summary>
public class EndpointPolicyTests
{
    /// <summary>
    /// Where the table lives, relative to the repository root.
    /// </summary>
    private const string Table = "Jellyfin.Plugin.WatchSync.Tests/Endpoints/policies.txt";

    /// <summary>
    /// One endpoint, as the reflection finds it.
    /// </summary>
    /// <param name="Name">The declaring type and method, as <c>Type.Method</c>.</param>
    /// <param name="Verb">The HTTP method.</param>
    /// <param name="Route">The route the attribute declares.</param>
    /// <param name="Policy">
    /// The policy the attribute names, <c>default</c> where it names none, or null where nothing
    /// authorises the endpoint at all.
    /// </param>
    private sealed record Endpoint(string Name, string Verb, string Route, string? Policy);

    /// <summary>
    /// One row of the table.
    /// </summary>
    /// <param name="Name">The declaring type and method.</param>
    /// <param name="Verb">The HTTP method.</param>
    /// <param name="Route">The route.</param>
    /// <param name="Policy">The policy.</param>
    private sealed record Row(string Name, string Verb, string Route, string Policy);

    /// <summary>
    /// The plugin's endpoints and the table name the same set, and each says the same policy.
    ///
    /// This is the fact that fails on the day an endpoint is added without an entry, which is
    /// what the condition asks for in those words. It is empty against empty today and it is the
    /// comparison rather than an assertion that there is nothing, so it starts biting the moment
    /// the first controller lands.
    /// </summary>
    [Fact]
    public void ThePluginsEndpointsAndTheTableNameTheSameSet()
    {
        Assert.Empty(Findings(Discovered(typeof(Plugin).Assembly.GetTypes()), Rows(TableText())));
    }

    /// <summary>
    /// The reflection finds an endpoint where there is one.
    ///
    /// This is the anti-trap and it is the reason the fixtures exist. Every other fact here is a
    /// comparison, and a comparison between two empty sets passes however broken the half that
    /// produces one of them is. A reflection that recognised no controller would satisfy the
    /// fact above today and would satisfy it on the day the first endpoint was written, which is
    /// exactly when it would be believed.
    /// </summary>
    [Fact]
    public void TheReflectionFindsAnEndpointWhereThereIsOne()
    {
        var found = Discovered(Fixtures());

        Assert.Equal(
            new[]
            {
                "ElevatedFixtureController.Elevated",
                "OpenFixtureController.Open",
                "OptedOutFixtureController.OptedOut",
                "UserScopedFixtureController.Mine",
            },
            found.Select(endpoint => endpoint.Name).OrderBy(name => name, StringComparer.Ordinal).ToList());

        Assert.Equal(
            "GET",
            Assert.Single(found.Where(endpoint => endpoint.Name.EndsWith(".Elevated", StringComparison.Ordinal))).Verb);
    }

    /// <summary>
    /// An endpoint nothing authorises is refused, and so is one whose type is authorised and
    /// whose method opts out again.
    ///
    /// The second is the one a reader scrolling past the type does not catch, because the type
    /// carries the attribute the reader is looking for and the method underneath it is open.
    /// </summary>
    [Fact]
    public void AnEndpointNothingAuthorisesIsRefused()
    {
        var findings = Findings(Discovered(Fixtures()), RowsForEveryFixture());

        Assert.Equal(
            new[]
            {
                "endpoint-with-no-authorisation: OpenFixtureController.Open",
                "endpoint-with-no-authorisation: OptedOutFixtureController.OptedOut",
            },
            findings.Where(finding => finding.StartsWith("endpoint-with-no-authorisation", StringComparison.Ordinal))
                .OrderBy(finding => finding, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    /// An endpoint with no row is refused, and a row naming no endpoint is refused.
    ///
    /// The two failures are opposite and cost differently. An endpoint with no row is one nobody
    /// decided the authorisation of, which is the whole of this issue. A row naming no endpoint
    /// survives every rename of the thing it was written for, and what it leaves is a table that
    /// reads as covering a surface it has lost track of.
    /// </summary>
    [Fact]
    public void AnEndpointWithNoRowAndARowWithNoEndpointAreBothRefused()
    {
        var missing = Findings(Discovered(Fixtures()), Array.Empty<Row>());

        Assert.Contains(
            "endpoint-with-no-row: ElevatedFixtureController.Elevated",
            missing,
            StringComparer.Ordinal);

        var stray = Findings(
            Array.Empty<Endpoint>(),
            new[] { new Row("GoneController.Gone", "GET", "Plugins/Gone", "RequiresElevation") });

        Assert.Equal(
            new[] { "row-naming-no-endpoint: GoneController.Gone" },
            stray);
    }

    /// <summary>
    /// An endpoint whose attribute does not say what its row says is refused.
    ///
    /// This is the direction that decides whether the table is worth reading. A table held to the
    /// names alone lets the policies drift apart, and somebody deciding whether an action is safe
    /// to expose reads the cell rather than the attribute.
    /// </summary>
    [Fact]
    public void AnEndpointAuthorisedOtherwiseThanItsRowSaysIsRefused()
    {
        var findings = Findings(
            Discovered(Fixtures()),
            RowsForEveryFixture()
                .Select(row => row.Name == "ElevatedFixtureController.Elevated"
                    ? row with { Policy = "default" }
                    : row)
                .ToList());

        Assert.Contains(
            "endpoint-not-authorised-as-the-row-says: ElevatedFixtureController.Elevated is RequiresElevation and the row says default",
            findings,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// What is wrong between a set of endpoints and a set of rows, one line per finding.
    ///
    /// It is a function over both sides rather than a search over the tree, so every direction is
    /// proven on inputs written here and none of them rests on the state of the plugin's own
    /// assembly, which is empty.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>The findings, sorted.</returns>
    private static IReadOnlyList<string> Findings(
        IReadOnlyList<Endpoint> endpoints,
        IReadOnlyList<Row> rows)
    {
        var findings = new List<string>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Policy is null)
            {
                findings.Add($"endpoint-with-no-authorisation: {endpoint.Name}");
            }

            var row = rows.FirstOrDefault(each =>
                string.Equals(each.Name, endpoint.Name, StringComparison.Ordinal));

            if (row is null)
            {
                findings.Add($"endpoint-with-no-row: {endpoint.Name}");
                continue;
            }

            if (endpoint.Policy is not null
                && !string.Equals(row.Policy, endpoint.Policy, StringComparison.Ordinal))
            {
                findings.Add(
                    $"endpoint-not-authorised-as-the-row-says: {endpoint.Name} is {endpoint.Policy} and the row says {row.Policy}");
            }

            if (!string.Equals(row.Verb, endpoint.Verb, StringComparison.Ordinal)
                || !string.Equals(row.Route, endpoint.Route, StringComparison.Ordinal))
            {
                findings.Add(
                    $"row-describes-another-route: {endpoint.Name} is {endpoint.Verb} {endpoint.Route} and the row says {row.Verb} {row.Route}");
            }
        }

        findings.AddRange(rows
            .Where(row => !endpoints.Any(endpoint =>
                string.Equals(endpoint.Name, row.Name, StringComparison.Ordinal)))
            .Select(row => $"row-naming-no-endpoint: {row.Name}"));

        findings.Sort(StringComparer.Ordinal);

        return findings;
    }

    /// <summary>
    /// The endpoints among a set of types.
    ///
    /// An endpoint is a public method of a public type deriving from <see cref="ControllerBase"/>
    /// that carries an attribute implementing <see cref="IActionHttpMethodProvider"/>. The
    /// interface is named rather than the four attributes so that a verb nobody has used here yet
    /// is inside the population rather than outside it, which is the direction a definition of
    /// this kind has to fail in.
    /// </summary>
    /// <param name="types">The types to look in.</param>
    /// <returns>The endpoints.</returns>
    private static IReadOnlyList<Endpoint> Discovered(IEnumerable<Type> types) =>
        types
            .Where(type => type.IsPublic && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => new { Type = type, Method = method, Verb = Verb(method) })
                .Where(each => each.Verb is not null)
                .Select(each => new Endpoint(
                    $"{each.Type.Name}.{each.Method.Name}",
                    each.Verb!,
                    Route(each.Method),
                    Policy(each.Type, each.Method))))
            .ToList();

    /// <summary>
    /// The HTTP method the attribute declares, or null where the method declares none and is
    /// therefore not an endpoint.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The verb.</returns>
    private static string? Verb(MethodInfo method) =>
        method
            .GetCustomAttributes(inherit: true)
            .OfType<IActionHttpMethodProvider>()
            .SelectMany(attribute => attribute.HttpMethods)
            .FirstOrDefault();

    /// <summary>
    /// The route the attribute declares, or the empty string where it declares none.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The route.</returns>
    private static string Route(MethodInfo method) =>
        method
            .GetCustomAttributes(inherit: true)
            .OfType<IRouteTemplateProvider>()
            .Select(attribute => attribute.Template)
            .FirstOrDefault(template => template is not null)
        ?? string.Empty;

    /// <summary>
    /// What authorises the endpoint: the policy an <c>Authorize</c> attribute names, <c>default</c>
    /// where it names none, or null where nothing authorises it.
    ///
    /// An <c>AllowAnonymous</c> anywhere on the pair answers null however many attributes sit
    /// beside it, because that is what the server does with it. The method is read before the
    /// type, so an authorised controller carrying one open action is the open action rather than
    /// the controller.
    /// </summary>
    /// <param name="type">The declaring type.</param>
    /// <param name="method">The method.</param>
    /// <returns>The policy, or null.</returns>
    private static string? Policy(Type type, MethodInfo method)
    {
        var attributes = method.GetCustomAttributes(inherit: true)
            .Concat(type.GetCustomAttributes(inherit: true))
            .ToList();

        if (attributes.OfType<IAllowAnonymous>().Any())
        {
            return null;
        }

        var authorised = attributes.OfType<AuthorizeAttribute>().ToList();

        if (authorised.Count == 0)
        {
            return null;
        }

        return authorised
            .Select(attribute => attribute.Policy)
            .FirstOrDefault(policy => !string.IsNullOrEmpty(policy))
            ?? "default";
    }

    /// <summary>
    /// The fixture controllers, which are the endpoints this suite proves the reflection on.
    /// </summary>
    /// <returns>The types.</returns>
    private static IReadOnlyList<Type> Fixtures() =>
        typeof(EndpointPolicyTests).Assembly
            .GetTypes()
            .Where(type => string.Equals(
                type.Namespace,
                "Jellyfin.Plugin.WatchSync.Tests.Endpoints",
                StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// A row for every fixture endpoint, so that a fact about one direction is not answered by
    /// findings from the other.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> RowsForEveryFixture() =>
        Discovered(Fixtures())
            .Select(endpoint => new Row(
                endpoint.Name,
                endpoint.Verb,
                endpoint.Route,
                endpoint.Policy ?? "default"))
            .ToList();

    /// <summary>
    /// The rows of the table, read as rows so a name mentioned in the comment above them does not
    /// count as one.
    /// </summary>
    /// <param name="text">The table.</param>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Rows(string text) =>
        text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(" :: ", StringSplitOptions.TrimEntries))
            .Where(fields => fields.Length == 4)
            .Select(fields => new Row(fields[0], fields[1], fields[2], fields[3]))
            .ToList();

    /// <summary>
    /// The table as it stands in the tree, read from the tracked file rather than from a copy in
    /// the output directory, because a copy proves the state of the file on the day it was
    /// copied.
    /// </summary>
    /// <returns>The text.</returns>
    private static string TableText() =>
        File.ReadAllText(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            Table.Replace('/', Path.DirectorySeparatorChar)));
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Tests.Endpoints;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Every endpoint this plugin serves is reachable from the configuration page, which is the half
/// of #74's first condition that no reflection over the controllers can see.
///
/// An endpoint nobody can reach from the page is an endpoint an operator has to call with a
/// terminal, and that is the state #74 exists against: a person asks what is held about them and
/// the answer is an administrator doing something by hand. The two guards beside this one hold the
/// routes against the policy table and against the document; neither of them opens the page.
///
/// <para>
/// The routes are derived from the attributes rather than typed here, so a route renamed on the
/// controller and not on the page reddens this rather than leaving a button that answers nothing.
/// A route carries a placeholder the page fills in, so what is compared is the literal parts of
/// the template on either side of it.
/// </para>
///
/// <para>
/// WHAT THIS CANNOT SEE, before anybody reads a green run as the page working. It reads markup and
/// runs no script, so it holds that the page names each route's literal parts and each verb, and
/// never that the page pairs the right verb with the right route, that either call is wired to a
/// control, or that a person pressing the control gets an answer. That last one is a browser and
/// a running server, which is #46's first condition and #122, and no reading of this tree reaches
/// it.
/// </para>
/// </summary>
public class ConfigurationPageActionsTests
{
    /// <summary>
    /// Every endpoint of the plugin is named by the page.
    ///
    /// It is empty against a page today only in the sense that the plugin has two endpoints and
    /// both are #74's. It is a comparison rather than an assertion about those two, so the third
    /// endpoint reddens it on the day it lands without a control, which is #62 and #64.
    /// </summary>
    [Fact]
    public void EveryEndpointThisPluginServesIsReachableFromThePage()
    {
        Assert.Empty(Findings(EndpointReflection.Discovered(EndpointReflection.ThePlugin()), ThePage()));
    }

    /// <summary>
    /// A page that does not name a route is refused, and the finding says which part is missing.
    ///
    /// The near miss is the shape somebody produces: the route is renamed on the controller, the
    /// page keeps the old address, and every other guard in this repository stays green because
    /// the controller, the table and the document were all edited together.
    /// </summary>
    [Fact]
    public void APageThatDoesNotNameARouteIsRefused()
    {
        const string Page = "ApiClient.getUrl('Plugins/WatchSync/People/' + person + '/Records')";

        var endpoints = EndpointReflection.Discovered(EndpointReflection.ThePlugin());
        var parts = endpoints.SelectMany(endpoint => LiteralParts(endpoint.Route)).ToList();

        var findings = Findings(endpoints, Page)
            .Where(finding => finding.StartsWith("the page does not name", StringComparison.Ordinal))
            .ToList();

        // The misspelt part is the one the near miss is about, and every other finding names a
        // literal part of a route this plugin serves. The routes no longer share one part, so the
        // second half is held against the routes rather than against the one part the first
        // controller happened to have.
        Assert.Contains(
            findings,
            finding => finding.Contains("Plugins/WatchSync/Persons/", StringComparison.Ordinal));
        Assert.All(
            findings,
            finding => Assert.Contains(parts, part => finding.Contains($"name {part},", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A page that names the route and not the verb is refused.
    ///
    /// This is the half a reader assumes is covered by the one above and is not. A page can carry
    /// the right address and ask for it with the wrong method, and what an operator then meets is
    /// a control that reports a failure for a server that is working.
    /// </summary>
    [Fact]
    public void APageThatNamesTheRouteAndNotTheVerbIsRefused()
    {
        const string Page =
            "ApiClient.getUrl('Plugins/WatchSync/Persons/' + person + '/Records');"
            + "ApiClient.ajax({ type: 'GET' })";

        Assert.Contains(
            Findings(EndpointReflection.Discovered(EndpointReflection.ThePlugin()), Page),
            finding => finding.Contains("DELETE", StringComparison.Ordinal));
    }

    /// <summary>
    /// The page says that removing these records does not remove what the person watched.
    ///
    /// #74's own body says the wording matters because a person told their data was deleted has
    /// been told something specific. The note in <c>docs/privacy.md</c> carries it for a reader of
    /// the documents; this is the surface where somebody presses the control, and it is the one
    /// place the sentence is read at the moment it matters.
    ///
    /// It refuses the deletion of the sentence and refuses nothing about a rewrite that keeps the
    /// words and changes what they mean, which is the bound every wording fact in this repository
    /// carries.
    /// </summary>
    [Fact]
    public void ThePageSaysTheRemovalLeavesTheWatchHistoryAlone()
    {
        var page = ThePage();

        Assert.Contains("does not remove what that person watched", page, StringComparison.Ordinal);
        Assert.Contains("belongs to the server and stays", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing the page shows is built as markup.
    ///
    /// What comes back from the report carries values this plugin stored, and some of those
    /// arrived from another server, so a page assembling markup out of them would be running a
    /// peer's string inside a dashboard an administrator is signed in to. That is #63 met on the
    /// surface it is about, and the repair is that everything written into the page goes in as
    /// text.
    /// </summary>
    [Fact]
    public void ThePageWritesWhatItShowsAsTextAndNeverAsMarkup()
    {
        Assert.DoesNotContain("innerHTML", ThePage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What is wrong between a set of endpoints and a page, one line per finding.
    ///
    /// A function over both sides, so each direction is proven on markup written in this file and
    /// none of it rests on the page as it happens to stand.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <param name="markup">The page.</param>
    /// <returns>The findings, sorted.</returns>
    private static IReadOnlyList<string> Findings(
        IReadOnlyList<Endpoint> endpoints,
        string markup)
    {
        var findings = new List<string>();

        foreach (var endpoint in endpoints)
        {
            foreach (var part in LiteralParts(endpoint.Route))
            {
                if (!markup.Contains(part, StringComparison.Ordinal))
                {
                    findings.Add(
                        $"the page does not name {part}, which {endpoint.Name} serves as part of {endpoint.Route}");
                }
            }

            if (!markup.Contains($"'{endpoint.Verb}'", StringComparison.Ordinal))
            {
                findings.Add(
                    $"the page asks for no {endpoint.Verb}, which is what {endpoint.Name} answers");
            }
        }

        findings.Sort(StringComparer.Ordinal);

        return findings;
    }

    /// <summary>
    /// The parts of a route template that are literal, which are what a page carrying the address
    /// has to contain around whatever it fills the placeholders with.
    /// </summary>
    /// <param name="route">The template.</param>
    /// <returns>The literal parts, without the empty ones.</returns>
    private static IReadOnlyList<string> LiteralParts(string route) =>
        Regex
            .Split(route, "\\{[^}]*\\}", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Where(part => part.Length > 0)
            .ToList();

    /// <summary>
    /// The page as the plugin serves it, read out of the assembly rather than off disk, because
    /// what an operator meets is the embedded resource.
    /// </summary>
    /// <returns>The markup.</returns>
    private static string ThePage()
    {
        var resource = typeof(Plugin).Namespace + ".Configuration.configPage.html";

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);

        return reader.ReadToEnd();
    }
}

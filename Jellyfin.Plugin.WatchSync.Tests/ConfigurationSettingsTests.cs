using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Tests.Configuration;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the four places a setting is written down to each other: the configuration type the
/// server stores, the table in <c>docs/configuration.md</c>, the controls on the page, and the
/// fixture the facts about the reader walk.
///
/// This is #58's second condition with a subject. That condition asks that every setting in the
/// table exist and every setting that exists be in the table, and until settings existed the
/// closure that could be written ran over every number the plugin declares, which is
/// <c>ConfigurationDocumentTests</c> and stays where it is. This one runs over the settings
/// themselves and each direction of it costs somebody something different.
///
/// A setting the table does not carry is one an operator finds on a page with no explanation of
/// what it decides. A row naming no setting is worse, because it is trusted: somebody reads that
/// this plugin can be told how long to keep a conflict, and there is nothing to tell. A default
/// quoted in the table and not in the source answers a reader confidently with a number nobody
/// set. And a bound on the page that is not the rule's bound is the one an operator only meets
/// as a saved value that then turns out to be refused.
/// </summary>
public class ConfigurationSettingsTests
{
    /// <summary>
    /// Where the table lives, relative to the repository root.
    /// </summary>
    private const string Document = "docs/configuration.md";

    private static readonly Regex _row = new Regex(
        @"^\|\s*`(?<setting>[A-Za-z]+)`\s*\|\s*(?<unit>[a-z ]+?)\s*\|\s*(?<default>-?[0-9]+)\s*\|\s*`(?<carries>[A-Za-z]+\.[A-Za-z]+)`\s*\|\s*(?<smallest>1|`[A-Za-z]+\.[A-Za-z]+`)\s*\|\s*`(?<largest>[A-Za-z]+\.[A-Za-z]+)`\s*\|\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A row of the wider table, read for the two cells this file is about: the number it names
    /// and the home it gives it.
    /// </summary>
    private static readonly Regex _homeRow = new Regex(
        @"^\|\s*`(?<number>[A-Za-z]+\.[A-Za-z]+)`\s*\|[^|]*\|[^|]*\|[^|]*\|\s*(?<home>[^|]*?)\s*\|[^|]*\|\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _control = new Regex(
        "<input\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The fixture the facts about the reader walk names exactly the settings the configuration
    /// type declares.
    ///
    /// This fact is what makes the fixture usable at all. It carries the unit and the consuming
    /// rule, which reflection cannot recover, so it is a second list beside the type; without
    /// this comparison a setting added to the type would simply be absent from every fact that
    /// walks the fixture, and the suite would go green over a setting nothing had judged.
    /// </summary>
    [Fact]
    public void TheFixtureNamesExactlyTheSettingsTheTypeDeclares()
    {
        Assert.Equal(
            Declared().Keys.OrderBy(name => name, StringComparer.Ordinal),
            Settings.All.Select(setting => setting.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every setting defaults to the value the rule that consumes it declares.
    ///
    /// The configuration type derives each default from that declaration rather than repeating
    /// it, so this is the fact that fails if somebody replaces a derivation with the number it
    /// currently produces and the rule then moves. What that would produce is a plugin whose
    /// page offers one default and whose rule uses another, with nothing in either place saying
    /// they disagree.
    /// </summary>
    [Fact]
    public void EverySettingDefaultsToWhatItsRuleDeclares()
    {
        var untouched = new PluginConfiguration();

        Assert.Empty(Settings.All
            .Where(setting => Value(untouched, setting.Name) != setting.Default)
            .Select(setting =>
                $"{setting.Name} defaults to {Value(untouched, setting.Name)} and its rule declares {setting.Default}"));
    }

    /// <summary>
    /// Every setting the type declares has a row in the table.
    /// </summary>
    [Fact]
    public void EverySettingHasARow()
    {
        var rows = Rows();

        Assert.NotEmpty(rows);

        Assert.Empty(Declared().Keys
            .Where(name => !rows.Any(row => string.Equals(row.Setting, name, StringComparison.Ordinal)))
            .Select(name =>
                $"{name} is a setting an operator can change and {Document} has no row for it, so nothing says what it decides."));
    }

    /// <summary>
    /// Every row names a setting the type declares.
    /// </summary>
    [Fact]
    public void EveryRowNamesASettingTheTypeDeclares()
    {
        var declared = Declared();

        Assert.Empty(Rows()
            .Where(row => !declared.ContainsKey(row.Setting))
            .Select(row =>
                $"{Document} carries a row for {row.Setting} and the configuration type declares no such setting, so the table promises something an operator cannot change."));
    }

    /// <summary>
    /// Every row quotes the default, the unit, the rule it carries and the rule that bounds it,
    /// as the sources declare them.
    ///
    /// The names alone would let the numbers drift, and the number is what a reader opens this
    /// table for.
    /// </summary>
    [Fact]
    public void EveryRowQuotesWhatTheSourcesDeclare()
    {
        var byName = Settings.All.ToDictionary(setting => setting.Name, StringComparer.Ordinal);

        Assert.Empty(Rows()
            .Where(row => byName.ContainsKey(row.Setting))
            .Select(row => (Row: row, Setting: byName[row.Setting]))
            .Where(pair => Disagreement(pair.Row, pair.Setting) is not null)
            .Select(pair => $"{Document}: {Disagreement(pair.Row, pair.Setting)}"));
    }

    /// <summary>
    /// Every control on the page refuses, in the browser, exactly what the reader refuses on the
    /// server.
    ///
    /// The page carries the bound twice over: as a number in an attribute and as the rule the
    /// server applies afterwards. Those are two different things and both are needed - a browser
    /// bound is what stops somebody typing a value at all, and the server bound is what holds
    /// when the request did not come from the page - so the repair is not to delete one but to
    /// refuse them disagreeing. A page whose maximum is above the rule's lets an operator save a
    /// value the server then refuses, with nothing on the page saying which of the two was
    /// right.
    /// </summary>
    [Fact]
    public void EveryControlCarriesTheBoundItsRuleDeclares()
    {
        var controls = ControlsOn(ThePage());
        var findings = new List<string>();

        foreach (var setting in Settings.All)
        {
            if (!controls.TryGetValue(setting.Name, out var found))
            {
                findings.Add($"the page carries no bounded numeric control for {setting.Name}");

                continue;
            }

            if (found.Minimum != setting.Minimum || found.Maximum != setting.Maximum)
            {
                findings.Add(
                    $"the page bounds {setting.Name} at {found.Minimum} to {found.Maximum} and its rule accepts {setting.Minimum} to {setting.Maximum}");
            }
        }

        Assert.Empty(findings);
    }

    /// <summary>
    /// The number a setting carries is homed in the plugin configuration by the wider table.
    ///
    /// The two tables in this document are about the same numbers from two directions, and until
    /// this fact nothing held them together. Moving a setting's row in the settings table while
    /// its number went on saying `pairing state` in the wider one reddened nothing, which was
    /// found by making that edit rather than by reading the file: the document would then say in
    /// one place that an operator sets a number on this page and in another that it belongs beside
    /// a pairing, and both halves would be closed against the sources.
    ///
    /// It is the home rather than the whole row, because everything else about the number is
    /// already held by the fact above and by the closure in
    /// <c>ConfigurationDocumentTests</c>.
    /// </summary>
    [Fact]
    public void EverySettingsNumberIsHomedInThePluginConfiguration()
    {
        var homes = Homes();

        Assert.NotEmpty(homes);

        Assert.Empty(Rows()
            .Where(row => !string.Equals(
                homes.GetValueOrDefault(row.Carries),
                "plugin configuration",
                StringComparison.Ordinal))
            .Select(row =>
                $"{Document} says {row.Setting} carries {row.Carries}, and the table of every number gives {row.Carries} the home '{homes.GetValueOrDefault(row.Carries) ?? "none"}' rather than the plugin configuration"));
    }

    /// <summary>
    /// The section that says which numbers are homed in the plugin configuration and are still
    /// not settings is in the document.
    ///
    /// It refuses the deletion of the passage and nothing about a rewrite of it, which is the
    /// bound the other facts of this family in this repository state about themselves. It is
    /// worth having because that passage is the one a reader of the big table needs: four rows
    /// there say a number will live in the plugin configuration, and without it the only way to
    /// find out that none of the four is a setting yet is to notice it is absent from the table
    /// above.
    /// </summary>
    [Fact]
    public void TheDocumentSaysWhichHomedNumbersAreStillNotSettings()
    {
        var text = Text();

        Assert.Contains("What is not a setting yet", text, StringComparison.Ordinal);
        Assert.Contains("Nothing consumes the settings", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row and a setting that disagree, as the sentence a reader of the failure gets, or null
    /// where they agree.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="setting">The setting.</param>
    /// <returns>The disagreement, or null.</returns>
    private static string? Disagreement(Row row, Settings.Setting setting)
    {
        var unit = setting.InUnit == Settings.Unit.PerCent
            ? "per cent"
            : setting.InUnit.ToString().ToLowerInvariant();

        if (!string.Equals(row.Unit, unit, StringComparison.Ordinal))
        {
            return $"{row.Setting} is in {row.Unit} and the type stores it in {unit}";
        }

        if (row.Default != setting.Default)
        {
            return $"{row.Setting} defaults to {row.Default} and the rule declares {setting.Default}";
        }

        if (!Declares(row.Carries, setting.DeclaredDefault))
        {
            return $"{row.Setting} says it carries {row.Carries}, which is not a number declaring {setting.DeclaredDefault}";
        }

        if (!Declares(row.Largest, setting.DeclaredBound))
        {
            return $"{row.Setting} says it is bounded by {row.Largest}, which is not a number declaring {setting.DeclaredBound}";
        }

        if (setting.DeclaredFloor is null)
        {
            return string.Equals(row.Smallest, "1", StringComparison.Ordinal)
                ? null
                : $"{row.Setting} says it is floored by {row.Smallest} and no rule declares a floor for it";
        }

        return Declares(row.Smallest, setting.DeclaredFloor)
            ? null
            : $"{row.Setting} says it is floored by {row.Smallest}, which is not a number declaring {setting.DeclaredFloor}";
    }

    /// <summary>
    /// Whether a number named as <c>Type.Member</c> in the table declares the given value.
    ///
    /// Resolved through the assembly rather than compared as text, so a row naming a member that
    /// was renamed fails here rather than going on describing it.
    /// </summary>
    /// <param name="number">The number, as <c>Type.Member</c>.</param>
    /// <param name="value">The value it has to declare.</param>
    /// <returns>Whether it does.</returns>
    private static bool Declares(string number, object value)
    {
        var parts = number.Split('.');

        if (parts.Length != 2)
        {
            return false;
        }

        var property = typeof(PluginConfiguration).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && string.Equals(type.Name, parts[0], StringComparison.Ordinal))
            .Select(type => type.GetProperty(
                parts[1],
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .FirstOrDefault(found => found is not null);

        return property is not null && Equals(property.GetValue(null), value);
    }

    /// <summary>
    /// The settings the configuration type declares, with the value each one has on an untouched
    /// document, by reflection rather than from a list.
    /// </summary>
    /// <returns>The settings, by name.</returns>
    private static IReadOnlyDictionary<string, PropertyInfo> Declared() =>
        typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite)
            .ToDictionary(property => property.Name, property => property, StringComparer.Ordinal);

    /// <summary>
    /// The value one setting has on a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The setting.</param>
    /// <returns>The value.</returns>
    private static int Value(PluginConfiguration document, string name) =>
        (int)Declared()[name].GetValue(document)!;

    /// <summary>
    /// The home the wider table gives each number it names.
    /// </summary>
    /// <returns>The homes, by number.</returns>
    private static IReadOnlyDictionary<string, string> Homes() =>
        _homeRow
            .Matches(Text())
            .ToDictionary(
                match => match.Groups["number"].Value,
                match => match.Groups["home"].Value,
                StringComparer.Ordinal);

    /// <summary>
    /// The rows of the settings table.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Rows() =>
        _row
            .Matches(Text())
            .Select(match => new Row(
                match.Groups["setting"].Value,
                match.Groups["unit"].Value,
                int.Parse(match.Groups["default"].Value, CultureInfo.InvariantCulture),
                match.Groups["carries"].Value,
                match.Groups["smallest"].Value.Trim('`'),
                match.Groups["largest"].Value))
            .ToList();

    /// <summary>
    /// The numeric controls the page carries, by the identifier they are bound to.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <returns>The bounds each control declares.</returns>
    private static IReadOnlyDictionary<string, (int Minimum, int Maximum)> ControlsOn(string html)
    {
        var found = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        foreach (Match control in _control.Matches(html))
        {
            var identifier = Attribute(control.Value, "id");
            var minimum = Attribute(control.Value, "min");
            var maximum = Attribute(control.Value, "max");

            if (identifier is null || minimum is null || maximum is null)
            {
                continue;
            }

            found[identifier] = (
                int.Parse(minimum, CultureInfo.InvariantCulture),
                int.Parse(maximum, CultureInfo.InvariantCulture));
        }

        return found;
    }

    /// <summary>
    /// One attribute of one element, or null where it carries none.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute.</param>
    /// <returns>The value, or null.</returns>
    private static string? Attribute(string element, string name)
    {
        var match = Regex.Match(
            element,
            "\\b" + name + "\\s*=\\s*[\"']([^\"']*)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// The page as the plugin serves it, read out of the assembly rather than off disk.
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

    /// <summary>
    /// The document as it stands in the tree.
    /// </summary>
    /// <returns>The text.</returns>
    private static string Text() =>
        File.ReadAllText(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "docs",
            "configuration.md"));

    /// <summary>
    /// A row of the settings table.
    /// </summary>
    /// <param name="Setting">The member on the configuration document.</param>
    /// <param name="Unit">The unit the row says it is stored in.</param>
    /// <param name="Default">The default the row quotes.</param>
    /// <param name="Carries">The number the row says the default comes from.</param>
    /// <param name="Smallest">
    /// The number the row says floors it, or the literal 1 where the floor is one of the setting's
    /// own unit rather than a number a rule declares.
    /// </param>
    /// <param name="Largest">The number the row says bounds it from above.</param>
    private sealed record Row(
        string Setting,
        string Unit,
        int Default,
        string Carries,
        string Smallest,
        string Largest);
}

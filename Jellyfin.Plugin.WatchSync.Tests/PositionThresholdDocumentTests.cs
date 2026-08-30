using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the position threshold table in the sync model document and the type that declares the
/// numbers to each other.
///
/// #17 asks that each threshold be a setting with a default and a stated reason for that default
/// in <c>docs/sync-model.md</c>. A default written in two places drifts, and it drifts in the
/// direction that costs most: the number a person reads before deciding whether to change a
/// setting is the one in the document, and the number a server behaves by is the one in the
/// type. This refuses the two disagreeing in either direction.
///
/// The declared set is read off the type by reflection rather than listed here, so a fourth
/// threshold added to the rule arrives with no row and reddens this suite instead of being a
/// number nothing explains.
///
/// The same condition says each threshold is a setting, and that half is held here too. The
/// document said the opposite of the truth for as long as it took #58 to land the settings and
/// nobody to come back to this section: it went on saying none of the three was a setting yet
/// while an operator could change all three on the page. A sentence saying a setting exists is
/// exactly the sentence a reader has no way to check, so the table names the setting that
/// carries each threshold and this file resolves the name against
/// <see cref="PluginConfiguration"/> rather than reading the prose around it.
/// </summary>
public class PositionThresholdDocumentTests
{
    /// <summary>
    /// The whole point, run against the table as it is. Every threshold the type declares has a
    /// row, no row names something the type does not declare, nothing is named twice, and every
    /// row states the number the rule would actually use.
    /// </summary>
    [Fact]
    public void TheTableStatesEveryThresholdAndTheNumberTheRuleUses()
    {
        var report = PositionThresholdDocument.Check(
            PositionThresholdDocument.Rows(PositionThresholdDocument.Text()),
            PositionThresholdDocument.Declared());

        Assert.Empty(report.Missing.Select(name =>
            $"{name} is a threshold the rule is bounded by and the table does not name it, so its default is a number nothing explains."));

        Assert.Empty(report.Unknown.Select(name =>
            $"{name} is named by the table and is not a threshold the rule declares, so its row is about nothing."));

        Assert.Empty(report.Repeated.Select(name =>
            $"{name} has more than one row, so which default the document states is undefined."));

        Assert.Empty(report.Disagreeing.Select(entry =>
            $"{entry.Name} reads as {entry.Stated} in the document and the rule uses {entry.Declared}."));
    }

    /// <summary>
    /// Every row names the setting an operator changes that threshold with, and that setting
    /// defaults to the number the rule declares.
    ///
    /// This is the half of #17's condition that is about the page rather than about the rule,
    /// and it is the half that had already gone stale once: the section went on saying none of
    /// the three was a setting yet while all three were on the page. A claim that a setting
    /// exists is worth nothing to a reader who cannot resolve the name, so the name is resolved
    /// against the configuration type here.
    ///
    /// The default is compared as well as the name, because a row naming a real setting that
    /// carries a different number is the mistake that survives a name check: the operator reads
    /// five minutes here, changes the setting the row sent them to, and moves something else.
    /// </summary>
    [Fact]
    public void EveryRowNamesTheSettingThatCarriesItAndThatSettingDefaultsToTheRulesNumber()
    {
        var report = PositionThresholdDocument.Check(
            PositionThresholdDocument.Rows(PositionThresholdDocument.Text()),
            PositionThresholdDocument.Declared());

        Assert.Empty(report.Unsettled.Select(name =>
            $"the table sends an operator to {name} and the configuration declares no such setting, so the threshold is described as one somebody can change and nobody can."));

        Assert.Empty(report.Undefaulted.Select(entry =>
            $"{entry.Name} is carried by a setting that defaults to {entry.Stated} and the rule declares {entry.Declared}, so the row names a setting that moves something else."));
    }

    /// <summary>
    /// Every row carries a reason for its default, which is the half of #17's condition a set
    /// comparison cannot see.
    ///
    /// A row with the right name and the right number and an empty reason satisfies everything
    /// above and leaves the document saying nothing about why five minutes rather than fifty.
    /// Whether the reason is a good reason is a reading at review; that one is there at all is
    /// this fact.
    /// </summary>
    [Fact]
    public void EveryRowCarriesAReasonForItsDefault()
    {
        var rows = PositionThresholdDocument.Rows(PositionThresholdDocument.Text());

        Assert.NotEmpty(rows);

        Assert.Empty(rows
            .Where(row => row.Why.Length == 0)
            .Select(row => $"{row.Name} states a default and no reason for it."));
    }

    /// <summary>
    /// The checker refuses the near miss and passes its repair.
    ///
    /// The fixture is a table where one row's number has drifted from the rule by a factor the
    /// reader has no way to notice: it is a plausible threshold, stated in the same units, in a
    /// row that is otherwise correct. Its repair changes that one number.
    /// </summary>
    [Fact]
    public void TheCheckerRefusesTheNearMissAndPassesItsRepair()
    {
        Assert.NotEmpty(PositionThresholdDocument.Check(
            PositionThresholdDocument.Rows(
                PositionThresholdDocument.Fixture("position-threshold-near-miss.txt")),
            PositionThresholdDocument.Declared()).Disagreeing);

        var repaired = PositionThresholdDocument.Check(
            PositionThresholdDocument.Rows(
                PositionThresholdDocument.Fixture("position-threshold-near-miss-repaired.txt")),
            PositionThresholdDocument.Declared());

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.Repeated);
        Assert.Empty(repaired.Disagreeing);
        Assert.Empty(repaired.Unsettled);
        Assert.Empty(repaired.Undefaulted);
    }

    /// <summary>
    /// A threshold the rule declares and the table does not name is refused, a row naming
    /// nothing the rule declares is refused, and a row written twice is refused.
    ///
    /// All three are driven off the repaired fixture rather than off the document, because a
    /// fact that mutated the real table would be measuring the table on the day it ran instead
    /// of measuring the comparison.
    /// </summary>
    [Fact]
    public void ARowMissingAnExtraRowAndARepeatedRowAreEachRefused()
    {
        var rows = PositionThresholdDocument.Rows(
            PositionThresholdDocument.Fixture("position-threshold-near-miss-repaired.txt"));

        var declared = PositionThresholdDocument.Declared();

        Assert.NotEmpty(PositionThresholdDocument
            .Check(rows.Skip(1).ToList(), declared)
            .Missing);

        Assert.NotEmpty(PositionThresholdDocument
            .Check(
                rows.Append(new PositionThresholdDocument.Row(
                    "pace",
                    "5 minutes",
                    "PositionMoveSeconds",
                    "a reason")).ToList(),
                declared)
            .Unknown);

        Assert.NotEmpty(PositionThresholdDocument
            .Check(rows.Append(rows[0]).ToList(), declared)
            .Repeated);
    }

    /// <summary>
    /// A row naming a setting nobody can change is refused, and so is one naming a real setting
    /// that carries a different number.
    ///
    /// Both are driven off the repaired fixture rather than off the document, for the reason the
    /// fact above gives about its own three.
    ///
    /// The second mutation is the near miss of this pair and it is the reason the comparison is
    /// not a name check. `EchoWindowSeconds` is a setting that exists, is declared beside these
    /// three, is spelled the way they are, and carries thirty seconds where the move threshold
    /// is five minutes. A row sending an operator there reads correctly and moves the wrong
    /// number.
    /// </summary>
    [Fact]
    public void ARowNamingNoSettingAndOneNamingASettingThatCarriesAnotherNumberAreEachRefused()
    {
        var rows = PositionThresholdDocument.Rows(
            PositionThresholdDocument.Fixture("position-threshold-near-miss-repaired.txt"));

        var declared = PositionThresholdDocument.Declared();

        Assert.NotEmpty(PositionThresholdDocument
            .Check(
                rows.Select(row => row with { Setting = "PositionMoveMinutes" }).ToList(),
                declared)
            .Unsettled);

        Assert.NotEmpty(PositionThresholdDocument
            .Check(
                rows.Select(row => row.Name == "move" ? row with { Setting = "EchoWindowSeconds" } : row).ToList(),
                declared)
            .Undefaulted);
    }

    /// <summary>
    /// Reading the table, reading the type, and refusing the two disagreeing.
    /// </summary>
    internal static class PositionThresholdDocument
    {
        /// <summary>
        /// One row of the threshold table.
        /// </summary>
        /// <param name="Name">The threshold the row is about.</param>
        /// <param name="Stated">The default as the document states it.</param>
        /// <param name="Setting">The setting the row says an operator changes it with.</param>
        /// <param name="Why">The reason the row gives for that default.</param>
        internal sealed record Row(string Name, string Stated, string Setting, string Why);

        /// <summary>
        /// One threshold whose stated default is not the one the rule would use.
        /// </summary>
        /// <param name="Name">The threshold.</param>
        /// <param name="Stated">What the document says.</param>
        /// <param name="Declared">What the type says.</param>
        internal sealed record Disagreement(string Name, TimeSpan Stated, TimeSpan Declared);

        /// <summary>
        /// What the comparison found.
        /// </summary>
        /// <param name="Missing">Thresholds the type declares and no row names.</param>
        /// <param name="Unknown">Rows naming nothing the type declares.</param>
        /// <param name="Repeated">Thresholds with more than one row.</param>
        /// <param name="Disagreeing">Rows whose number is not the one the type holds.</param>
        /// <param name="Unsettled">Rows naming a setting the configuration does not declare.</param>
        /// <param name="Undefaulted">Rows whose setting does not default to the row's threshold.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> Repeated,
            IReadOnlyList<Disagreement> Disagreeing,
            IReadOnlyList<string> Unsettled,
            IReadOnlyList<Disagreement> Undefaulted);

        /// <summary>
        /// Reads the rows of the threshold table.
        ///
        /// The second column has to be a number and a unit and the third a backticked name, which
        /// is what keeps this pattern off the other two tables in the same document: one of them
        /// carries a disposition there and the other a treatment, and neither is a duration.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<Row> Rows(string text) =>
            Regex
                .Matches(
                    text,
                    @"(?m)^\|\s*`(?<name>[A-Za-z]+)`\s*\|\s*(?<stated>\d+\s+[a-z]+)\s*\|\s*`(?<setting>[A-Za-z]+)`\s*\|(?<why>[^|]*)\|\s*$")
                .Select(match => new Row(
                    match.Groups["name"].Value,
                    match.Groups["stated"].Value.Trim(),
                    match.Groups["setting"].Value,
                    match.Groups["why"].Value.Trim()))
                .ToList();

        /// <summary>
        /// Reads the defaults off <see cref="PositionThresholds"/>.
        ///
        /// A static property returning a <see cref="TimeSpan"/> whose name begins with Default
        /// is a default, and the threshold it is about is the rest of its name with the first
        /// letter lowered, which is how the row names it. So a default added to the type is a
        /// row this document owes, without anything here being told about it.
        /// </summary>
        /// <returns>The declared defaults, by threshold.</returns>
        internal static IReadOnlyDictionary<string, TimeSpan> Declared() =>
            typeof(PositionThresholds)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(property =>
                    property.PropertyType == typeof(TimeSpan)
                    && property.Name.StartsWith("Default", StringComparison.Ordinal)
                    && property.Name.Length > "Default".Length)
                .ToDictionary(
                    property => Lowered(property.Name["Default".Length..]),
                    property => (TimeSpan)property.GetValue(null)!,
                    StringComparer.Ordinal);

        /// <summary>
        /// Reads the settings off <see cref="PluginConfiguration"/>, with the value each one has
        /// on a document nobody has touched.
        ///
        /// It is the configuration type rather than the page or the settings document, because
        /// what the table claims is that an operator can change the number, and the type is what
        /// the server stores. The other two are held to it elsewhere, by
        /// <c>ConfigurationSettingsTests</c>.
        /// </summary>
        /// <returns>The untouched value of every setting, by name.</returns>
        internal static IReadOnlyDictionary<string, int> Settings()
        {
            var untouched = new PluginConfiguration();

            return typeof(PluginConfiguration)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.CanRead && property.CanWrite && property.PropertyType == typeof(int))
                .ToDictionary(
                    property => property.Name,
                    property => (int)property.GetValue(untouched)!,
                    StringComparer.Ordinal);
        }

        /// <summary>
        /// Compares the rows against the declared defaults in both directions.
        /// </summary>
        /// <param name="rows">The rows of the table.</param>
        /// <param name="declared">The defaults the type declares.</param>
        /// <returns>What the comparison found.</returns>
        internal static Report Check(
            IReadOnlyList<Row> rows,
            IReadOnlyDictionary<string, TimeSpan> declared)
        {
            var named = rows.Select(row => row.Name).ToList();

            var repeated = named
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            var missing = declared.Keys
                .Where(name => !named.Contains(name, StringComparer.Ordinal))
                .ToList();

            var unknown = named
                .Where(name => !declared.ContainsKey(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var disagreeing = rows
                .Where(row => declared.ContainsKey(row.Name))
                .Select(row => new Disagreement(row.Name, Duration(row.Stated), declared[row.Name]))
                .Where(entry => entry.Stated != entry.Declared)
                .ToList();

            var settings = Settings();

            var unsettled = rows
                .Where(row => !settings.ContainsKey(row.Setting))
                .Select(row => row.Setting)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // The setting is whole seconds and the threshold is a span, and the comparison is
            // made in seconds because that is the unit every member of the configuration document
            // names in its own name. A threshold that arrives stored in some other unit is not
            // silently accepted here: its number would not be its second count, so it lands in
            // this list rather than passing as a setting nobody had compared.
            var undefaulted = rows
                .Where(row => declared.ContainsKey(row.Name) && settings.ContainsKey(row.Setting))
                .Select(row => new Disagreement(
                    row.Name,
                    TimeSpan.FromSeconds(settings[row.Setting]),
                    declared[row.Name]))
                .Where(entry => entry.Stated != entry.Declared)
                .ToList();

            return new Report(missing, unknown, repeated, disagreeing, unsettled, undefaulted);
        }

        /// <summary>
        /// Reads the document text.
        /// </summary>
        /// <returns>The text of the sync model document.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "docs",
                "sync-model.md"));

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Model",
                name));

        /// <summary>
        /// Turns a stated default into the duration it names.
        ///
        /// A unit the document has never used is refused rather than read as something else. A
        /// row saying five somethings that this reader silently took for five minutes would be
        /// a comparison that passed on a document nobody could act on.
        /// </summary>
        /// <param name="stated">The default as the document states it.</param>
        /// <returns>The duration.</returns>
        /// <exception cref="FormatException">The unit is not one this reader knows.</exception>
        private static TimeSpan Duration(string stated)
        {
            var parts = stated.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = double.Parse(parts[0], CultureInfo.InvariantCulture);

            return parts[1].TrimEnd('s') switch
            {
                "second" => TimeSpan.FromSeconds(count),
                "minute" => TimeSpan.FromMinutes(count),
                "hour" => TimeSpan.FromHours(count),
                _ => throw new FormatException(
                    $"{stated} states its default in a unit this reader does not know, so the number it names cannot be compared."),
            };
        }

        /// <summary>
        /// The same word with its first letter lowered.
        /// </summary>
        /// <param name="word">The word.</param>
        /// <returns>The word, lowered.</returns>
        private static string Lowered(string word) =>
            char.ToLowerInvariant(word[0]) + word[1..];
    }
}

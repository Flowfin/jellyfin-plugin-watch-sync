using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the settings document to the numbers the plugin actually declares, which is #58's second
/// condition.
///
/// That condition asks that every setting in the table exist and every setting that exists be in
/// the table, so the two cannot drift. The subject it was written against does not exist yet:
/// nothing in this plugin is a setting, the configuration type is empty, and a closure over an
/// empty set is green and discriminates nothing.
///
/// So the population here is wider than the condition's word and is the one that can be read: every
/// public static number the plugin declares. Each is either a default waiting for the setting that
/// will carry it, a bound refusing what that setting may be, or a number that is deliberately never
/// going to be a setting. The table says which, and the document beside it says why. Widening the
/// population is what gives the closure something to be about today, and it is what makes the
/// fourth condition - a setting that would turn a refusal into a guess is named as deliberately
/// absent - a list a machine keeps rather than a sentence.
///
/// The value is compared as well as the name, because a name-only closure passes a number moved in
/// the sources while the table goes on quoting what it used to be, and a table quoting a number
/// nobody set is worse than no table: it is trusted.
/// </summary>
public class ConfigurationDocumentTests
{
    /// <summary>
    /// Where the table lives, relative to the repository root.
    /// </summary>
    private const string Document = "docs/configuration.md";

    /// <summary>
    /// The homes a row may declare. Three of them are where a setting lives, and the rest are the
    /// four different refusals to make something a setting.
    ///
    /// The set is closed on purpose. A home nobody listed here is a category invented in a table
    /// cell, and the argument for it would live in that cell rather than in the section of the
    /// document that has to carry it.
    /// </summary>
    private static readonly IReadOnlyList<string> _homes = new[]
    {
        "plugin configuration",
        "pairing state",
        "user record",
        "bound on a setting",
        "deliberately absent",
        "another tree",
        "derived",
    };

    /// <summary>
    /// A row of the table: the number it names, the value it quotes and the home it declares.
    /// </summary>
    /// <param name="Number">The declaring type and member, as <c>Type.Member</c>.</param>
    /// <param name="Value">The value the row quotes, as written.</param>
    /// <param name="Home">Where the row says the number lives.</param>
    private sealed record Row(string Number, string Value, string Home);

    /// <summary>
    /// Every number the plugin declares has a row.
    ///
    /// This is the direction that costs something. A number with no row is one an operator is never
    /// told about and one nobody decided the home of, and the issues that add the next settings
    /// each add a number before they add a page, so this is the moment the decision is cheap.
    /// </summary>
    [Fact]
    public void EveryNumberThePluginDeclaresHasARow()
    {
        var rows = Rows(Text());

        Assert.NotEmpty(Declared());
        Assert.NotEmpty(rows);

        Assert.Empty(Declared()
            .Where(number => !rows.Any(row => string.Equals(row.Number, number.Key, StringComparison.Ordinal)))
            .Select(number =>
                $"{number.Key} is declared in the plugin's sources and {Document} has no row for it, so nobody has said where it lives or whether it is deliberately not a setting."));
    }

    /// <summary>
    /// The other direction. A row naming nothing is a setting an operator reads about and cannot
    /// find, and it survives every rename of the thing it was written for.
    /// </summary>
    [Fact]
    public void EveryRowNamesANumberThePluginDeclares()
    {
        var declared = Declared();

        Assert.Empty(Rows(Text())
            .Where(row => !declared.ContainsKey(row.Number))
            .Select(row =>
                $"{Document} carries a row for {row.Number} and the plugin declares nothing by that name, so the table promises a number that is not there."));
    }

    /// <summary>
    /// Every row quotes the value the source declares.
    ///
    /// Without this the closure holds the two lists of names together and lets the numbers drift
    /// apart, which is the drift that matters: a reader consults this table precisely to find out
    /// what a default is, and a stale cell answers them confidently.
    /// </summary>
    [Fact]
    public void EveryRowQuotesTheValueTheSourceDeclares()
    {
        var declared = Declared();

        Assert.Empty(Rows(Text())
            .Where(row => declared.ContainsKey(row.Number))
            .Where(row => !string.Equals(Written(declared[row.Number]), row.Value, StringComparison.Ordinal))
            .Select(row =>
                $"{Document} says {row.Number} is {row.Value} and the source declares {Written(declared[row.Number])}, so the table answers a reader with a number nobody set."));
    }

    /// <summary>
    /// Every row declares one of the homes the document argues for.
    ///
    /// A home spelled some other way is not a typo to be tidied. It is a category whose reason
    /// exists only in the cell that names it, and the whole point of this document is that where a
    /// setting lives is decided once and argued in one place.
    /// </summary>
    [Fact]
    public void EveryRowDeclaresAHomeTheDocumentArguesFor()
    {
        Assert.Empty(Rows(Text())
            .Where(row => !_homes.Contains(row.Home, StringComparer.Ordinal))
            .Select(row =>
                $"{Document} gives {row.Number} the home '{row.Home}', which is not one the document argues for, so the reason for it would live in that cell and nowhere else."));
    }

    /// <summary>
    /// The rule this document exists to fix is still in it: a setting that would turn a refusal
    /// into a guess is named rather than left out.
    ///
    /// This is a weaker fact than the closures above and is worth having anyway, because it refuses
    /// the deletion of the section, which is how a document loses the half that says no. It refuses
    /// nothing about a rewrite that keeps the heading and changes what sits under it, and that
    /// bound is written here rather than left for a reader to find out by trusting the fact.
    /// </summary>
    [Fact]
    public void TheDocumentStillCarriesTheRefusalItIsWrittenAround()
    {
        var text = Text();

        Assert.Contains("A refusal does not become a guess", text, StringComparison.Ordinal);
        Assert.Contains("deliberately absent", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rows of the table, read as rows rather than as a search over the document, so a number
    /// mentioned in prose does not count as having been given a home.
    ///
    /// The row ends on optional whitespace rather than on the bar, which is the spelling
    /// <c>OptOutDocumentTests</c> arrived at the hard way: the checkout carries a carriage return
    /// before the newline on one of the three platforms the suite runs on, so a pattern anchored
    /// straight after the last bar matches every row on two platforms and no row on the third, and
    /// a table read as empty agrees with an empty set of sources about nothing.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Rows(string text) =>
        Regex
            .Matches(
                text,
                @"^\|\s*`(?<number>[A-Za-z]+\.[A-Za-z]+)`\s*\|[^|]*\|\s*(?<value>[^|]*?)\s*\|[^|]*\|\s*(?<home>[^|]*?)\s*\|[^|]*\|\s*$",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(5))
            .Select(match => new Row(
                match.Groups["number"].Value,
                match.Groups["value"].Value,
                match.Groups["home"].Value))
            .ToList();

    /// <summary>
    /// Every public static number the plugin declares, keyed as <c>Type.Member</c>, found by
    /// reflection rather than listed here.
    ///
    /// A list in this file would be the drift the closure refuses, one level in: whoever adds the
    /// next bound would add it here as readily as to the table, and the two would then agree about
    /// a tree neither had read.
    ///
    /// Properties and fields together, because the tree spells these both ways today and which
    /// spelling a number takes is a matter of whether it is computed. A number nobody can reach is
    /// not one an operator is owed an answer about, so the scan is over public types and public
    /// members and stops there.
    /// </summary>
    /// <returns>The numbers, by name.</returns>
    private static IReadOnlyDictionary<string, object> Declared()
    {
        var numbers = new[] { typeof(int), typeof(long), typeof(double), typeof(TimeSpan) };

        var properties = typeof(EnvelopeBounds).Assembly
            .GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(property => numbers.Contains(property.PropertyType) && property.GetMethod is not null)
                .Select(property => new KeyValuePair<string, object>(
                    $"{type.Name}.{property.Name}",
                    property.GetValue(null)!)));

        var fields = typeof(EnvelopeBounds).Assembly
            .GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => numbers.Contains(field.FieldType))
                .Select(field => new KeyValuePair<string, object>(
                    $"{type.Name}.{field.Name}",
                    field.GetValue(null)!)));

        return properties.Concat(fields).ToDictionary(
            number => number.Key,
            number => number.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A declared value as the table writes it: a count for a number, and a count with its unit for
    /// a span.
    ///
    /// A span is written in the unit it was declared in rather than in ticks or in a machine
    /// format, because the table is read by somebody deciding whether a default is sensible, and
    /// nobody weighs 00:05:00 against how long the credits run. The unit is the largest one the
    /// span divides into exactly, so a value that stops being a whole number of minutes is written
    /// as seconds rather than rounded into a cell that then says something untrue.
    ///
    /// The division is asked of ticks rather than of seconds. Ticks are whole numbers on both
    /// sides, so the test is exact by construction; the same test on a double held only while
    /// every declared value happened to be a whole number of seconds small enough to be exact.
    /// </summary>
    /// <param name="value">The declared value.</param>
    /// <returns>The value as the table writes it.</returns>
    private static string Written(object value)
    {
        if (value is not TimeSpan span)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        var units = new (long Ticks, string One, string Many)[]
        {
            (TimeSpan.TicksPerDay, "day", "days"),
            (TimeSpan.TicksPerHour, "hour", "hours"),
            (TimeSpan.TicksPerMinute, "minute", "minutes"),
            (TimeSpan.TicksPerSecond, "second", "seconds"),
        };

        foreach (var unit in units)
        {
            if (span.Ticks % unit.Ticks == 0)
            {
                var count = span.Ticks / unit.Ticks;

                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{count} {(count == 1 ? unit.One : unit.Many)}");
            }
        }

        return span.ToString("c", CultureInfo.InvariantCulture);
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
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the one function that brings a provider identifier to the spelling this plugin
/// compares, and holds it to the table in the matching document.
///
/// The failure this is about is quiet in both directions. Comparing values as they were
/// stored leaves two servers unable to see that they hold the same work, and comparing them
/// after too much normalisation makes two different works equal, which writes one person's
/// watch state onto the wrong film.
/// </summary>
public class ProviderIdentifierTests
{
    /// <summary>
    /// The spellings an IMDb identifier actually arrives in. Every row is the same work, so
    /// every row has to come out as the same string.
    /// </summary>
    /// <returns>The stored value and what it normalises to.</returns>
    public static TheoryData<string, string> ImdbSpellings() => new TheoryData<string, string>
    {
        { "tt0111161", "tt0111161" },
        { "TT0111161", "tt0111161" },
        { "Tt0111161", "tt0111161" },
        { "0111161", "tt0111161" },
        { "00111161", "tt0111161" },
        { "000000111161", "tt0111161" },
        { "tt00111161", "tt0111161" },
        { "  tt0111161  ", "tt0111161" },
        { "\ttt0111161\n", "tt0111161" },
        { "tt12345678", "tt12345678" },
        { "12345678", "tt12345678" },
    };

    /// <summary>
    /// The same for the two providers whose identifiers are a plain number. The set is one
    /// table rather than two because the rule is one rule; the provider travels with the row
    /// so a failure names which of the two moved.
    /// </summary>
    /// <returns>The provider, the stored value and what it normalises to.</returns>
    public static TheoryData<IdentifierProvider, string, string> NumberSpellings() => new TheoryData<IdentifierProvider, string, string>
    {
        { IdentifierProvider.Tmdb, "550", "550" },
        { IdentifierProvider.Tmdb, "0550", "550" },
        { IdentifierProvider.Tmdb, "0000550", "550" },
        { IdentifierProvider.Tmdb, "  550  ", "550" },
        { IdentifierProvider.Tmdb, "\n550\t", "550" },
        { IdentifierProvider.Tvdb, "121361", "121361" },
        { IdentifierProvider.Tvdb, "0121361", "121361" },
        { IdentifierProvider.Tvdb, " 121361 ", "121361" },
    };

    /// <summary>
    /// A URL is what a scraper writes into the field when it was pointed at a page rather
    /// than at an identifier, and it is one of the spellings the issue behind this names. It
    /// is refused rather than mined, because taking an identifier out of it means deciding
    /// which part of somebody else's path layout is the identifier.
    /// </summary>
    /// <returns>The provider and the URL stored in its field.</returns>
    public static TheoryData<IdentifierProvider, string> Urls() => new TheoryData<IdentifierProvider, string>
    {
        { IdentifierProvider.Imdb, "https://www.imdb.com/title/tt0111161/" },
        { IdentifierProvider.Imdb, "imdb.com/title/tt0111161" },
        { IdentifierProvider.Tmdb, "https://www.themoviedb.org/movie/550" },
        { IdentifierProvider.Tmdb, "themoviedb.org/movie/550-fight-club" },
        { IdentifierProvider.Tvdb, "https://thetvdb.com/series/breaking-bad" },
        { IdentifierProvider.Tvdb, "thetvdb.com/dereferrer/series/81189" },
    };

    /// <summary>
    /// Nothing at all in the field. Distinguished from a malformed value because an operator
    /// can act on the two differently: one item was never scraped, the other was scraped
    /// badly.
    /// </summary>
    /// <returns>The provider and the empty value.</returns>
    public static TheoryData<IdentifierProvider, string?> Absent() => new TheoryData<IdentifierProvider, string?>
    {
        { IdentifierProvider.Imdb, null },
        { IdentifierProvider.Imdb, string.Empty },
        { IdentifierProvider.Imdb, "   " },
        { IdentifierProvider.Tmdb, null },
        { IdentifierProvider.Tmdb, "\t" },
        { IdentifierProvider.Tvdb, null },
        { IdentifierProvider.Tvdb, " " },
    };

    /// <summary>
    /// One provider's identifier stored under another provider's name. Both directions are
    /// here, because a shape test that only refuses one of them is a shape test that lets the
    /// silent mistake through.
    /// </summary>
    /// <returns>The provider the value is stored under, the value, and why it is refused.</returns>
    public static TheoryData<IdentifierProvider, string, IdentifierRefusal> ForeignValues() => new TheoryData<IdentifierProvider, string, IdentifierRefusal>
    {
        { IdentifierProvider.Tmdb, "tt0111161", IdentifierRefusal.NotTheProvidersShape },
        { IdentifierProvider.Tvdb, "tt0111161", IdentifierRefusal.NotTheProvidersShape },
        { IdentifierProvider.Tmdb, "TT0111161", IdentifierRefusal.NotTheProvidersShape },
        { IdentifierProvider.Imdb, "550", IdentifierRefusal.TooFewDigitsForAnImdbIdentifier },
        { IdentifierProvider.Imdb, "121361", IdentifierRefusal.TooFewDigitsForAnImdbIdentifier },
        { IdentifierProvider.Imdb, "tt550", IdentifierRefusal.TooFewDigitsForAnImdbIdentifier },
        { IdentifierProvider.Imdb, "nm0000151", IdentifierRefusal.NotTheProvidersShape },
        { IdentifierProvider.Tvdb, "breaking-bad", IdentifierRefusal.NotTheProvidersShape },
        { IdentifierProvider.Tmdb, "550-fight-club", IdentifierRefusal.NotTheProvidersShape },
        { IdentifierProvider.Tmdb, "5 5 0", IdentifierRefusal.NotTheProvidersShape },
        // The same number written in Arabic-Indic digits, escaped rather than pasted so this
        // source stays ASCII. A comparison over digits a reader cannot tell apart is a
        // comparison nobody can review.
        { IdentifierProvider.Imdb, "\u0660\u0661\u0661\u0661\u0661\u0666\u0661", IdentifierRefusal.NotTheProvidersShape },
    };

    /// <summary>
    /// Zero is not an identifier any of the three allocates. It arrives when a scraper wrote
    /// a placeholder into the field, and a match on it would gather every such item into one
    /// key.
    /// </summary>
    /// <returns>The provider and the value.</returns>
    public static TheoryData<IdentifierProvider, string> Zeros() => new TheoryData<IdentifierProvider, string>
    {
        { IdentifierProvider.Imdb, "tt0000000" },
        { IdentifierProvider.Imdb, "0000000" },
        { IdentifierProvider.Tmdb, "0" },
        { IdentifierProvider.Tmdb, "0000" },
        { IdentifierProvider.Tvdb, "0" },
    };

    /// <summary>
    /// The prefixed and unprefixed spelling, the padded and unpadded number, mixed case and
    /// surrounding whitespace, all landing on one string.
    /// </summary>
    /// <param name="stored">The value as some scraper stored it.</param>
    /// <param name="expected">The normal form.</param>
    [Theory]
    [MemberData(nameof(ImdbSpellings))]
    public void AnImdbIdentifierNormalisesToOneSpelling(string stored, string expected)
    {
        var reading = ProviderIdentifier.Normalise(IdentifierProvider.Imdb, stored);

        Assert.True(reading.IsUsable, $"{stored} was refused as {reading.Refusal} and it is a spelling of {expected}.");
        Assert.Equal(IdentifierProvider.Imdb, reading.Identifier!.Provider);
        Assert.Equal(expected, reading.Identifier.Value);
    }

    /// <summary>
    /// The same for the plain number providers.
    /// </summary>
    /// <param name="provider">The provider the value was stored under.</param>
    /// <param name="stored">The value as some scraper stored it.</param>
    /// <param name="expected">The normal form.</param>
    [Theory]
    [MemberData(nameof(NumberSpellings))]
    public void ANumericIdentifierNormalisesToOneSpelling(IdentifierProvider provider, string stored, string expected)
    {
        var reading = ProviderIdentifier.Normalise(provider, stored);

        Assert.True(reading.IsUsable, $"{stored} was refused as {reading.Refusal} and it is a spelling of {expected}.");
        Assert.Equal(provider, reading.Identifier!.Provider);
        Assert.Equal(expected, reading.Identifier.Value);
    }

    /// <summary>
    /// A URL is refused as not being the provider's shape, which is the same answer whether
    /// or not the identifier is visible inside it. Reading one out would be a guess about
    /// another site's path layout.
    /// </summary>
    /// <param name="provider">The provider whose field holds the URL.</param>
    /// <param name="stored">The URL.</param>
    [Theory]
    [MemberData(nameof(Urls))]
    public void AUrlWhereAnIdentifierWasExpectedIsRefused(IdentifierProvider provider, string stored)
    {
        var reading = ProviderIdentifier.Normalise(provider, stored);

        Assert.False(reading.IsUsable, $"{stored} is a URL and it was accepted for {provider}.");
        Assert.Null(reading.Identifier);
        Assert.Equal(IdentifierRefusal.NotTheProvidersShape, reading.Refusal);
    }

    /// <summary>
    /// An empty field is its own reason, separate from a malformed one.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="stored">The absent value.</param>
    [Theory]
    [MemberData(nameof(Absent))]
    public void AnAbsentValueIsRefusedAsAbsent(IdentifierProvider provider, string? stored)
    {
        var reading = ProviderIdentifier.Normalise(provider, stored);

        Assert.False(reading.IsUsable);
        Assert.Null(reading.Identifier);
        Assert.Equal(IdentifierRefusal.Absent, reading.Refusal);
    }

    /// <summary>
    /// The bullet this issue turns on. A value belonging to one provider does not pass
    /// another provider's shape test, in both directions, so a field somebody filled in
    /// wrongly produces an unmatched item with a reason rather than a match on the wrong
    /// work.
    /// </summary>
    /// <param name="provider">The provider the value is stored under.</param>
    /// <param name="stored">The value, which belongs to a different provider.</param>
    /// <param name="expected">The refusal it earns.</param>
    [Theory]
    [MemberData(nameof(ForeignValues))]
    public void AValueFromADifferentProviderCannotPassThisProvidersShapeTest(
        IdentifierProvider provider,
        string stored,
        IdentifierRefusal expected)
    {
        var reading = ProviderIdentifier.Normalise(provider, stored);

        Assert.False(reading.IsUsable, $"{stored} was accepted as a {provider} identifier.");
        Assert.Null(reading.Identifier);
        Assert.Equal(expected, reading.Refusal);
    }

    /// <summary>
    /// Zero is refused rather than normalised to an empty string, which is what stripping
    /// leading zeros from it would otherwise produce.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="stored">The value.</param>
    [Theory]
    [MemberData(nameof(Zeros))]
    public void AZeroIsRefusedRatherThanNormalisedToNothing(IdentifierProvider provider, string stored)
    {
        var reading = ProviderIdentifier.Normalise(provider, stored);

        Assert.False(reading.IsUsable);
        Assert.Null(reading.Identifier);
        Assert.Equal(IdentifierRefusal.Zero, reading.Refusal);
    }

    /// <summary>
    /// Normalising a normal form has to leave it alone. Without this a stored value and a
    /// value that has already been through the function compare unequal, and the record of
    /// what two servers last agreed is written in one spelling and read in another.
    /// </summary>
    [Fact]
    public void TheNormalFormIsItsOwnNormalForm()
    {
        var values = ImdbSpellings()
            .Select(row => (Provider: IdentifierProvider.Imdb, Stored: (string)row[0]))
            .Concat(NumberSpellings().Select(row => ((IdentifierProvider)row[0], (string)row[1])));

        foreach (var (provider, stored) in values)
        {
            var once = ProviderIdentifier.Normalise(provider, stored);
            var twice = ProviderIdentifier.Normalise(provider, once.Identifier!.Value);

            Assert.True(twice.IsUsable, $"{once.Identifier.Value} is a normal form and normalising it again refused it.");
            Assert.Equal(once.Identifier, twice.Identifier);
        }
    }

    /// <summary>
    /// Two spellings of one work compare equal and two works do not, which is the whole
    /// purpose of the normal form stated as an assertion rather than as a comment.
    /// </summary>
    [Fact]
    public void TwoSpellingsOfOneWorkCompareEqualAndTwoWorksDoNot()
    {
        var padded = ProviderIdentifier.Normalise(IdentifierProvider.Imdb, "  TT00111161 ").Identifier;
        var plain = ProviderIdentifier.Normalise(IdentifierProvider.Imdb, "tt0111161").Identifier;
        var other = ProviderIdentifier.Normalise(IdentifierProvider.Imdb, "tt0111162").Identifier;

        Assert.Equal(plain, padded);
        Assert.Equal(plain!.GetHashCode(), padded!.GetHashCode());
        Assert.NotEqual(plain, other);
    }

    /// <summary>
    /// The same number under two providers is two different works, and the provider travels
    /// with the identifier so nothing has to remember to keep them apart. #22 keys on the
    /// pair for this reason.
    /// </summary>
    [Fact]
    public void TheSameNumberUnderTwoProvidersIsNotOneIdentifier()
    {
        var tmdb = ProviderIdentifier.Normalise(IdentifierProvider.Tmdb, "550").Identifier;
        var tvdb = ProviderIdentifier.Normalise(IdentifierProvider.Tvdb, "550").Identifier;

        Assert.NotEqual(tmdb, tvdb);
        Assert.Equal("Tmdb:550", tmdb!.ToString());
        Assert.Equal("Tvdb:550", tvdb!.ToString());
    }

    /// <summary>
    /// What makes every comparison go through the one function, rather than a rule somebody
    /// has to remember. An identifier cannot be constructed; the only way to hold one is to
    /// have called the normalising function, so a value that reached a comparison is a
    /// normalised value by construction.
    ///
    /// What this does not hold is written in the matching document rather than only here: it
    /// does not stop a source comparing two raw strings without making an identifier at all.
    /// </summary>
    [Fact]
    public void AnIdentifierCannotBeConstructedWithoutNormalising()
    {
        Assert.Empty(typeof(ProviderIdentifier).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(IdentifierReading).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var factories = typeof(ProviderIdentifier)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(IdentifierReading))
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(new[] { nameof(ProviderIdentifier.Normalise) }, factories);
    }

    /// <summary>
    /// The document and the code name the same providers, refused in both directions. A
    /// provider the code carries and the table does not is a normal form nobody wrote down; a
    /// provider the table names and the code does not is a rule a reader will believe is
    /// implemented.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheCodeNameTheSameProviders()
    {
        var report = NormalForms.Check(
            NormalForms.Rows(NormalForms.Text()),
            Enum.GetNames<IdentifierProvider>());

        Assert.Empty(report.Missing.Select(provider =>
            $"{provider} is a provider the code carries and the table does not name, so its normal form is written nowhere."));

        Assert.Empty(report.Unknown.Select(provider =>
            $"{provider} is named by the table and the code carries no such provider, so the row is about nothing."));

        Assert.Empty(report.Repeated.Select(provider =>
            $"{provider} has more than one row, so which normal form holds is undefined."));
    }

    /// <summary>
    /// The guard proven by the mistake a hand maintained table actually produces. The
    /// near-miss misspells one provider by one character, so the row is present, in the right
    /// place, with the right normal form beside it. The repair is that one character.
    ///
    /// The fixture carries its own vocabulary rather than being judged against the real
    /// enumeration, so it proves the guard rather than the state of the tree on the day it
    /// ran.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = new[] { "Imdb", "Tmdb", "Tvdb" };

        var refused = NormalForms.Check(
            NormalForms.Rows(NormalForms.Fixture("normal-form-near-miss.txt")),
            vocabulary);

        Assert.Equal("Tmdb", Assert.Single(refused.Missing));
        Assert.Equal("Tmbd", Assert.Single(refused.Unknown));
        Assert.Empty(refused.Repeated);

        var repaired = NormalForms.Check(
            NormalForms.Rows(NormalForms.Fixture("normal-form-near-miss-repaired.txt")),
            vocabulary);

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.Repeated);
    }

    /// <summary>
    /// A provider named twice is the other way a table goes wrong, and the two rows can
    /// disagree about the normal form. This is driven from the fixture because the real table
    /// has no repeat, and a leg exercised only by the tree stops being exercised the moment
    /// the tree is right.
    /// </summary>
    [Fact]
    public void ARepeatedProviderIsRefused()
    {
        var rows = NormalForms.Rows(NormalForms.Fixture("normal-form-near-miss-repaired.txt"));

        var report = NormalForms.Check(
            rows.Concat(new[] { rows[0] }).ToList(),
            new[] { "Imdb", "Tmdb", "Tvdb" });

        Assert.Equal("Imdb", Assert.Single(report.Repeated));
    }

    /// <summary>
    /// The reader has to find the table at all. A change that renames the section or reflows
    /// the table into a different number of columns would otherwise leave every check above
    /// passing over an empty set, which reads exactly like a clean tree.
    /// </summary>
    [Fact]
    public void TheReaderFindsTheTableInTheRealDocument()
    {
        Assert.NotEmpty(NormalForms.Rows(NormalForms.Text()));
    }

    internal static class NormalForms
    {
        /// <summary>
        /// What the table and a set of providers disagree about.
        /// </summary>
        /// <param name="Missing">Providers the code carries and the table does not name.</param>
        /// <param name="Unknown">Providers the table names and the code does not carry.</param>
        /// <param name="Repeated">Providers the table names more than once.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> Repeated);

        /// <summary>
        /// Compares the providers a table names against a vocabulary. Pure, so the fixtures
        /// run through the same code the document does rather than through a second
        /// implementation of it.
        /// </summary>
        /// <param name="named">The providers the table names, in the order it names them.</param>
        /// <param name="providers">The vocabulary they are judged against.</param>
        /// <returns>What the two disagree about.</returns>
        internal static Report Check(IReadOnlyList<string> named, IReadOnlyList<string> providers)
        {
            var vocabulary = new HashSet<string>(providers, StringComparer.Ordinal);

            return new Report(
                providers.Where(provider => !named.Contains(provider, StringComparer.Ordinal)).ToList(),
                named.Where(provider => !vocabulary.Contains(provider)).Distinct(StringComparer.Ordinal).ToList(),
                named.GroupBy(provider => provider, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList());
        }

        /// <summary>
        /// Reads the provider column of the normal form table. The pattern requires four
        /// columns, which is what keeps it off the item kind table in the same document and
        /// keeps that document's own reader off this one.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The providers the table names, in the order it names them.</returns>
        internal static IReadOnlyList<string> Rows(string text) =>
            Regex
                .Matches(text, @"(?m)^\|\s*`(?<provider>[A-Za-z0-9]+)`\s*\|[^|]*\|[^|]*\|[^|]*\|\s*$")
                .Select(match => match.Groups["provider"].Value)
                .ToList();

        /// <summary>
        /// Reads the matching document from the tracked tree rather than from a copy in the
        /// output directory, because a copy proves the state of the file on the day it was
        /// copied.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "docs", "matching.md"));

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Matching",
                name));
    }
}

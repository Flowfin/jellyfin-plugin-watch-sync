using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the key one film is matched by on two servers that do not hold the same file.
///
/// The failure this is written against is not the missed match. It is the match that should
/// not have happened: a key that two different works can both produce writes one person's
/// watch state onto somebody else's film, and it looks exactly like a working sync until
/// somebody notices. So the key carries its provider, the order it is chosen in is fixed,
/// and an item that cannot produce one produces none rather than something weaker.
/// </summary>
public class MovieMatchKeyTests
{
    /// <summary>
    /// One identifier, per provider, spelled the way a scraper stored it.
    /// </summary>
    /// <returns>The provider name as stored, the value as stored, and the key it produces.</returns>
    public static TheoryData<string, string, string> SingleIdentifiers() => new TheoryData<string, string, string>
    {
        { "Imdb", "tt0000550", "Imdb:tt0000550" },
        { "Imdb", "TT0000550", "Imdb:tt0000550" },
        { "Imdb", "  0000550  ", "Imdb:tt0000550" },
        { "Tmdb", "550", "Tmdb:550" },
        { "Tmdb", "000550", "Tmdb:550" },
        { "Tvdb", "121361", "Tvdb:121361" },
    };

    /// <summary>
    /// Values a preferred provider carries that the normal form refuses.
    /// </summary>
    /// <returns>The provider name as stored and the refused value.</returns>
    public static TheoryData<string, string> RefusedIdentifiers() => new TheoryData<string, string>
    {
        { "Imdb", "https://www.imdb.com/title/tt0000550/" },
        { "Imdb", "550" },
        { "Imdb", "tt0000000" },
        { "Tmdb", "https://www.themoviedb.org/movie/550" },
        { "Tmdb", "550a" },
        { "Tmdb", "0" },
        { "Tvdb", "not a number" },
    };

    /// <summary>
    /// A film carrying one identifier is keyed by it, and the key names the provider.
    /// </summary>
    /// <param name="provider">The provider name as the item stores it.</param>
    /// <param name="stored">The value as the item stores it.</param>
    /// <param name="expected">The key.</param>
    [Theory]
    [MemberData(nameof(SingleIdentifiers))]
    public void AFilmWithOneIdentifierIsKeyedByIt(string provider, string stored, string expected)
    {
        var reading = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [provider] = stored,
        });

        Assert.True(reading.IsKeyed, $"{provider} {stored} produced no key: {reading.Refusal}.");
        Assert.Equal(expected, reading.Key!.ToString());
    }

    /// <summary>
    /// A film carrying several is keyed by the most preferred one, whatever order the map
    /// holds them in.
    ///
    /// The second half is the point. A dictionary has no order anybody promised, so a
    /// derivation that took whichever value it met first would key the same film differently
    /// on two servers that both identified it correctly.
    /// </summary>
    [Fact]
    public void AFilmWithSeveralIdentifiersIsKeyedByTheMostPreferredOne()
    {
        var oneWay = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Imdb"] = "tt0000550",
            ["Tmdb"] = "550",
            ["Tvdb"] = "121361",
        });

        var theOther = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tvdb"] = "121361",
            ["Tmdb"] = "550",
            ["Imdb"] = "tt0000550",
        });

        Assert.Equal("Imdb:tt0000550", oneWay.Key!.ToString());
        Assert.Equal(oneWay.Key.ToString(), theOther.Key!.ToString());
    }

    /// <summary>
    /// Two identifiers that name different works is a conflict nothing here can resolve, and
    /// the order is what stops it becoming a disagreement between two servers.
    ///
    /// Both sides carry the same IMDb identifier and a TMDb identifier for another film. The
    /// item was scraped twice and one of the two is wrong; which one, nothing in either
    /// server knows. Preferring the same provider on both sides means the two agree on the
    /// work regardless, and the wrong TMDb value moves nothing.
    /// </summary>
    [Fact]
    public void AFilmWhoseIdentifiersDisagreeIsKeyedByTheOrderRatherThanByAGuess()
    {
        var here = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Imdb"] = "tt0000550",
            ["Tmdb"] = "550",
        });

        var there = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Imdb"] = "tt0000550",
            ["Tmdb"] = "603",
        });

        Assert.Equal("Imdb:tt0000550", here.Key!.ToString());
        Assert.Equal(here.Key.ToString(), there.Key!.ToString());
    }

    /// <summary>
    /// The same number under two providers is two works, so it is two keys.
    /// </summary>
    [Fact]
    public void TheSameNumberUnderTwoProvidersIsNotOneKey()
    {
        var tmdb = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tmdb"] = "550",
        });

        var tvdb = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tvdb"] = "550",
        });

        Assert.NotEqual(tmdb.Key!.ToString(), tvdb.Key!.ToString());
        Assert.NotEqual(tmdb.Key, tvdb.Key);
    }

    /// <summary>
    /// A film carrying nothing produces no key and says so. A library of home video is the
    /// ordinary case rather than an error.
    /// </summary>
    [Fact]
    public void AFilmWithNoIdentifierAtAllProducesNoKey()
    {
        var readings = new IReadOnlyDictionary<string, string>?[]
        {
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = string.Empty },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "   " },
        }.Select(MovieMatchKey.Derive);

        foreach (var reading in readings)
        {
            Assert.False(reading.IsKeyed);
            Assert.Null(reading.Key);
            Assert.Equal(MatchKeyRefusal.NoIdentifierAtAll, reading.Refusal);
        }
    }

    /// <summary>
    /// A film scraped only by a source this plugin does not key on produces no key, and that
    /// is a different answer from having been scraped by nothing.
    /// </summary>
    [Fact]
    public void AFilmScrapedOnlyBySomeOtherProviderProducesNoKey()
    {
        var reading = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Zap2It"] = "EP000000550000",
            ["TvMaze"] = "550",
        });

        Assert.False(reading.IsKeyed);
        Assert.Null(reading.Key);
        Assert.Equal(MatchKeyRefusal.NoIdentifierFromAPreferredProvider, reading.Refusal);
    }

    /// <summary>
    /// A film whose preferred identifier is malformed produces no key, and it says which of
    /// the three cases it is, because this is the one an operator can repair.
    /// </summary>
    /// <param name="provider">The provider name as the item stores it.</param>
    /// <param name="stored">The value the normal form refuses.</param>
    [Theory]
    [MemberData(nameof(RefusedIdentifiers))]
    public void AFilmWhosePreferredIdentifierIsMalformedProducesNoKey(string provider, string stored)
    {
        var reading = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [provider] = stored,
        });

        Assert.False(reading.IsKeyed);
        Assert.Null(reading.Key);
        Assert.Equal(MatchKeyRefusal.EveryPreferredIdentifierWasRefused, reading.Refusal);
    }

    /// <summary>
    /// A malformed preferred identifier does not stop a less preferred one that is fine.
    ///
    /// The refusal is about the value rather than about the item, so falling through to the
    /// next provider is the same rule as before rather than a second chance at the refused
    /// one.
    /// </summary>
    [Fact]
    public void AMalformedIdentifierFallsThroughToTheNextProviderAndNeverBackToItself()
    {
        var reading = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Imdb"] = "https://www.imdb.com/title/tt0000550/",
            ["Tmdb"] = "550",
        });

        Assert.Equal("Tmdb:550", reading.Key!.ToString());
    }

    /// <summary>
    /// The provider name is matched however it is spelled.
    ///
    /// The map is written by scrapers, by imports and by two server lines. A name differing
    /// from this plugin's spelling only in case is an identifier the item genuinely carries,
    /// and reading it as absent would record a scraped film as unmatched.
    /// </summary>
    [Fact]
    public void APreferredProviderIsFoundWhateverCaseItsNameIsSpelledIn()
    {
        var reading = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["imdb"] = "tt0000550",
        });

        Assert.Equal("Imdb:tt0000550", reading.Key!.ToString());
    }

    /// <summary>
    /// The item's own database identifier never reaches the key.
    ///
    /// The server puts it in its own user data key list, where it is meaningful, and it names
    /// nothing on the peer. A key carrying it would match nothing there, and the failure
    /// would read as an unscraped library rather than as a mistake in this plugin.
    ///
    /// The second local value is a run of digits rather than the guid one usually is. A
    /// fallback added on the day somebody decides an unmatched item should at least key on
    /// something is refused by the shape test as long as the value is a guid, which would
    /// leave this test green over a rule nothing was holding. The digits are what make it
    /// bite.
    /// </summary>
    [Fact]
    public void TheLocalItemIdentifierNeverAppearsInTheKey()
    {
        var guid = "8f0a2b1c4d5e4f6a8b9c0d1e2f3a4b5c";
        var digits = "4815162342";

        var alongsideAnIdentifier = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = guid,
            ["ExternalId"] = digits,
            ["Imdb"] = "tt0000550",
        });

        Assert.Equal("Imdb:tt0000550", alongsideAnIdentifier.Key!.ToString());
        Assert.DoesNotContain(guid, alongsideAnIdentifier.Key.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(digits, alongsideAnIdentifier.Key.ToString(), StringComparison.Ordinal);

        foreach (var local in new[] { guid, digits })
        {
            var onItsOwn = MovieMatchKey.Derive(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Id"] = local,
                ["ExternalId"] = local,
            });

            Assert.False(onItsOwn.IsKeyed, $"{local} produced a key: {onItsOwn.Key}.");
            Assert.Null(onItsOwn.Key);
            Assert.Equal(MatchKeyRefusal.NoIdentifierFromAPreferredProvider, onItsOwn.Refusal);
        }
    }

    /// <summary>
    /// The local identifier cannot reach the key because it is not something the derivation
    /// is given.
    ///
    /// The test above proves it for the names an item is likely to carry. This one proves the
    /// shape: the provider identifier map is the only thing anything on this type is handed,
    /// so there is no item, no path and no database identifier to leak in the first place,
    /// and a later change taking one has to move this test to do it.
    /// </summary>
    [Fact]
    public void TheDerivationIsGivenNothingButProviderIdentifiers()
    {
        var methods = typeof(MovieMatchKey)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .ToList();

        Assert.Contains(methods, method => method.Name == nameof(MovieMatchKey.Derive));

        Assert.Empty(methods
            .SelectMany(method => method.GetParameters().Select(parameter => (method, parameter)))
            .Where(taken => taken.parameter.ParameterType != typeof(IReadOnlyDictionary<string, string>))
            .Select(taken =>
                $"{taken.method.Name} is handed a {taken.parameter.ParameterType.Name}, which is not the provider identifier map."));

        var derive = Assert.Single(methods, method => method.Name == nameof(MovieMatchKey.Derive));

        Assert.Single(derive.GetParameters());
    }

    /// <summary>
    /// The document and the code prefer the providers in the same order.
    ///
    /// The order decides which key a film with two identifiers produces, so a document saying
    /// one thing while the code does another is not a documentation defect. It is two servers
    /// disagreeing about a work they both identified, with a written rule that says they
    /// should not.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheCodeAgreeOnThePreferenceOrder()
    {
        var report = Preference.Check(
            Preference.Order(Preference.Text()),
            MovieMatchKey.PreferenceOrder().Select(provider => provider.ToString()).ToList());

        Assert.Empty(report.Missing.Select(provider =>
            $"{provider} is preferred by the code and the document's order does not name it."));

        Assert.Empty(report.Unknown.Select(provider =>
            $"{provider} is named by the document's order and the code prefers no such provider."));

        Assert.Empty(report.OutOfOrder);
    }

    /// <summary>
    /// The guard proven by the mistake this list actually invites. The near-miss puts the
    /// second provider first, which is a list somebody tidies rather than a list somebody
    /// breaks: every name is present, spelled correctly, and the document still reads well.
    /// The repair is the two lines swapped back.
    ///
    /// The fixture carries its own list rather than being judged against the real
    /// enumeration, so it proves the guard rather than the state of the tree on the day it
    /// ran.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheReorderedNearMissAndPassesItsRepair()
    {
        var code = new[] { "Imdb", "Tmdb", "Tvdb" };

        var refused = Preference.Check(
            Preference.Order(Preference.Fixture("preference-order-near-miss.txt")),
            code);

        Assert.Empty(refused.Missing);
        Assert.Empty(refused.Unknown);
        Assert.Equal(2, refused.OutOfOrder.Count);

        var repaired = Preference.Check(
            Preference.Order(Preference.Fixture("preference-order-near-miss-repaired.txt")),
            code);

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.OutOfOrder);
    }

    /// <summary>
    /// The reader has to find the list at all. A change that reflows it into prose or into
    /// bullets would otherwise leave the check above passing over an empty set, which reads
    /// exactly like a document and a code base that agree.
    /// </summary>
    [Fact]
    public void TheReaderFindsThePreferenceOrderInTheRealDocument()
    {
        Assert.NotEmpty(Preference.Order(Preference.Text()));
    }

    /// <summary>
    /// Reads the order the matching document prefers providers in.
    /// </summary>
    internal static class Preference
    {
        /// <summary>
        /// The numbered list of providers. It is the only numbered list in the document, and
        /// the check below fails rather than passes if it stops being found.
        /// </summary>
        private static readonly Regex Entry = new(@"(?m)^\d+\.\s*`(?<provider>[A-Za-z0-9]+)`\s*$", RegexOptions.Compiled);

        /// <summary>
        /// What the document's order and the code's order disagree about.
        /// </summary>
        /// <param name="Missing">Providers the code prefers and the document does not name.</param>
        /// <param name="Unknown">Providers the document names and the code does not prefer.</param>
        /// <param name="OutOfOrder">Providers both name, in different places.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> OutOfOrder);

        /// <summary>
        /// Compares two orders. Pure, so the fixtures run through the same code the document
        /// does rather than through a second implementation of it.
        /// </summary>
        /// <param name="document">The order the document fixes.</param>
        /// <param name="code">The order the code prefers.</param>
        /// <returns>What the two disagree about.</returns>
        internal static Report Check(IReadOnlyList<string> document, IReadOnlyList<string> code)
        {
            var byTheDocument = document.ToList();
            var byTheCode = code.ToList();
            var named = new HashSet<string>(byTheDocument, StringComparer.Ordinal);
            var preferred = new HashSet<string>(byTheCode, StringComparer.Ordinal);

            return new Report(
                byTheCode.Where(provider => !named.Contains(provider)).ToList(),
                byTheDocument.Where(provider => !preferred.Contains(provider)).Distinct(StringComparer.Ordinal).ToList(),
                byTheCode
                    .Where(named.Contains)
                    .Where(provider => byTheDocument.IndexOf(provider) != byTheCode.IndexOf(provider))
                    .Select(provider => string.Create(
                        CultureInfo.InvariantCulture,
                        $"{provider} is preferred {byTheCode.IndexOf(provider) + 1} by the code and {byTheDocument.IndexOf(provider) + 1} by the document."))
                    .ToList());
        }

        /// <summary>
        /// Reads the providers out of the numbered list, in the order it names them.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The providers, most preferred first.</returns>
        internal static IReadOnlyList<string> Order(string text) =>
            Entry.Matches(text).Select(match => match.Groups["provider"].Value).ToList();

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

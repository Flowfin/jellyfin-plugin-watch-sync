using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Runs the matcher against a corpus of situations held as data.
///
/// A wrong match is the failure this exists for, and it is the one nobody notices: it looks
/// like a working sync until somebody's watch state appears on the wrong film. So the
/// situations are chosen deliberately rather than accumulated from reports, and they are rows
/// in `Matching/corpus.txt` rather than cases in this file, so that a situation can be added
/// without writing a test.
///
/// Every row is one executed case. What a row asserts is the answer the matcher gives for the
/// item the peer sent against the library this server holds: which local item carries the key,
/// that two items claiming one key are refused rather than resolved, that nothing carries it,
/// or that the item produced no key at all and why.
///
/// Two closures sit beside the rows and both read a type rather than restating it. A provider
/// added to the preference order with no row matching on it fails, and a reason added to the
/// refusal vocabulary with no row producing it fails.
/// </summary>
public class MatcherCorpusTests
{
    /// <summary>
    /// Gets one entry per row in the corpus, which is what makes every row an executed case.
    /// </summary>
    public static TheoryData<string> Rows
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var row in Corpus.Rows())
            {
                data.Add(row.Id);
            }

            return data;
        }
    }

    /// <summary>
    /// The answer the matcher gives for one row is the answer the row names.
    /// </summary>
    /// <param name="id">The row's identifier.</param>
    [Theory]
    [MemberData(nameof(Rows))]
    public void TheCorpusRowHoldsItsAnswer(string id)
    {
        var row = Corpus.Named(id);
        var derived = Corpus.Derive(row.Peer);

        if (!Corpus.IsALookup(row.Answer))
        {
            Assert.Null(derived.Key);
            Assert.Equal(row.Answer, derived.Refusal.ToString());
            return;
        }

        Assert.Equal(MatchKeyRefusal.None, derived.Refusal);
        Assert.NotNull(derived.Key);

        var lookup = Corpus.Library(row).Lookup(derived.Key!);

        switch (row.Answer)
        {
            case "matched":
                Assert.Equal(MatchAnswer.Matched, lookup.Answer);
                Assert.Equal(Corpus.ExpectedItem(row), lookup.Item);
                break;

            case "ambiguous":
                Assert.Equal(MatchAnswer.Ambiguous, lookup.Answer);
                Assert.True(lookup.CompetingItems.Count > 1, $"{row.Id} expects an ambiguity and the index named {lookup.CompetingItems.Count} competing items.");
                break;

            default:
                Assert.Equal(MatchAnswer.NoMatch, lookup.Answer);
                break;
        }
    }

    /// <summary>
    /// The executed cases are the rows in the file, and every row names the situation it
    /// represents under an identifier no other row uses.
    ///
    /// A repeated identifier would run one row twice and count it once, and a row naming no
    /// situation is a case in data with none of what makes data worth holding as data.
    /// </summary>
    [Fact]
    public void EveryRowIsExecutedAndNamesItsSituation()
    {
        var rows = Corpus.Rows();

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Situation), $"{row.Id} names no situation."));
        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        var executed = ((IEnumerable<object?[]>)Rows)
            .Select(entry => (string)entry[0]!)
            .ToList();

        Assert.Equal(rows.Select(row => row.Id).ToList(), executed);
    }

    /// <summary>
    /// Every provider the key is derived from has a row that matches on it.
    ///
    /// This is the closure that makes the corpus grow with the preference order rather than
    /// behind it: adding a provider without adding a row fails here, and it fails naming the
    /// provider rather than a count.
    /// </summary>
    [Fact]
    public void EveryProviderInThePreferenceOrderHasARowThatMatchesOnIt()
    {
        var matchedOn = Corpus.Rows()
            .Where(row => string.Equals(row.Answer, "matched", StringComparison.Ordinal))
            .Select(row => Corpus.Derive(row.Peer).Provider)
            .Where(provider => provider.HasValue)
            .Select(provider => provider!.Value)
            .ToHashSet();

        Assert.All(
            MovieMatchKey.PreferenceOrder(),
            provider => Assert.True(matchedOn.Contains(provider), $"No row in the corpus matches on {provider}, so adding it to the preference order added nothing that exercises it."));
    }

    /// <summary>
    /// Every reason an item can carry instead of a key has a row that produces it.
    ///
    /// The set is read from the refusal vocabulary rather than listed here, so a reason added
    /// to it with no row producing it fails. The absence of a refusal is excluded, because
    /// every row that matches carries it.
    /// </summary>
    [Fact]
    public void EveryRefusalHasARowThatProducesIt()
    {
        var produced = Corpus.Rows()
            .Select(row => Corpus.Derive(row.Peer).Refusal)
            .ToHashSet();

        Assert.All(
            Enum.GetValues<MatchKeyRefusal>().Where(refusal => refusal != MatchKeyRefusal.None),
            refusal => Assert.True(produced.Contains(refusal), $"No row in the corpus produces {refusal}, so nothing here shows what an item carrying that reason looks like."));
    }

    /// <summary>
    /// The corpus holds a library item that produces no key.
    ///
    /// Such an item cannot be in the index, because the index holds keys, and the row that
    /// pairs one with a scraped copy on the peer is where that is asserted rather than assumed.
    /// This is what stops that situation being deleted from the file without anything saying so.
    /// </summary>
    [Fact]
    public void TheCorpusHoldsALocalItemThatProducesNoKey()
    {
        Assert.Contains(
            Corpus.Rows(),
            row => row.Library.Any(item => Corpus.Derive(item).Key is null));
    }

    /// <summary>
    /// Reads the corpus and turns a row into the calls the matcher answers.
    /// </summary>
    internal static class Corpus
    {
        private const string Separator = " :: ";

        private const string ItemSeparator = " | ";

        private static readonly char[] _fieldSeparator = new[] { ';' };

        /// <summary>
        /// One row of the corpus.
        /// </summary>
        /// <param name="Id">What the row is called.</param>
        /// <param name="Situation">What the row represents, in words.</param>
        /// <param name="Library">What this server holds.</param>
        /// <param name="Peer">What arrived from the peer.</param>
        /// <param name="Answer">The answer the row asserts.</param>
        internal sealed record Row(
            string Id,
            string Situation,
            IReadOnlyList<Item> Library,
            Item Peer,
            string Answer);

        /// <summary>
        /// One item, on either side.
        /// </summary>
        /// <param name="Kind">Which key rule the item is under.</param>
        /// <param name="IsExpected">Whether a match is expected to resolve to this item.</param>
        /// <param name="Own">What the item itself was scraped by.</param>
        /// <param name="Series">What the series it belongs to was scraped by.</param>
        /// <param name="Ordering">The ordering the series is numbered under.</param>
        /// <param name="Season">Its season number.</param>
        /// <param name="Episode">Its episode number.</param>
        /// <param name="Last">The last episode number the file covers.</param>
        internal sealed record Item(
            MatchKeyKind Kind,
            bool IsExpected,
            IReadOnlyDictionary<string, string> Own,
            IReadOnlyDictionary<string, string> Series,
            string? Ordering,
            int? Season,
            int? Episode,
            int? Last);

        /// <summary>
        /// What one derivation answered.
        /// </summary>
        /// <param name="Key">The key, where there is one.</param>
        /// <param name="Refusal">Why there is none, where there is not.</param>
        /// <param name="Provider">Which provider the key was derived from.</param>
        internal sealed record Derived(MatchKey? Key, MatchKeyRefusal Refusal, IdentifierProvider? Provider);

        /// <summary>
        /// Whether an answer is one the index gives rather than one the derivation gives.
        /// </summary>
        /// <param name="answer">The answer a row names.</param>
        /// <returns>Whether the row reaches the index at all.</returns>
        internal static bool IsALookup(string answer) =>
            string.Equals(answer, "matched", StringComparison.Ordinal)
            || string.Equals(answer, "ambiguous", StringComparison.Ordinal)
            || string.Equals(answer, "no-match", StringComparison.Ordinal);

        /// <summary>
        /// Every row in the corpus, in the order the file writes them.
        /// </summary>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<Row> Rows()
        {
            var path = Path.Combine(
                InvariantGuardTests.InvariantGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Matching",
                "corpus.txt");

            var significant = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(trimmed => trimmed.Length > 0 && !trimmed.StartsWith('#'));

            var rows = new List<Row>();

            foreach (var trimmed in significant)
            {
                var fields = trimmed.Split(Separator);

                Assert.True(fields.Length == 5, $"corpus.txt has a row with {fields.Length} fields where 5 are required: {trimmed}");

                var library = string.Equals(fields[2].Trim(), "-", StringComparison.Ordinal)
                    ? Array.Empty<Item>()
                    : fields[2].Split(ItemSeparator).Select(ReadItem).ToArray();

                rows.Add(new Row(
                    fields[0].Trim(),
                    fields[1].Trim(),
                    library,
                    ReadItem(fields[3]),
                    fields[4].Trim()));
            }

            return rows;
        }

        /// <summary>
        /// The row under one identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The row.</returns>
        internal static Row Named(string id) =>
            Rows().Single(row => string.Equals(row.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// Derives the key for one item, by the rule its kind is under.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>What the derivation answered.</returns>
        internal static Derived Derive(Item item)
        {
            if (item.Kind == MatchKeyKind.Movie)
            {
                var reading = MovieMatchKey.Derive(item.Own);

                return reading.IsKeyed
                    ? new Derived(MatchKey.Of(reading.Key!), MatchKeyRefusal.None, reading.Key!.Provider)
                    : new Derived(null, reading.Refusal, null);
            }

            var episode = EpisodeMatchKey.Derive(
                item.Own,
                item.Series,
                item.Ordering,
                item.Season,
                item.Episode,
                item.Last);

            return episode.IsKeyed
                ? new Derived(MatchKey.Of(episode.Key!), MatchKeyRefusal.None, episode.Key!.Identifier.Provider)
                : new Derived(null, episode.Refusal, null);
        }

        /// <summary>
        /// The index over what this server holds for one row.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The index.</returns>
        internal static MatchIndex Library(Row row)
        {
            var keyed = new List<KeyedItem>();

            for (var position = 0; position < row.Library.Count; position++)
            {
                var key = Derive(row.Library[position]).Key;

                if (key is not null)
                {
                    keyed.Add(new KeyedItem(Identity(position), key));
                }
            }

            var index = new MatchIndex(new CorpusLibrary(keyed));
            index.Rebuild();

            return index;
        }

        /// <summary>
        /// Which library item a match is expected to resolve to.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>Its identity.</returns>
        internal static Guid ExpectedItem(Row row)
        {
            var expected = row.Library
                .Select((item, position) => new { Item = item, Position = position })
                .Where(candidate => candidate.Item.IsExpected)
                .ToList();

            Assert.True(expected.Count == 1, $"{row.Id} expects a match and marks {expected.Count} library items as the one it resolves to, where exactly one is required.");

            return Identity(expected[0].Position);
        }

        private static Guid Identity(int position) =>
            Guid.ParseExact(
                string.Create(CultureInfo.InvariantCulture, $"00000000000000000000000000{position:D6}"),
                "N");

        private static Item ReadItem(string text)
        {
            var trimmed = text.Trim();
            var isExpected = trimmed.StartsWith('*');
            var body = isExpected ? trimmed[1..].Trim() : trimmed;
            var space = body.IndexOf(' ');
            var kind = space < 0 ? body : body[..space];

            Assert.True(
                string.Equals(kind, "movie", StringComparison.Ordinal) || string.Equals(kind, "episode", StringComparison.Ordinal),
                $"corpus.txt names an item of kind \"{kind}\", which is not one of the kinds docs/matching.md gives a key rule to: {trimmed}");

            var own = new Dictionary<string, string>(StringComparer.Ordinal);
            var series = new Dictionary<string, string>(StringComparer.Ordinal);
            string? ordering = null;
            int? season = null;
            int? episode = null;
            int? last = null;

            var fields = space < 0
                ? Array.Empty<string>()
                : body[(space + 1)..].Split(_fieldSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (var field in fields)
            {
                var at = field.IndexOf('=');

                Assert.True(at > 0, $"corpus.txt has a field that is not a name and a value: {field}");

                var name = field[..at].Trim();
                var value = field[(at + 1)..].Trim().Replace("\\s", " ", StringComparison.Ordinal);

                switch (name)
                {
                    case "order":
                        ordering = value;
                        break;

                    case "season":
                        season = Number(value);
                        break;

                    case "episode":
                        episode = Number(value);
                        break;

                    case "last":
                        last = Number(value);
                        break;

                    default:
                        if (name.StartsWith("series.", StringComparison.Ordinal))
                        {
                            series[name["series.".Length..]] = value;
                        }
                        else
                        {
                            own[name] = value;
                        }

                        break;
                }
            }

            return new Item(
                string.Equals(kind, "movie", StringComparison.Ordinal) ? MatchKeyKind.Movie : MatchKeyKind.Episode,
                isExpected,
                own,
                series,
                ordering,
                season,
                episode,
                last);
        }

        private static int Number(string value)
        {
            Assert.True(int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number), $"corpus.txt has a position that is not a number: {value}");

            return number;
        }

        /// <summary>
        /// The library one row holds, handed to the index a page at a time.
        /// </summary>
        private sealed class CorpusLibrary : IMatchIndexSource
        {
            private readonly IReadOnlyList<KeyedItem> _items;

            public CorpusLibrary(IReadOnlyList<KeyedItem> items)
            {
                _items = items;
            }

            public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count) =>
                _items.Skip(startIndex).Take(count).ToArray();
        }
    }
}

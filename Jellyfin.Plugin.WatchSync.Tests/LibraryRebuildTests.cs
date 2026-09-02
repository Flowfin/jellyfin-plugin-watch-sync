using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What survives an operator rebuilding a library, which is the third condition of #83.
///
/// The case is one a developer does not meet and an operator does: a library removed and
/// added again, or moved to new paths, so that the server hands every work a local identifier
/// it has never used before. Nothing about what was watched changed, and every identifier this
/// server holds for it did.
///
/// The condition's own sentence says the matched set has to be the same afterwards, because
/// nothing this plugin stores may be keyed on a local identifier alone. These are the first
/// half of that: the key a work is matched under is derived from what the work is rather than
/// from where this server filed it, so an index rebuilt over the new library answers the same
/// questions with different items behind them.
///
/// The second half is a reading rather than a fact here, and it is written on #83 rather than
/// asserted: `AgreedRecords` is addressed by the local item identifier, so what two sides last
/// agreed does not survive this and the repair is #14's rather than this file's. A fact here
/// stating the current shape would have to be deleted by the change that repairs it, which is
/// the wrong way round for a suite.
///
/// The whole file runs under the rule in `Jellyfin.Plugin.WatchSync.Tests/headless-rule.md`:
/// no clock, no disk, no network, and a library that is a list.
/// </summary>
public class LibraryRebuildTests
{
    /// <summary>
    /// The keys a library answers to are unchanged when every local identifier changes.
    ///
    /// This is the condition's sentence, asserted as two sets rather than item by item, so a
    /// rebuild that kept most of the library and lost one work fails on the member it lost
    /// instead of on whichever one a loop reached first.
    /// </summary>
    [Fact]
    public void TheMatchedSetIsTheSameAfterEveryLocalIdentifierChanges()
    {
        var before = new RebuiltLibrary(Films(1, 40));
        var index = new MatchIndex(before);
        index.Rebuild();

        var matchedBefore = MatchedKeys(index, 40);

        var after = new RebuiltLibrary(Films(1001, 40));
        var rebuilt = new MatchIndex(after);
        rebuilt.Rebuild();

        var matchedAfter = MatchedKeys(rebuilt, 40);

        Assert.Equal(matchedBefore, matchedAfter);
        Assert.Equal(40, matchedAfter.Count);
    }

    /// <summary>
    /// The items behind those keys are all different, which is what makes the rule above worth
    /// asserting.
    ///
    /// Without this the fact above passes over a rebuild that changed nothing, and a fixture
    /// that quietly reused the old identifiers would prove that a library which did not move
    /// still matches. The two are asserted together because neither is the claim alone.
    /// </summary>
    [Fact]
    public void EveryItemBehindThoseKeysIsANewOne()
    {
        var index = new MatchIndex(new RebuiltLibrary(Films(1, 40)));
        index.Rebuild();

        var itemsBefore = MatchedItems(index, 40);

        var rebuilt = new MatchIndex(new RebuiltLibrary(Films(1001, 40)));
        rebuilt.Rebuild();

        var itemsAfter = MatchedItems(rebuilt, 40);

        Assert.Empty(itemsBefore.Intersect(itemsAfter));
        Assert.Equal(40, itemsAfter.Count);
    }

    /// <summary>
    /// A rebuild that dropped a work is not a rebuild the rule above passes.
    ///
    /// The near-miss for both facts, kept as a fact of its own rather than left to be trusted:
    /// a library that came back one work short answers one key with a non-match, and the set
    /// comparison is what sees it. Without this the two rules above could be satisfied by an
    /// index that answered every question the same way whatever it held.
    /// </summary>
    [Fact]
    public void AWorkThatDidNotComeBackIsMissingFromTheMatchedSet()
    {
        var index = new MatchIndex(new RebuiltLibrary(Films(1, 40)));
        index.Rebuild();

        var complete = MatchedKeys(index, 40);

        var shortOfOne = new RebuiltLibrary(Films(1001, 40).Where(film => film.Key.Value != Key(20)).ToList());
        var rebuilt = new MatchIndex(shortOfOne);
        rebuilt.Rebuild();

        var missing = MatchedKeys(rebuilt, 40);

        Assert.NotEqual(complete, missing);
        Assert.DoesNotContain(Key(20), missing);
        Assert.Equal(39, missing.Count);
    }

    /// <summary>
    /// The key is derived from what the work is and not from where this server filed it.
    ///
    /// The reason the rules above hold, asserted at the one place it is decided rather than
    /// inferred from them. Two items with different local identifiers and one provider
    /// identifier carry one key, which is the same statement `docs/matching.md` makes and is
    /// what a rebuild depends on.
    /// </summary>
    [Fact]
    public void OneWorkCarriesOneKeyWhicheverItemTheServerFiledItUnder()
    {
        var filed = new KeyedItem(Item(7), MatchKey.Of(Identifier(7)));
        var refiled = new KeyedItem(Item(1007), MatchKey.Of(Identifier(7)));

        Assert.NotEqual(filed.ItemId, refiled.ItemId);
        Assert.Equal(filed.Key, refiled.Key);
    }

    /// <summary>
    /// The key of the film numbered as given, as its value reads.
    /// </summary>
    /// <param name="number">The film's number.</param>
    /// <returns>The key's value.</returns>
    private static string Key(int number) => MatchKey.Of(Identifier(number)).Value;

    /// <summary>
    /// A provider identifier, which is the half of a film that a rebuild does not touch.
    /// </summary>
    /// <param name="number">The film's number.</param>
    /// <returns>The identifier.</returns>
    private static ProviderIdentifier Identifier(int number)
    {
        var reading = ProviderIdentifier.Normalise(
            IdentifierProvider.Tmdb,
            number.ToString(CultureInfo.InvariantCulture));

        Assert.True(reading.IsUsable);

        return reading.Identifier!;
    }

    /// <summary>
    /// A local item identifier, which is the half a rebuild replaces.
    /// </summary>
    /// <param name="number">The item's number.</param>
    /// <returns>The identifier.</returns>
    private static Guid Item(int number) =>
        Guid.ParseExact(number.ToString("D32", CultureInfo.InvariantCulture), "N");

    /// <summary>
    /// A library of films, numbered so that the same works come back under new local
    /// identifiers: the provider identifier counts from one either way and the item identifier
    /// starts where the caller says.
    /// </summary>
    /// <param name="firstItem">The number the first item identifier takes.</param>
    /// <param name="count">How many films.</param>
    /// <returns>The items.</returns>
    private static List<KeyedItem> Films(int firstItem, int count) =>
        Enumerable
            .Range(0, count)
            .Select(offset => new KeyedItem(Item(firstItem + offset), MatchKey.Of(Identifier(1 + offset))))
            .ToList();

    /// <summary>
    /// The keys the index answers with a match, asked for every work the fixture knows about.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="works">How many works to ask about.</param>
    /// <returns>The keys that matched, as their values, ordered.</returns>
    private static List<string> MatchedKeys(MatchIndex index, int works) =>
        Enumerable
            .Range(1, works)
            .Where(number => index.Lookup(MatchKey.Of(Identifier(number))).IsMatched)
            .Select(number => MatchKey.Of(Identifier(number)).Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The local items behind the keys that matched.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="works">How many works to ask about.</param>
    /// <returns>The item identifiers, ordered.</returns>
    private static List<Guid> MatchedItems(MatchIndex index, int works) =>
        Enumerable
            .Range(1, works)
            .Select(number => index.Lookup(MatchKey.Of(Identifier(number))))
            .Where(lookup => lookup.IsMatched)
            .Select(lookup => lookup.Item)
            .OrderBy(item => item)
            .ToList();

    /// <summary>
    /// A library that hands over pages and nothing else.
    ///
    /// The rebuild is modelled as a second library rather than as a mutation of one, because
    /// that is what the server does: the old items are gone and the new ones were never the
    /// old ones. An index told about the change item by item would be testing the journal,
    /// which is #29's subject and is already covered there.
    /// </summary>
    private sealed class RebuiltLibrary : IMatchIndexSource
    {
        private readonly List<KeyedItem> _items;

        public RebuiltLibrary(IEnumerable<KeyedItem> items)
        {
            _items = items.ToList();
        }

        public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count) =>
            _items.Skip(startIndex).Take(count).ToList();
    }
}

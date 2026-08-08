using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

[assembly: TestCaseOrderer(
    "Jellyfin.Plugin.WatchSync.Tests.SeededTestCaseOrderer",
    "Jellyfin.Plugin.WatchSync.Tests")]
[assembly: TestCollectionOrderer(
    "Jellyfin.Plugin.WatchSync.Tests.SeededTestCollectionOrderer",
    "Jellyfin.Plugin.WatchSync.Tests")]

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The order the suite runs in, derived from a seed rather than from whatever order the runner
/// happens to hand the cases over in.
///
/// A suite that only ever runs in one order does not say whether a test needs another test to
/// have run first. That dependency is invisible until somebody adds a case, splits a class or
/// runs a single test on its own, and by then the suite has been trusted for months.
///
/// The seed is a number this repository turns into an order, and not a call to a random source
/// the runtime owns. Two reasons. A failing order has to be replayable, and a seed printed in a
/// log is only useful if feeding it back produces the same order on the machine that reads the
/// log. And the suite builds for two targets, so an order that came from the runtime's generator
/// could differ between them for a reason that has nothing to do with this repository.
/// </summary>
public static class RunOrder
{
    /// <summary>
    /// The environment variable the seed is read from. A run that sets nothing gets
    /// <see cref="SeedWhenUnset"/>, so a plain run of the suite is reproducible; varying the
    /// order is asking for it by name.
    /// </summary>
    public const string SeedVariable = "WATCHSYNC_TEST_ORDER_SEED";

    /// <summary>
    /// The seed used when the variable is not set.
    /// </summary>
    public const int SeedWhenUnset = 0;

    /// <summary>
    /// The seed this run orders by.
    /// </summary>
    /// <returns>The seed read from <see cref="SeedVariable"/>, or <see cref="SeedWhenUnset"/>.</returns>
    public static int Seed() => SeedFrom(Environment.GetEnvironmentVariable(SeedVariable));

    /// <summary>
    /// Turns the variable's text into a seed.
    ///
    /// A value that is not a whole number is refused rather than read as zero. Silently falling
    /// back would mean a mistyped seed running the order the person was trying to move away
    /// from, and reporting that it passed under the seed they asked for.
    /// </summary>
    /// <param name="value">The text of the variable, or null where it is not set.</param>
    /// <returns>The seed the text names.</returns>
    /// <exception cref="FormatException">The text is neither empty nor a whole number.</exception>
    public static int SeedFrom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SeedWhenUnset;
        }

        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
        {
            throw new FormatException(
                $"{SeedVariable} is set to \"{value}\", which is not a whole number. "
                + "It is refused rather than read as a default, because a mistyped seed that runs "
                + "the default order reports a pass under a seed nothing ran.");
        }

        return seed;
    }

    /// <summary>
    /// Puts a set into the order the seed names.
    ///
    /// The set is sorted by its own identity before it is shuffled, so the result depends on the
    /// seed and on which items are in the set, and never on the order they arrived in. Without
    /// that sort the same seed would produce different orders on two runs whenever the runner's
    /// discovery order moved, which is exactly the unreproducibility this exists to remove.
    /// </summary>
    /// <typeparam name="T">The kind of item being ordered.</typeparam>
    /// <param name="items">The set to order.</param>
    /// <param name="identity">A stable identity per item, unique within the set.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The set, every item once, in the order the seed names.</returns>
    public static IReadOnlyList<T> InSeededOrder<T>(IEnumerable<T> items, Func<T, string> identity, int seed)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(identity);

        var ordered = items.OrderBy(identity, StringComparer.Ordinal).ToArray();
        var state = unchecked((ulong)seed);

        for (var i = ordered.Length - 1; i > 0; i--)
        {
            var j = (int)(NextFrom(ref state) % (ulong)(i + 1));
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }

        return ordered;
    }

    /// <summary>
    /// SplitMix64. Small enough to read, and its output for a given seed is a property of these
    /// four lines rather than of the runtime the suite happens to be running on.
    /// </summary>
    /// <param name="state">The generator's state, advanced by the call.</param>
    /// <returns>The next value.</returns>
    private static ulong NextFrom(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

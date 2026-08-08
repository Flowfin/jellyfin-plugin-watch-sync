using System.Collections.Generic;
using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Orders the collections by the run's seed.
///
/// Named by an assembly attribute in <see cref="RunOrder"/>. Every test class here is its own
/// collection, so this is the ordering that decides which class starts first, and the case
/// orderer decides the order within one. Both are needed: shuffling inside a class while the
/// classes always run in the same sequence leaves the between-class dependency in place, and it
/// is the one that actually happens, because that is where shared state lives.
/// </summary>
public sealed class SeededTestCollectionOrderer : ITestCollectionOrderer
{
    /// <inheritdoc />
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
        => RunOrder.InSeededOrder(
            testCollections,
            collection => collection.UniqueID.ToString("D", CultureInfo.InvariantCulture),
            RunOrder.Seed());
}

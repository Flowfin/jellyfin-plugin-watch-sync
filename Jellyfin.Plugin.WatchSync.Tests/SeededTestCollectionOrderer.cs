using System.Collections.Generic;
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
///
/// The display name is the identity rather than the unique identifier, which is the opposite of
/// what the case orderer does and is not a slip. A collection's identifier is issued fresh on
/// every run, so ordering by it gave a different order for one seed on two runs of the same
/// build: the whole reason for having a seed, gone, while every test still passed and nothing
/// said so. RunOrderTests holds that direction closed.
/// </summary>
public sealed class SeededTestCollectionOrderer : ITestCollectionOrderer
{
    /// <inheritdoc />
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
        => RunOrder.InSeededOrder(testCollections, collection => collection.DisplayName, RunOrder.Seed());
}

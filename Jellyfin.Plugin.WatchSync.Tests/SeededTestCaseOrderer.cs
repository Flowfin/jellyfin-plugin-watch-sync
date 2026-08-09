using System.Collections.Generic;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Orders the cases inside one class by the run's seed.
///
/// Named by an assembly attribute in <see cref="RunOrder"/>. A case's unique identifier is used
/// as its identity rather than its display name, because two theory rows can share a display
/// name and the sort has to be total for the order to be reproducible.
/// </summary>
public sealed class SeededTestCaseOrderer : ITestCaseOrderer
{
    /// <inheritdoc />
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => RunOrder.InSeededOrder(testCases, testCase => testCase.UniqueID, RunOrder.Seed());
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the ordering the suite runs under.
///
/// The failure this set is written against is not a wrong order. It is an ordering that looks
/// installed and is not, or one that quietly runs fewer tests than were discovered. Both leave a
/// green run that reads as coverage, which is the worst outcome available to a test arrangement.
/// </summary>
public class RunOrderTests
{
    private static readonly string[] _names = Enumerable
        .Range(0, 64)
        .Select(index => "case-" + index.ToString("00", CultureInfo.InvariantCulture))
        .ToArray();

    /// <summary>
    /// The property that makes a failing order worth reporting. Without it a random order turns
    /// a real dependency between two tests into a flake somebody re-runs until it passes.
    /// </summary>
    [Fact]
    public void OneSeedAlwaysProducesOneOrder()
    {
        var first = Order(_names, seed: 4242);
        var second = Order(_names, seed: 4242);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The other half of the same property. An ordering that ignored its seed would pass the
    /// test above and nothing else here, and it would leave the suite in exactly the single
    /// fixed order this is meant to break.
    /// </summary>
    [Fact]
    public void TwoSeedsProduceTwoOrders()
    {
        Assert.NotEqual(Order(_names, seed: 1), Order(_names, seed: 2));
    }

    /// <summary>
    /// The one that matters most. A shuffle that drops a case, or hands one back twice, changes
    /// how many tests ran while every one of them still passes, and no summary line says so: the
    /// count moves and nothing compares it to anything.
    /// </summary>
    [Fact]
    public void NoCaseIsLostAndNoneIsRepeated()
    {
        for (var seed = -3; seed <= 3; seed++)
        {
            var ordered = Order(_names, seed);

            Assert.Equal(_names.Length, ordered.Count);
            Assert.Equal(_names.OrderBy(name => name, StringComparer.Ordinal), ordered.OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The order is a function of the seed and of which cases exist, never of the sequence the
    /// runner discovered them in. A shuffle applied straight to the incoming sequence would
    /// satisfy every other test here and still give two different orders for one seed on two
    /// machines, so a replay would not replay anything.
    /// </summary>
    [Fact]
    public void TheOrderDoesNotFollowTheOrderTheCasesArriveIn()
    {
        var reversed = _names.Reverse().ToArray();

        Assert.Equal(Order(_names, seed: 7), Order(reversed, seed: 7));
    }

    /// <summary>
    /// A seed that is not a number is refused rather than read as the default. The mistake this
    /// is written for is a shell that expanded nothing, leaving the variable holding a word: the
    /// run would report a pass under a seed that never ordered anything.
    /// </summary>
    [Fact]
    public void ASeedThatIsNotAWholeNumberIsRefused()
    {
        Assert.Equal(RunOrder.SeedWhenUnset, RunOrder.SeedFrom(null));
        Assert.Equal(RunOrder.SeedWhenUnset, RunOrder.SeedFrom("   "));
        Assert.Equal(-11, RunOrder.SeedFrom("-11"));

        Assert.Throws<FormatException>(() => RunOrder.SeedFrom("seven"));
        Assert.Throws<FormatException>(() => RunOrder.SeedFrom("1.5"));
    }

    /// <summary>
    /// This run's own seed, checked where a person will see it. A variable holding something the
    /// ordering cannot read is refused here and turns the run red, rather than being swallowed
    /// during discovery, where the runner logs it as a diagnostic and carries on in the
    /// unshuffled order.
    /// </summary>
    [Fact]
    public void TheSeedThisRunWasGivenIsReadable()
    {
        var value = Environment.GetEnvironmentVariable(RunOrder.SeedVariable);

        Assert.Null(Record.Exception(() => RunOrder.SeedFrom(value)));
    }

    /// <summary>
    /// The wiring, read from the assembly rather than assumed from the source.
    ///
    /// Both orderers are named by a string pair, and there are two ways for that to go wrong. A
    /// name that resolves to nothing, from a renamed type or a moved namespace, is refused by the
    /// runner itself: it reports a catastrophic failure, runs no test at all and exits non-zero,
    /// so nothing here is owed for that one.
    ///
    /// An attribute that is deleted rather than mistyped is the case nothing else catches. The
    /// runner has no opinion about an assembly that names no orderer, so the suite runs in its
    /// default order, green, and the only sign is that these orders stopped varying with the
    /// seed. Both halves were measured by making the change and running the suite.
    /// </summary>
    [Theory]
    [InlineData(typeof(TestCaseOrdererAttribute), typeof(ITestCaseOrderer))]
    [InlineData(typeof(TestCollectionOrdererAttribute), typeof(ITestCollectionOrderer))]
    public void TheOrdererTheAssemblyNamesResolvesAndIsOne(Type attribute, Type contract)
    {
        var assembly = typeof(RunOrder).Assembly;

        var declaration = Assert.Single(
            CustomAttributeData.GetCustomAttributes(assembly),
            data => data.AttributeType == attribute);

        var typeName = Assert.IsType<string>(declaration.ConstructorArguments[0].Value);
        var assemblyName = Assert.IsType<string>(declaration.ConstructorArguments[1].Value);

        var named = Assembly.Load(assemblyName).GetType(typeName, throwOnError: false);

        Assert.NotNull(named);
        Assert.True(
            contract.IsAssignableFrom(named),
            $"{typeName} is named as the {contract.Name} for this assembly and does not implement it.");
    }

    private static IReadOnlyList<string> Order(IEnumerable<string> names, int seed)
        => RunOrder.InSeededOrder(names, name => name, seed);
}

using System;
using System.Linq;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Where this plugin's clock enters, which is the answer taken on #32.
///
/// `injected-clock` refuses every way a source can read a clock nothing in a test can move,
/// and it refuses the runtime's own provider by name. A clock still has to enter somewhere:
/// a sweep has a start and an end, and a handler receiving a real event has to stamp it with
/// a real moment. So one file names the real clock, the composition root, and it is a declared
/// departure from that rule with its reason written beside it.
///
/// The composition root is the exception because that is where a dependency is chosen rather
/// than used. Everything it hands the clock to takes it as a constructor argument or takes its
/// moments as parameters, so a test replaces it by constructing the type it is testing and
/// nothing reaches past a static to do it.
///
/// What these hold is that the departure stays one, that the clock handed out is the runtime's
/// own, and that a clock the host registered first is the one that stands. What they cannot
/// see is a caller that resolves the clock and then reads it where a moment should have been
/// handed in; that is a judgement about a call site and the review is where it is caught.
/// </summary>
public class ClockEntryTests
{
    /// <summary>
    /// The rule the composition root departs from, spelled as the vocabulary spells it.
    /// </summary>
    private const string RealClockRule = "clock-time-provider-system";

    /// <summary>
    /// The one file that may name the real clock.
    /// </summary>
    private const string CompositionRoot = "Jellyfin.Plugin.WatchSync/ServiceRegistrator.cs";

    /// <summary>
    /// The whole point. The invariant guard already refuses a second file naming the real clock
    /// with no departure declared for it, so the mistake somebody will actually make is the
    /// other one: a second line in the exceptions file beside the first, for their own file,
    /// with a reason that reads well. This holds the set of departures from that rule to exactly
    /// the composition root, in both directions, so a departure removed reddens as much as one
    /// added.
    /// </summary>
    [Fact]
    public void TheRealClockIsDeclaredForTheCompositionRootAndForNoOtherFile()
    {
        var departing = InvariantGuardTests.InvariantGuard.Departures()
            .Where(departure => string.Equals(departure.Id, RealClockRule, StringComparison.Ordinal))
            .Select(departure => departure.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { CompositionRoot }, departing);
    }

    /// <summary>
    /// The departure is not a hole: the file it names does hand out a clock, and the clock it
    /// hands out is the runtime's own rather than a stand-in that would answer with the same
    /// instant on every server. Resolved from the registrator with no server present, which is
    /// the same arrangement every other registered service is held to.
    /// </summary>
    [Fact]
    public void TheCompositionRootHandsOutTheRuntimesOwnClock()
    {
        var services = new ServiceCollection();

        new ServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        using var provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>
    /// Two clocks in one container are two answers to what time it is. The host may register a
    /// clock of its own before any plugin is asked, and if it has, that one stands: the
    /// registration here is a TryAdd rather than an Add, and this is the fact that reddens when
    /// somebody tidies it into the other one.
    /// </summary>
    [Fact]
    public void AClockTheHostAlreadyRegisteredIsTheOneThatStands()
    {
        var hosts = new Mock<TimeProvider>().Object;
        var services = new ServiceCollection();
        services.AddSingleton(hosts);

        new ServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        using var provider = services.BuildServiceProvider();

        Assert.Same(hosts, provider.GetRequiredService<TimeProvider>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
    }
}

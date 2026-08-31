using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds this plugin's registration to the services it has, which is #8.
///
/// The invariant `static-instance-not-read` already refuses a type reaching for the plugin's
/// static instance. That is the refusal half. On its own it leaves a service with no way to be
/// handed anything, and the failure it produces is not a read of the static: it is a type that
/// constructs its own dependency, which no scan refuses and which is the same untestable knot
/// one indirection further in.
///
/// So what these judge is the other half. A type this plugin cannot construct without something
/// the server owns is exactly a type a caller cannot construct either, and it has to arrive
/// through registration. The set is read off the assembly rather than written here, so a service
/// added tomorrow joins the subject without anybody remembering to add it to a list.
///
/// What they cannot judge is whether the registered lifetime is the right one, or whether a
/// caller that could have taken a service actually did. Both are judgements about intent, the
/// review is where they are caught, and nothing here claims otherwise.
/// </summary>
public class ServiceRegistratorTests
{
    /// <summary>
    /// The type the server constructs itself, so it is outside the subject rather than exempted
    /// by preference. <c>BasePlugin</c> is instantiated by the server's plugin loader from the
    /// two arguments it hands every plugin, and registering it here would produce a second
    /// instance beside the one the server holds.
    /// </summary>
    private static readonly IReadOnlyList<Type> ServerConstructs = new[] { typeof(Plugin) };

    /// <summary>
    /// The whole point. A type that cannot be built without something the server owns and that
    /// nothing registers is a type whose only remaining route to its dependency is to reach for
    /// it, which is what this issue was opened against.
    /// </summary>
    [Fact]
    public void EveryTypeThatNeedsSomethingFromTheServerIsRegistered()
    {
        var needing = TypesNeedingTheServer();

        Assert.NotEmpty(needing);

        var registered = Registrations().Select(descriptor => descriptor.ImplementationType).ToList();

        Assert.Empty(needing
            .Where(type => !registered.Contains(type))
            .Select(type => type.Name + " cannot be constructed without something the server owns and nothing registers it, so its only route to that dependency is to reach for one."));
    }

    /// <summary>
    /// The other direction, and it fails soft rather than loud. A registration for a type that
    /// is no longer in the assembly, or for one this plugin does not own, is a line that reads
    /// as wiring and hands nobody anything.
    ///
    /// It refuses a factory registration too, by construction, because such a descriptor names
    /// no implementation type at all. That is deliberate rather than a side effect: a factory is
    /// where a service is constructed by hand inside the registrator, which is the same knot
    /// this issue is against arriving one indirection further in. A factory that is genuinely
    /// wanted is a declared departure and an argument, not a line nobody notices.
    /// </summary>
    [Fact]
    public void EveryRegistrationIsATypeThisPluginOwns()
    {
        var owned = typeof(Plugin).Assembly;

        Assert.Empty(Registrations()
            .Where(descriptor => descriptor.ImplementationType?.Assembly != owned)
            .Select(descriptor => descriptor.ServiceType.Name + " is registered against an implementation this plugin does not own."));
    }

    /// <summary>
    /// The closure. A registered service with no constructor this collection can satisfy cannot
    /// be resolved at all, and the failure arrives on a running server at the moment the first
    /// caller asks for it rather than here.
    ///
    /// It is one constructor rather than all of them, because a type may offer a second one that
    /// nothing registers on purpose: the store takes a way of opening a file so that a full disk
    /// is reachable from a suite, and that constructor is for a caller who has one rather than
    /// for this collection.
    /// </summary>
    [Fact]
    public void EveryRegisteredServiceHasAConstructorThisCollectionCanSatisfy()
    {
        var registered = Registrations().Select(descriptor => descriptor.ImplementationType).ToList();

        Assert.Empty(registered
            .Where(type => type is not null)
            .Where(type => !type!.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters()
                    .All(parameter => registered.Contains(parameter.ParameterType)
                        || IsAServerCollaborator(parameter.ParameterType))))
            .Select(type => type!.Name + " has no constructor whose parameters are all either registered here or handed over by the server, so resolving it fails on a running server at the first caller.")
            .Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// The third condition of #8. Every registered service is built here from fakes alone, with
    /// no server present, and the fakes are derived from what each service asks for rather than
    /// written out, so a service that grows a dependency is covered without this being edited.
    /// </summary>
    [Fact]
    public void EveryRegisteredServiceIsConstructedFromFakesWithNoServerPresent()
    {
        var services = Registered();

        foreach (var dependency in Registrations()
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null)
            .SelectMany(type => Dependencies(type!))
            .Where(IsAServerCollaborator)
            .Distinct())
        {
            services.AddSingleton(dependency, Fake(dependency));
        }

        using var provider = services.BuildServiceProvider();

        foreach (var descriptor in Registrations())
        {
            Assert.NotNull(provider.GetRequiredService(descriptor.ServiceType));
        }
    }

    /// <summary>
    /// A fake of a type the server owns, which is what stands in for the server here.
    /// </summary>
    /// <param name="type">The server-owned type.</param>
    /// <returns>The fake.</returns>
    private static object Fake(Type type) =>
        ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object;

    /// <summary>
    /// Whether a type is the server's rather than this plugin's or the platform's. The server
    /// ships assemblies under both of its own prefixes, and a check written against the package
    /// names would have matched neither.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether the server owns it.</returns>
    private static bool ComesFromTheServer(Type type)
    {
        var assembly = type.Assembly.GetName().Name ?? string.Empty;

        return type.Assembly != typeof(Plugin).Assembly
            && (assembly.StartsWith("Jellyfin.", StringComparison.Ordinal)
                || assembly.StartsWith("MediaBrowser.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether a type is something the server hands over rather than something a caller makes.
    ///
    /// The line is the interface. A manager, a paths accessor or a library is a collaborator
    /// this plugin cannot construct and has to be given; a kind, an identifier or an item is
    /// data that arrives in an argument and is made by whoever has the values. Both come out of
    /// the server's assemblies, so ownership alone does not separate them, and a rule that
    /// stopped there would call every record carrying a server enumeration a service.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether the server hands it over.</returns>
    private static bool IsAServerCollaborator(Type type) => type.IsInterface && ComesFromTheServer(type);

    /// <summary>
    /// What one type asks for, over every public constructor it has.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The parameter types.</returns>
    private static IEnumerable<Type> Dependencies(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

    /// <summary>
    /// The types of this plugin that cannot be constructed without the server, read off the
    /// assembly this plugin builds rather than out of a list kept here.
    /// </summary>
    /// <returns>Those types.</returns>
    private static IReadOnlyList<Type> TypesNeedingTheServer() =>
        typeof(Plugin).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsNested)
            .Where(type => !ServerConstructs.Contains(type))
            .Where(type => Dependencies(type).Any(IsAServerCollaborator))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// What the registrator registers, read by running it rather than by reading its source.
    /// </summary>
    /// <returns>The collection it filled.</returns>
    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();

        new ServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        return services;
    }

    /// <summary>
    /// The descriptors the registrator added. It fails loudly on finding none, because every
    /// assertion above would otherwise be a comparison against an empty set.
    /// </summary>
    /// <returns>The descriptors.</returns>
    private static IReadOnlyList<ServiceDescriptor> Registrations()
    {
        var registered = Registered().ToList();

        if (registered.Count == 0)
        {
            Assert.Fail("The registrator registered nothing, so nothing here is judging what this plugin hands its callers.");
        }

        return registered;
    }
}

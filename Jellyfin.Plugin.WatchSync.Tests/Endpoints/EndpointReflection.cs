using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Jellyfin.Plugin.WatchSync.Tests.Endpoints;

/// <summary>
/// What an endpoint of this plugin is, in one place, because two guards read it.
///
/// #66 holds a table of policies against the controllers and #112 holds a document against the
/// same controllers, and #112's own reading says what happens if each writes its own reflection:
/// they disagree about the population, and the disagreement is invisible while there are no
/// endpoints. One of them would then be quietly holding a smaller set than it claims on the day
/// the first controller lands, which is the day both are believed.
///
/// So the definition lives here and neither guard carries a copy. It is a public method of a
/// public type deriving from <see cref="ControllerBase"/> that carries an attribute implementing
/// <see cref="IActionHttpMethodProvider"/>, which is what every <c>HttpGet</c>, <c>HttpPost</c>,
/// <c>HttpPut</c> and <c>HttpDelete</c> is. Naming the interface rather than the four attributes
/// is what keeps a verb nobody has used here yet inside the population rather than outside it.
///
/// <para>
/// THE PLUGIN SERVES NO ENDPOINT TODAY, so a search over its assembly answers nothing and every
/// comparison built on one is empty against empty. That is why what is offered here is a function
/// over a set of types: the fixtures beside this file carry an endpoint of each shape, so both
/// guards prove their reflection on a set that is not empty before the plugin has any.
/// </para>
/// </summary>
internal static class EndpointReflection
{
    /// <summary>
    /// The endpoints among a set of types.
    /// </summary>
    /// <param name="types">The types to look in.</param>
    /// <returns>The endpoints.</returns>
    internal static IReadOnlyList<Endpoint> Discovered(IEnumerable<Type> types) =>
        types
            .Where(type => type.IsPublic && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => new { Type = type, Method = method, Verb = Verb(method) })
                .Where(each => each.Verb is not null)
                .Select(each => new Endpoint(
                    $"{each.Type.Name}.{each.Method.Name}",
                    each.Verb!,
                    Route(each.Method),
                    Policy(each.Type, each.Method))))
            .ToList();

    /// <summary>
    /// The types of the plugin's own assembly, which is the population both guards are about.
    /// </summary>
    /// <returns>The types.</returns>
    internal static IReadOnlyList<Type> ThePlugin() => typeof(Plugin).Assembly.GetTypes();

    /// <summary>
    /// The fixture controllers, which are what the reflection is proven on.
    /// </summary>
    /// <returns>The types.</returns>
    internal static IReadOnlyList<Type> Fixtures() =>
        typeof(EndpointReflection).Assembly
            .GetTypes()
            .Where(type => string.Equals(
                type.Namespace,
                "Jellyfin.Plugin.WatchSync.Tests.Endpoints",
                StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// The HTTP method the attribute declares, or null where the method declares none and is
    /// therefore not an endpoint.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The verb.</returns>
    private static string? Verb(MethodInfo method) =>
        method
            .GetCustomAttributes(inherit: true)
            .OfType<IActionHttpMethodProvider>()
            .SelectMany(attribute => attribute.HttpMethods)
            .FirstOrDefault();

    /// <summary>
    /// The route the attribute declares, or the empty string where it declares none.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The route.</returns>
    private static string Route(MethodInfo method) =>
        method
            .GetCustomAttributes(inherit: true)
            .OfType<IRouteTemplateProvider>()
            .Select(attribute => attribute.Template)
            .FirstOrDefault(template => template is not null)
        ?? string.Empty;

    /// <summary>
    /// What authorises the endpoint: the policy an <c>Authorize</c> attribute names, <c>default</c>
    /// where it names none, or null where nothing authorises it.
    ///
    /// An <c>AllowAnonymous</c> anywhere on the pair answers null however many attributes sit
    /// beside it, because that is what the server does with it. The method is read before the
    /// type, so an authorised controller carrying one open action is the open action rather than
    /// the controller.
    /// </summary>
    /// <param name="type">The declaring type.</param>
    /// <param name="method">The method.</param>
    /// <returns>The policy, or null.</returns>
    private static string? Policy(Type type, MethodInfo method)
    {
        var attributes = method.GetCustomAttributes(inherit: true)
            .Concat(type.GetCustomAttributes(inherit: true))
            .ToList();

        if (attributes.OfType<IAllowAnonymous>().Any())
        {
            return null;
        }

        var authorised = attributes.OfType<AuthorizeAttribute>().ToList();

        if (authorised.Count == 0)
        {
            return null;
        }

        return authorised
            .Select(attribute => attribute.Policy)
            .FirstOrDefault(policy => !string.IsNullOrEmpty(policy))
            ?? "default";
    }
}

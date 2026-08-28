namespace Jellyfin.Plugin.WatchSync.Tests.Endpoints;

/// <summary>
/// One endpoint, as <see cref="EndpointReflection"/> finds it.
///
/// It carries what both guards over the endpoints compare against their own register: #66's table
/// of policies and #112's document. Neither owns it, because a record declared inside one of them
/// is a population the other has to re-derive.
/// </summary>
/// <param name="Name">The declaring type and method, as <c>Type.Method</c>.</param>
/// <param name="Verb">The HTTP method.</param>
/// <param name="Route">The route the attribute declares.</param>
/// <param name="Policy">
/// The policy the attribute names, <c>default</c> where it names none, or null where nothing
/// authorises the endpoint at all.
/// </param>
internal sealed record Endpoint(string Name, string Verb, string Route, string? Policy);

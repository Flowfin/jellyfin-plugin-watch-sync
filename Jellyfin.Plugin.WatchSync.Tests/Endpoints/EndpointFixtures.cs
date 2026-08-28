using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchSync.Tests.Endpoints;

/// <summary>
/// An endpoint of the shape this plugin will serve: elevated, and authorised by a policy the
/// server declares.
///
/// These are fixtures and not this plugin's surface. They exist because the plugin has no
/// controller, so a reflection over its assembly finds nothing and every comparison built on it
/// is empty against empty. A guard proven only on that would go on passing on the day the first
/// controller arrived under a shape the reflection does not recognise, which is the failure #66's
/// first reading names: the trap looks exactly like coverage.
///
/// They carry their policy as a literal rather than as the server's constant, which is the one
/// thing here that is not what a real controller would do. The invariant that refuses that
/// spelling reads the plugin's own sources, and a fixture written to satisfy it would have to
/// name a constant this project does not reference; what it would buy is nothing, because
/// nothing authorises these.
/// </summary>
[ApiController]
public sealed class ElevatedFixtureController : ControllerBase
{
    /// <summary>
    /// An endpoint an operator reaches and nobody else does.
    /// </summary>
    /// <returns>Nothing.</returns>
    [HttpGet("Plugins/WatchSyncFixture/Elevated")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult Elevated() => Ok();
}

/// <summary>
/// An endpoint bound to the calling user, authorised by the server's default authorisation
/// rather than by a policy of its own.
/// </summary>
[ApiController]
public sealed class UserScopedFixtureController : ControllerBase
{
    /// <summary>
    /// An endpoint a person reaches about their own record.
    /// </summary>
    /// <returns>Nothing.</returns>
    [HttpPost("Plugins/WatchSyncFixture/Mine")]
    [Authorize]
    public IActionResult Mine() => Ok();
}

/// <summary>
/// The endpoint that forgot its attribute, which is the one #66's body opens on: it is
/// indistinguishable from an authorised one until somebody calls it.
/// </summary>
[ApiController]
public sealed class OpenFixtureController : ControllerBase
{
    /// <summary>
    /// An endpoint nothing authorises.
    /// </summary>
    /// <returns>Nothing.</returns>
    [HttpGet("Plugins/WatchSyncFixture/Open")]
    public IActionResult Open() => Ok();
}

/// <summary>
/// An endpoint that carries an attribute and opts out of it again, which is the second way an
/// open endpoint is written and the one a reader scrolling past the type sees as authorised.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
public sealed class OptedOutFixtureController : ControllerBase
{
    /// <summary>
    /// An endpoint whose type is authorised and whose method is not.
    /// </summary>
    /// <returns>Nothing.</returns>
    [HttpDelete("Plugins/WatchSyncFixture/OptedOut")]
    [AllowAnonymous]
    public IActionResult OptedOut() => Ok();
}

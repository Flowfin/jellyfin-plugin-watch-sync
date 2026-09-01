using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// What this plugin's store holds about one person, and the removal of it, reachable without an
/// administrator reading files by hand. This is #74's first condition.
///
/// The two operations are <see cref="HeldAboutOnePerson"/>'s and nothing about what they do is
/// decided here. What this type adds is the surface: a route, the server's own authorisation on
/// it, and a shape a caller can read. Deciding any of it again here would be a second answer to
/// a question that has one.
///
/// <para>
/// Both are elevated, and that is not a judgement made in this file either. An action about
/// another person's record is exactly the case the rule under
/// <c>endpoint-user-from-the-request</c> names elevation for, and the policy is the constant the
/// server declares rather than a string, so a rename on either supported line is a build failure
/// here instead of an endpoint nothing authorises.
/// </para>
///
/// <para>
/// A person this plugin holds nothing about and a person this server has never had answer
/// identically, with an empty report and a removal of nothing. That is deliberate rather than a
/// gap: this plugin holds no list of users, so it could not tell the two apart without asking the
/// server, and an answer that separated them would tell a caller which accounts exist. The rule
/// is in docs/endpoints.md and the row says so.
/// </para>
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
public class HeldAboutOnePersonController : ControllerBase
{
    private readonly DocumentStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeldAboutOnePersonController"/> class.
    /// </summary>
    /// <param name="store">The store this plugin keeps its documents in.</param>
    public HeldAboutOnePersonController(DocumentStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Everything this plugin's store holds about one person, across every pairing.
    /// </summary>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>The report.</returns>
    [HttpGet("Plugins/WatchSync/Persons/{mappedUserId}/Records")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<HeldRecordsReport> Report([FromRoute] Guid mappedUserId)
    {
        if (mappedUserId == Guid.Empty)
        {
            return BadRequest();
        }

        var held = HeldAboutOnePerson.Report(_store, mappedUserId);

        return new HeldRecordsReport(
            mappedUserId,
            held
                .Select(entry => new HeldRecord(
                    entry.Key.Name,
                    entry.Key.Kind.NamePrefix,
                    entry.Key.PairingId,
                    entry.Value.Version,
                    entry.Value.ToJson()))
                .ToList());
    }

    /// <summary>
    /// Removes everything this plugin's store holds about one person, and answers how many
    /// documents went.
    ///
    /// This does not remove what the person watched. That belongs to the server, it is not this
    /// plugin's to delete, and docs/privacy.md says so in the same words rather than leaving a
    /// caller to infer it from a count.
    /// </summary>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <returns>How many documents were removed.</returns>
    [HttpDelete("Plugins/WatchSync/Persons/{mappedUserId}/Records")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<RecordsRemoved> Remove([FromRoute] Guid mappedUserId)
    {
        if (mappedUserId == Guid.Empty)
        {
            return BadRequest();
        }

        return new RecordsRemoved(mappedUserId, HeldAboutOnePerson.Remove(_store, mappedUserId));
    }
}

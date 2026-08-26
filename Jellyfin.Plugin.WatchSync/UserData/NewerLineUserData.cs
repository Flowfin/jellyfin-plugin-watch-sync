#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.WatchSync.UserData;

/// <summary>
/// The adapter on the newer supported line, which is the one whose server reads a page in one
/// call and names the version that drives a resume point.
///
/// It is compiled only into the target that line is built for, for the reason its counterpart
/// gives.
///
/// The version the server names is an identifier and carries no length with it, so the runtime
/// is read off the item that identifier resolves to. Where the server names no version the item
/// has none with a resume point, and the item's own runtime is the answer, which is the same
/// answer the older line gives for the same item. Where the identifier resolves to nothing the
/// item is gone from under the read, and the item's own runtime is the answer for the same
/// reason: a rule that is handed no number drops a position, and dropping one here would say the
/// two versions are far apart rather than that one of them was not found.
///
/// WHAT THIS ADAPTER DOES NOT DECIDE, AND IT IS WORTH FINDING HERE RATHER THAN LATER. The server
/// on this line holds two records for a work held as several files: the item's own, which is what
/// a read returns, and the resume version's, which is what drives what a person actually resumes.
/// This adapter reads the moved set from the item's own record on both lines, so a position this
/// server offers a peer is the item's rather than the resume version's. Whether it should be the
/// other one is a question about what leaves this server rather than about where an arriving
/// position lands, so it belongs with the moved set in #12 and the handler in #15 rather than
/// here, and nothing in this plugin reads either record yet.
/// </summary>
public sealed class NewerLineUserData : ServerUserData
{
    private readonly ILibraryManager _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="NewerLineUserData"/> class.
    /// </summary>
    /// <param name="server">The server's own user data manager.</param>
    /// <param name="library">The server's own library, which resolves a version identifier.</param>
    /// <exception cref="ArgumentNullException">The library is null.</exception>
    public NewerLineUserData(IUserDataManager server, ILibraryManager library)
        : base(server)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;
    }

    /// <inheritdoc />
    public override IReadOnlyDictionary<Guid, UserDataReading> ReadMany(User user, IReadOnlyList<BaseItem> items)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(items);

        var records = Server.GetUserDataBatch(items, user);
        var resumes = Server.GetResumeUserDataBatch(items, user);

        var readings = new Dictionary<Guid, UserDataReading>(items.Count);

        foreach (var item in items)
        {
            var record = records.TryGetValue(item.Id, out var found) ? found : Server.GetUserData(user, item);
            var version = resumes.TryGetValue(item.Id, out var resume) ? resume.VersionId : (Guid?)null;

            readings[item.Id] = new UserDataReading(MovedSetOf(record), RuntimeOf(version, item));
        }

        return readings;
    }

    /// <inheritdoc />
    protected override long? ResumeRuntimeTicks(User user, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return RuntimeOf(Server.GetResumeUserData(user, item)?.VersionId, item);
    }

    /// <summary>
    /// The runtime of the version an identifier names, falling back to the item's own.
    /// </summary>
    /// <param name="version">The version the server named, or null where it named none.</param>
    /// <param name="item">The leaf item the read was about.</param>
    /// <returns>The runtime in ticks, or null where neither carries one.</returns>
    private long? RuntimeOf(Guid? version, BaseItem item)
    {
        if (version is not Guid named)
        {
            return item.RunTimeTicks;
        }

        var resolved = _library.GetItemById(named);

        return resolved is null ? item.RunTimeTicks : resolved.RunTimeTicks;
    }
}
#endif

#if !NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.WatchSync.UserData;

/// <summary>
/// The adapter on the older supported line, which is the one whose server offers neither a batch
/// read nor any notion of which version drives a resume point.
///
/// It is compiled only into the target that line is built for. The two lines do not sit on one
/// framework, so this file and its counterpart are never both present in one assembly, and the
/// question of which one answers is settled by the build rather than at run time.
///
/// Both differences are answered honestly rather than approximated. A page costs one read per
/// item here, because the server offers nothing else, and a caller that wanted to know is told by
/// <c>docs/sync-model.md</c> rather than by a number this type invents. The runtime is the item's
/// own, because there is no version to name: the server presents a work held as several files as
/// one item and stops there.
/// </summary>
public sealed class OlderLineUserData : ServerUserData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OlderLineUserData"/> class.
    /// </summary>
    /// <param name="server">The server's own user data manager.</param>
    public OlderLineUserData(IUserDataManager server)
        : base(server)
    {
    }

    /// <inheritdoc />
    public override IReadOnlyDictionary<Guid, UserDataReading> ReadMany(User user, IReadOnlyList<BaseItem> items)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(items);

        var readings = new Dictionary<Guid, UserDataReading>(items.Count);

        foreach (var item in items)
        {
            readings[item.Id] = Read(user, item);
        }

        return readings;
    }

    /// <inheritdoc />
    protected override long? ResumeRuntimeTicks(User user, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.RunTimeTicks;
    }
}
#endif

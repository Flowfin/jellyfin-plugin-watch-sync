using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.WatchSync.UserData;

/// <summary>
/// What the two supported lines answer the same way, which is everything except how many reads a
/// page costs and where the runtime of a resumable version comes from.
///
/// This type holds the server's user data manager and is the only type in this plugin that does.
/// Every reach for it is a declared departure from the invariant #20 argues, listed one per call
/// in <c>Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt</c>, so the file that touches
/// the boundary is the file that says it does.
///
/// The two members that differ are abstract rather than switched on at run time. A line is chosen
/// when the plugin is compiled, because the two lines do not sit on one framework, so a branch
/// asking which line this is would be a branch one of the two builds can never take and no run
/// could ever cover.
/// </summary>
public abstract class ServerUserData : IUserDataGateway
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerUserData"/> class.
    /// </summary>
    /// <param name="server">The server's own user data manager.</param>
    /// <exception cref="ArgumentNullException">The manager is null.</exception>
    protected ServerUserData(IUserDataManager server)
    {
        ArgumentNullException.ThrowIfNull(server);

        Server = server;
    }

    /// <summary>
    /// Gets the server's own user data manager, which no type outside this adapter may name.
    /// </summary>
    protected IUserDataManager Server { get; }

    /// <inheritdoc />
    public UserDataReading Read(User user, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);

        return new UserDataReading(MovedSetOf(Server.GetUserData(user, item)), ResumeRuntimeTicks(user, item));
    }

    /// <inheritdoc />
    public abstract IReadOnlyDictionary<Guid, UserDataReading> ReadMany(User user, IReadOnlyList<BaseItem> items);

    /// <inheritdoc />
    public void Write(
        User user,
        BaseItem item,
        SyncedState state,
        UserDataSaveReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(state);

        var record = Server.GetUserData(user, item) ?? new UserItemData { Key = string.Empty };

        record.Played = state.Played;
        record.PlayCount = state.PlayCount;
        record.PlaybackPositionTicks = state.PlaybackPositionTicks;
        record.LastPlayedDate = state.LastPlayedDate;

        Server.SaveUserData(user, item, record, reason, cancellationToken);
    }

    /// <summary>
    /// Reads the moved set out of the server's own record for an item.
    ///
    /// Four fields out of the ten that record holds. Which four is decided in
    /// <c>docs/sync-model.md</c> and carried by <see cref="SyncedState"/>, and the reason this
    /// adapter copies rather than passing the server's record on is written on that type: a
    /// wire type that is the server's own carries every field the server adds to it on the day
    /// the server adds it.
    ///
    /// A record the server does not hold is carried through as nothing rather than as a set of
    /// zeroes. The server's own interface says it may answer with none, on both lines, and the
    /// two states read the same on a dashboard and differently to this plugin.
    /// </summary>
    /// <param name="record">
    /// The server's own record for one user and one item, or null where it holds none.
    /// </param>
    /// <returns>The moved set, or null where there is no record.</returns>
    protected static SyncedState? MovedSetOf(UserItemData? record) =>
        record is null
            ? null
            : new SyncedState(
                record.Played,
                record.PlayCount,
                record.PlaybackPositionTicks,
                record.LastPlayedDate);

    /// <summary>
    /// The runtime of the version this server would resume for one user and one item.
    ///
    /// This is the question the two lines answer in two different places, and answering it here
    /// is the whole reason this adapter exists. The number it returns is what
    /// <c>Jellyfin.Plugin.WatchSync.Versions.VersionLanding</c> compares a peer's runtime
    /// against, so a line that answered it with the wrong version's length would give that rule
    /// a confident answer about the wrong file.
    /// </summary>
    /// <param name="user">The local user the mapping names.</param>
    /// <param name="item">The leaf item.</param>
    /// <returns>The runtime in ticks, or null where there is none to read.</returns>
    protected abstract long? ResumeRuntimeTicks(User user, BaseItem item);
}

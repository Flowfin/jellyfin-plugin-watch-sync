using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.WatchSync.Tests.Apply;

/// <summary>
/// A server that keeps what was written to it and can be told to refuse one item.
///
/// The facts about the apply path are about the order a walk writes in and about what it leaves
/// behind when one write is refused, so what they need is a side that answers a read, keeps a
/// write, and refuses a named item. The two-server harness stands up a side of its own and cannot
/// be told to refuse anything, which is what this stands in for; a refusal is a state of one
/// write rather than of a link, so it belongs at this surface and not at that one.
///
/// It records every write in the order it arrived rather than only the last state per item. A
/// walk that put back what it had already written would leave the same state as one that never
/// did, and the order is the only place the difference is visible.
/// </summary>
internal sealed class RecordedWrites : IUserDataGateway
{
    private readonly Dictionary<Guid, SyncedState> _held = new Dictionary<Guid, SyncedState>();

    private readonly Dictionary<Guid, Exception> _refused = new Dictionary<Guid, Exception>();

    private readonly List<RecordedWrite> _writes = new List<RecordedWrite>();

    private Action? _afterEachWrite;

    /// <summary>
    /// Gets every write this side was asked for, in the order it arrived, refused ones included.
    /// </summary>
    internal IReadOnlyList<RecordedWrite> Writes => _writes;

    /// <summary>
    /// Refuses the next write against one item, with the failure the server would raise.
    /// </summary>
    /// <param name="itemId">The item to refuse.</param>
    /// <param name="failure">What the write is refused with.</param>
    internal void Refuse(Guid itemId, Exception failure) => _refused[itemId] = failure;

    /// <summary>
    /// Runs something after each write, which is how a case cancels a walk part-way through.
    /// </summary>
    /// <param name="afterEachWrite">What to run.</param>
    internal void OnWrite(Action afterEachWrite) => _afterEachWrite = afterEachWrite;

    /// <summary>
    /// What this side holds for one item, read without going through the write path.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The state, or null where this side holds none.</returns>
    internal SyncedState? HeldFor(Guid itemId) =>
        _held.TryGetValue(itemId, out var held) ? held : null;

    /// <inheritdoc />
    public UserDataReading Read(User user, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new UserDataReading(HeldFor(item.Id), item.RunTimeTicks);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, UserDataReading> ReadMany(User user, IReadOnlyList<BaseItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var readings = new Dictionary<Guid, UserDataReading>();

        foreach (var item in items)
        {
            readings[item.Id] = Read(user, item);
        }

        return readings;
    }

    /// <inheritdoc />
    public void Write(
        User user,
        BaseItem item,
        SyncedState state,
        UserDataSaveReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(state);

        _writes.Add(new RecordedWrite(item.Id, reason));

        if (_refused.TryGetValue(item.Id, out var failure))
        {
            throw failure;
        }

        _held[item.Id] = state;
        _afterEachWrite?.Invoke();
    }
}

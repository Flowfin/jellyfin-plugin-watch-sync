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

    private readonly Dictionary<Guid, Exception> _refusedReads = new Dictionary<Guid, Exception>();

    private readonly List<RecordedWrite> _writes = new List<RecordedWrite>();

    private Action? _afterEachWrite;

    /// <summary>
    /// Gets every write this side was asked for, in the order it arrived, refused ones included.
    /// </summary>
    internal IReadOnlyList<RecordedWrite> Writes => _writes;

    /// <summary>
    /// Puts a state on this side without going through the write path, so a case can say what the
    /// person already held before the walk reached the item.
    ///
    /// It is not a write and is deliberately not recorded as one. What the facts about provenance
    /// compare is the value a walk replaced against the value that was there, and a set-up that
    /// arrived through <see cref="Write"/> would put a row in the list the walk's own writes are
    /// counted in.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="state">What this side holds for it.</param>
    internal void Hold(Guid itemId, SyncedState state) => _held[itemId] = state;

    /// <summary>
    /// Refuses the next read of one item, with the failure the server would raise.
    ///
    /// A read is refused separately from a write because the walk makes both against one item and
    /// the two fail differently: a refused read is an item nothing was attempted on, and a refused
    /// write is one that was attempted and did not land. Neither may leave a record of provenance
    /// behind, and only a case that can produce each on its own says so.
    /// </summary>
    /// <param name="itemId">The item to refuse a read of.</param>
    /// <param name="failure">What the read is refused with.</param>
    internal void RefuseRead(Guid itemId, Exception failure) => _refusedReads[itemId] = failure;

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

        if (_refusedReads.TryGetValue(item.Id, out var failure))
        {
            throw failure;
        }

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

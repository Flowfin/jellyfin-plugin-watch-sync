using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Answers which local item carries a key, without asking the library each time.
///
/// A sweep asks that question once per incoming change, so a library scan per lookup is
/// what makes the obvious version of this plugin unusable at the size where somebody
/// actually needs it. The index is built by walking the library once and is then kept
/// current by the library's own events.
///
/// It is a cache and never a record. Losing it costs a rebuild and never a wrong answer:
/// every value in it is derived from the library, nothing is stored here that could not be
/// read again, and a lookup on an index that was cleared rebuilds and answers the same
/// thing. That is why <see cref="Clear"/> is safe to call at any moment and why nothing
/// else in this plugin may treat the index as the place a fact lives.
///
/// Two properties of the rebuild are what stop it becoming the outage it is meant to
/// prevent. It walks the library one page at a time, so a build holds one page rather than
/// the library. And it builds into a new map and swaps that in at the end, so the index is
/// never empty while it is being rebuilt: a lookup during a rebuild is answered from the
/// previous map, and the alternative, clearing first and refilling, would report every item
/// in the library as unmatched for the length of the walk.
/// </summary>
public sealed class MatchIndex
{
    /// <summary>
    /// How many items one read of the library asks for.
    ///
    /// The number bounds what a build holds at once, which is the whole reason the walk is
    /// paged. It is not a tuning knob for a setting page: an operator who changes it changes
    /// how much memory a rebuild takes and nothing an operator can observe, so it is a
    /// constant here rather than one more thing to configure and get wrong.
    /// </summary>
    public const int PageSize = 500;

    private readonly IMatchIndexSource _source;

    /// <summary>
    /// Held for the whole of a walk, so two rebuilds cannot interleave their pages into one
    /// map. It is taken before <see cref="_gate"/> and never after it, which is what keeps
    /// the pair from deadlocking.
    /// </summary>
    private readonly object _rebuildGate = new object();

    /// <summary>
    /// Held for as long as it takes to read or change the maps, and never across a read of
    /// the library. A lookup that waited on the library would be the linear cost this index
    /// exists to remove, arriving as a delay rather than as a scan.
    /// </summary>
    private readonly object _gate = new object();

    private Dictionary<MatchKey, List<Guid>> _itemsByKey = new Dictionary<MatchKey, List<Guid>>();

    private Dictionary<Guid, MatchKey> _keyByItem = new Dictionary<Guid, MatchKey>();

    private List<PendingChange>? _journal;

    private bool _isBuilt;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchIndex"/> class.
    /// </summary>
    /// <param name="source">Where the library's keyed items are read from.</param>
    public MatchIndex(IMatchIndexSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
    }

    /// <summary>
    /// Gets a value indicating whether a map has been built.
    ///
    /// It reports what the index has done and never what a caller may do. A lookup on an
    /// index that reports false answers the same thing it would have answered on one that
    /// reports true, and it takes a walk of the library to do it.
    /// </summary>
    public bool IsBuilt
    {
        get
        {
            lock (_gate)
            {
                return _isBuilt;
            }
        }
    }

    /// <summary>
    /// Walks the library and replaces the map with what it found.
    ///
    /// This is what a start and a scheduled sweep call. Events that arrive while the walk is
    /// running are held and applied to the new map once it is in place, so an item added
    /// during a rebuild is in the index when the rebuild finishes rather than missing from
    /// it until the next one.
    /// </summary>
    public void Rebuild() => Build(false);

    /// <summary>
    /// Drops the map.
    ///
    /// The next lookup walks the library again and answers what it would have answered
    /// anyway. Nothing else changes, which is the property that makes this a cache.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _itemsByKey = new Dictionary<MatchKey, List<Guid>>();
            _keyByItem = new Dictionary<Guid, MatchKey>();
            _isBuilt = false;
        }
    }

    /// <summary>
    /// Answers which local item carries the key.
    ///
    /// One of the three answers <c>docs/matching.md</c> fixes, and the ambiguity is refused
    /// here rather than resolved. A key that two local items claim produces no item at all:
    /// taking the first would land the watch state on one of them at random and on a
    /// different one after the next scan, which is the failure that looks exactly like a
    /// working sync.
    /// </summary>
    /// <param name="key">The key to resolve.</param>
    /// <returns>The answer.</returns>
    public MatchLookup Lookup(MatchKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        EnsureBuilt();

        lock (_gate)
        {
            if (!_itemsByKey.TryGetValue(key, out var items) || items.Count == 0)
            {
                return MatchLookup.NoMatch();
            }

            if (items.Count == 1)
            {
                return MatchLookup.Matched(items[0]);
            }

            return MatchLookup.Ambiguous(items.ToArray());
        }
    }

    /// <summary>
    /// Takes in an item the library has just gained.
    /// </summary>
    /// <param name="item">The item and the key it produced.</param>
    public void ItemAdded(KeyedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Record(new PendingChange(item.ItemId, item.Key));
    }

    /// <summary>
    /// Takes in an item whose metadata has changed.
    ///
    /// The key it produced is re-read rather than assumed unchanged, because the ordinary
    /// reason an item is updated is that somebody repaired the metadata this plugin keys on.
    /// An item that stopped producing a key at all is <see cref="ItemRemoved"/> as far as
    /// the index is concerned: it has nothing to be held under, and what an operator is told
    /// about it is the unmatched record in #26.
    /// </summary>
    /// <param name="item">The item and the key it now produces.</param>
    public void ItemUpdated(KeyedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Record(new PendingChange(item.ItemId, item.Key));
    }

    /// <summary>
    /// Takes out an item the library has lost.
    ///
    /// Only that item leaves. The index is never emptied by an event, which is what a
    /// library rescan depends on: a rescan removes and re-adds items one after another, and
    /// an index that treated a removal as a reason to start again would report every other
    /// item as unmatched for as long as the rescan ran.
    /// </summary>
    /// <param name="itemId">The item's identifier on this server.</param>
    public void ItemRemoved(Guid itemId) => Record(new PendingChange(itemId, null));

    private void EnsureBuilt() => Build(true);

    private void Build(bool onlyIfMissing)
    {
        lock (_rebuildGate)
        {
            lock (_gate)
            {
                if (onlyIfMissing && _isBuilt)
                {
                    return;
                }

                _journal = new List<PendingChange>();
            }

            Dictionary<MatchKey, List<Guid>>? finishedItemsByKey = null;
            Dictionary<Guid, MatchKey>? finishedKeyByItem = null;

            try
            {
                var itemsByKey = new Dictionary<MatchKey, List<Guid>>();
                var keyByItem = new Dictionary<Guid, MatchKey>();

                Walk(itemsByKey, keyByItem);

                finishedItemsByKey = itemsByKey;
                finishedKeyByItem = keyByItem;
            }
            finally
            {
                Adopt(finishedItemsByKey, finishedKeyByItem);
            }
        }
    }

    /// <summary>
    /// Reads the library a page at a time into the map being built.
    ///
    /// A page shorter than the one asked for is the end of the library. Nothing here holds
    /// more than one page, and nothing holds the lock while the library is being read.
    /// </summary>
    /// <param name="itemsByKey">The map being built.</param>
    /// <param name="keyByItem">The reverse map being built.</param>
    private void Walk(Dictionary<MatchKey, List<Guid>> itemsByKey, Dictionary<Guid, MatchKey> keyByItem)
    {
        var startIndex = 0;

        while (true)
        {
            var page = _source.ReadPage(startIndex, PageSize);

            if (page is null || page.Count == 0)
            {
                return;
            }

            foreach (var item in page)
            {
                Apply(itemsByKey, keyByItem, new PendingChange(item.ItemId, item.Key));
            }

            if (page.Count < PageSize)
            {
                return;
            }

            startIndex += page.Count;
        }
    }

    /// <summary>
    /// Puts a finished map in place and applies what arrived while it was being built.
    ///
    /// The swap is the whole of the visible change, so a lookup sees the map before it or
    /// the map after it and never a half-filled one. A walk that failed hands over nothing,
    /// and the held changes are then applied to the map that is still in place rather than
    /// dropped.
    /// </summary>
    /// <param name="itemsByKey">The map the walk built, or null where it did not finish.</param>
    /// <param name="keyByItem">The reverse map the walk built, or null for the same reason.</param>
    private void Adopt(Dictionary<MatchKey, List<Guid>>? itemsByKey, Dictionary<Guid, MatchKey>? keyByItem)
    {
        lock (_gate)
        {
            var held = _journal;

            _journal = null;

            if (itemsByKey is not null && keyByItem is not null)
            {
                _itemsByKey = itemsByKey;
                _keyByItem = keyByItem;
                _isBuilt = true;
            }

            if (held is null)
            {
                return;
            }

            foreach (var change in held)
            {
                Apply(_itemsByKey, _keyByItem, change);
            }
        }
    }

    /// <summary>
    /// Applies one change, or holds it where a walk is in flight.
    /// </summary>
    /// <param name="change">The change.</param>
    private void Record(PendingChange change)
    {
        lock (_gate)
        {
            if (_journal is not null)
            {
                _journal.Add(change);

                return;
            }

            Apply(_itemsByKey, _keyByItem, change);
        }
    }

    /// <summary>
    /// Puts one item at its key, taking it off whichever key it was under before.
    ///
    /// The reverse map is what makes that possible. Without it an item whose metadata was
    /// repaired would stay listed under the key it no longer produces, and the index would
    /// answer with an item that has moved, which is the wrong-item write this plugin refuses
    /// everywhere else.
    /// </summary>
    /// <param name="itemsByKey">The map to change.</param>
    /// <param name="keyByItem">The reverse map to change.</param>
    /// <param name="change">The change.</param>
    private static void Apply(
        Dictionary<MatchKey, List<Guid>> itemsByKey,
        Dictionary<Guid, MatchKey> keyByItem,
        PendingChange change)
    {
        if (keyByItem.TryGetValue(change.ItemId, out var previous))
        {
            if (itemsByKey.TryGetValue(previous, out var holders))
            {
                holders.Remove(change.ItemId);

                if (holders.Count == 0)
                {
                    itemsByKey.Remove(previous);
                }
            }

            keyByItem.Remove(change.ItemId);
        }

        if (change.Key is null)
        {
            return;
        }

        keyByItem[change.ItemId] = change.Key;

        if (!itemsByKey.TryGetValue(change.Key, out var items))
        {
            items = new List<Guid>();
            itemsByKey[change.Key] = items;
        }

        if (!items.Contains(change.ItemId))
        {
            items.Add(change.ItemId);
        }
    }

    /// <summary>
    /// One library event as the index carries it: the item, and the key it produces now, or
    /// nothing where it produces none.
    /// </summary>
    private readonly struct PendingChange
    {
        public PendingChange(Guid itemId, MatchKey? key)
        {
            ItemId = itemId;
            Key = key;
        }

        public Guid ItemId { get; }

        public MatchKey? Key { get; }
    }
}

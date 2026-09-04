using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Where the index reads this server's own library from, which is the half of #29 that had no
/// implementation: every reader of <see cref="IMatchIndexSource"/> in this repository was a fake
/// in the suite, so the index was proven against libraries nobody has.
///
/// One implementation rather than one per line. The user data adapter beside this one is split
/// because the two supported lines answer that question with different members; the two calls
/// used here are the same on both, measured at the packages this plugin compiles against rather
/// than assumed from one of them:
///
/// <code>
/// grep -o 'M:MediaBrowser\.Controller\.Library\.ILibraryManager\.GetItem\(Ids\|ById\)([^)]*)' \
///   "$GP/jellyfin.controller/10.11.11/lib/net9.0/MediaBrowser.Controller.xml" | sort -u
/// </code>
///
/// returns the same pair on <c>12.0.0-rc4/lib/net10.0</c>, so a file per line here would be two
/// copies of one call and a place for them to drift.
///
/// What it reads is the leaf items a transfer can be about, which is what
/// <see cref="Model.TransferSubject"/> admits, and never a season, a series or a box set. An
/// aggregate has no user data of its own to carry, <c>docs/sync-model.md</c> refuses it one layer
/// up, and a key in the map that nothing may ever look up is a key that can only be matched by
/// mistake.
/// </summary>
public sealed class ServerLibrary : IMatchIndexSource
{
    private readonly ILibraryManager _library;

    /// <summary>
    /// Held for the length of one page, because the snapshot and the resume point below are read
    /// and written by every call. The index walks under a gate of its own, so this is
    /// uncontended in the ordinary case and is here for the case where something else asks at
    /// the same moment.
    /// </summary>
    private readonly object _gate = new object();

    private IReadOnlyList<Guid> _snapshot = Array.Empty<Guid>();

    /// <summary>
    /// How many keyed items had been handed out when the walk stood at
    /// <see cref="_resumeOffset"/> in the snapshot, or a negative number where no page has been
    /// answered from the snapshot yet.
    /// </summary>
    private int _resumeDelivered = -1;

    private int _resumeOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerLibrary"/> class.
    /// </summary>
    /// <param name="library">The server's own library manager.</param>
    public ServerLibrary(ILibraryManager library)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;
    }

    /// <inheritdoc />
    public IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        lock (_gate)
        {
            if (startIndex == 0)
            {
                _snapshot = _library.GetItemIds(Everything());
                _resumeDelivered = -1;
                _resumeOffset = 0;
            }

            var resumable = _resumeDelivered == startIndex;
            var offset = resumable ? _resumeOffset : 0;
            var keyed = resumable ? startIndex : 0;
            var page = new List<KeyedItem>(count);

            while (offset < _snapshot.Count && page.Count < count)
            {
                var id = _snapshot[offset];
                offset++;

                var item = _library.GetItemById(id);

                if (item is null)
                {
                    continue;
                }

                var key = KeyOf(item);

                if (key is null)
                {
                    continue;
                }

                keyed++;

                if (keyed <= startIndex)
                {
                    continue;
                }

                page.Add(new KeyedItem(item.Id, key));
            }

            _resumeDelivered = startIndex + page.Count;
            _resumeOffset = offset;

            return page;
        }
    }

    /// <summary>
    /// The key one library item produced, or null where it produced none.
    ///
    /// The derivation is <see cref="LibraryItemKey"/>'s rather than this type's, because the
    /// events that keep the index current ask the same question of the same items and a copy
    /// here would be a second answer nothing compares against the first.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The key, or null.</returns>
    private MatchKey? KeyOf(BaseItem item) => LibraryItemKey.Of(_library, item);

    /// <summary>
    /// The query the snapshot is taken with.
    ///
    /// It asks for identifiers rather than for items, and that is what makes the walk paged at
    /// all. The server offers no total order over items to page an offset against: its sort
    /// vocabulary is what a person sorts a screen by, and none of its members is unique, so two
    /// offset reads of one library may return an item twice and miss another. A list of
    /// identifiers taken once is a position that means something, and it costs sixteen bytes per
    /// leaf item rather than the item graph the interface's bound is about.
    ///
    /// A virtual item is excluded. It is an episode the library knows about and holds no file
    /// for, so nobody watched it here, and a key resolving to it is one no write can land on.
    /// </summary>
    /// <returns>The query.</returns>
    private static InternalItemsQuery Everything() =>
        new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            Recursive = true,
            IsVirtualItem = false,
        };
}

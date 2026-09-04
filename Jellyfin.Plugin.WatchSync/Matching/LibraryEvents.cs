using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// What keeps the match index current between rebuilds, which is the half of #29 that had no
/// caller: the index carried the three changes and nothing ever handed it a library event, so
/// the rules were proven of the index and unproven of a running server.
///
/// The subscription is made when the server starts this service and unmade when it stops it.
/// Both halves matter and the second is the one that is easy to leave out: an event handler
/// still attached to the server's library after this plugin has been disabled is a plugin that
/// keeps working after an operator turned it off, and it holds the index and everything the
/// index holds alive for as long as the server runs.
///
/// It is a hosted service because neither supported line offers anything else. The entry-point
/// type the older template used is on neither line's assembly, measured at the packages this
/// plugin compiles against, so a lifetime hook is the hosting abstractions or nothing. What
/// that costs is one package reference per line, and the argument for paying it is in the
/// project file next to the reference.
///
/// What it does NOT do is start a walk. An event carries the item it is about, so a change is
/// applied to the map in place; the rebuild is the scheduled sweep's, and a handler that
/// rebuilt would turn a library scan into one walk of the library per item scanned.
/// </summary>
public sealed class LibraryEvents : IHostedService
{
    private readonly ILibraryManager _library;

    private readonly MatchIndex _index;

    /// <summary>
    /// Held for the length of a subscribe or an unsubscribe, so that a stop racing a start
    /// cannot leave a handler attached to a library nothing will detach it from. The two calls
    /// are made by the server's own lifetime and are not expected to contend; the lock is here
    /// for the case where they do rather than for the ordinary one.
    /// </summary>
    private readonly object _gate = new object();

    private bool _isSubscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryEvents"/> class.
    /// </summary>
    /// <param name="library">The server's own library manager.</param>
    /// <param name="index">The index the events keep current.</param>
    public LibraryEvents(ILibraryManager library, MatchIndex index)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(index);

        _library = library;
        _index = index;
    }

    /// <summary>
    /// Subscribes to the library's own events.
    ///
    /// A second start attaches nothing, because a handler attached twice applies every change
    /// twice and the second application of a removal is what takes an item out of the index
    /// that the first one had already put back.
    /// </summary>
    /// <param name="cancellationToken">The token the server offers, which nothing here waits on.</param>
    /// <returns>A completed task, because subscribing does no input and output.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_isSubscribed)
            {
                return Task.CompletedTask;
            }

            _library.ItemAdded += OnItemAdded;
            _library.ItemUpdated += OnItemUpdated;
            _library.ItemRemoved += OnItemRemoved;
            _isSubscribed = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Unsubscribes from the library's own events.
    ///
    /// A stop with no start behind it detaches handlers that were never attached, which on a
    /// multicast delegate is a silent no-op. There is deliberately no guard against it: a guard
    /// there would be a branch nothing can refuse, because the guarded and the unguarded
    /// spelling do the same thing, and a branch nothing can refuse reads as one that is
    /// covered.
    /// </summary>
    /// <param name="cancellationToken">The token the server offers, which nothing here waits on.</param>
    /// <returns>A completed task, because unsubscribing does no input and output.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _library.ItemAdded -= OnItemAdded;
            _library.ItemUpdated -= OnItemUpdated;
            _library.ItemRemoved -= OnItemRemoved;
            _isSubscribed = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether an item is one a transfer can be about at all.
    ///
    /// The same two kinds the walk reads, and for the same reason: an aggregate has no user
    /// data of its own to carry, so a key resolving to one is a key that can only be matched by
    /// mistake. A virtual item is excluded as well - the library knows about it and holds no
    /// file for it, so nobody watched it here.
    ///
    /// An item that is neither is ignored rather than removed. A removal on a series would take
    /// nothing out, because a series was never put in, and a handler that treated every kind as
    /// a candidate would be one line away from putting a series in under a film's key.
    ///
    /// An absent item answers the same way and is not a second test. The item on the server's
    /// event argument is a settable property, so an event may carry none, and a separate null
    /// guard beside this one would be a branch this test already decides.
    /// </summary>
    /// <param name="item">The item the event carried, which may be absent.</param>
    /// <returns>Whether the index is about it.</returns>
    private static bool IsALeaf(BaseItem? item) =>
        item is Movie or Episode && !item.IsVirtualItem;

    private void OnItemAdded(object? sender, ItemChangeEventArgs e) => Take(e, true);

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e) => Take(e, false);

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;

        if (!IsALeaf(item))
        {
            return;
        }

        _index.ItemRemoved(item.Id);
    }

    /// <summary>
    /// Takes in an item the library has gained or changed.
    ///
    /// The key is re-read rather than assumed, because the ordinary reason an item is updated
    /// is that somebody repaired the metadata this plugin keys on. An item that produces no key
    /// any more leaves the index rather than staying in it under the key it used to have: it
    /// has nothing to be held under, and an entry nothing can produce again is exactly the
    /// entry that resolves a peer's change onto the wrong file.
    ///
    /// The branch below names the index member the event's own name asks for, and no fact here
    /// separates the two. <c>ItemAdded</c> and <c>ItemUpdated</c> record the same change on the
    /// index today, so an added item routed through the second answers what the first would
    /// have answered; the branch is a claim about which member each event belongs to rather
    /// than about what either one does, and removing it reddens nothing. That is stated rather
    /// than left for a reader to discover, because a branch nothing can refuse looks exactly
    /// like one that is covered.
    /// </summary>
    /// <param name="e">The event the library raised.</param>
    /// <param name="isNew">Whether the library gained the item rather than changed it.</param>
    private void Take(ItemChangeEventArgs e, bool isNew)
    {
        var item = e.Item;

        if (!IsALeaf(item))
        {
            return;
        }

        var key = LibraryItemKey.Of(_library, item);

        if (key is null)
        {
            _index.ItemRemoved(item.Id);

            return;
        }

        var keyed = new KeyedItem(item.Id, key);

        if (isNew)
        {
            _index.ItemAdded(keyed);

            return;
        }

        _index.ItemUpdated(keyed);
    }
}

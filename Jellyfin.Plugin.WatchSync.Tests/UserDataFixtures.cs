using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Moq;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the facts about the user data adapter are built out of.
///
/// The adapter this plugin compiles is decided by the target, because the two supported server
/// lines do not sit on one framework. <see cref="ForTheLineThisBuildIsFor"/> is the one place
/// that knows which, so a fact about what a caller sees is written once and runs on both legs
/// of the suite rather than twice with a chance of the two drifting.
/// </summary>
internal static class UserDataFixtures
{
    /// <summary>
    /// A local user. The mapping the pairing plugin hands over names one of these, and nothing
    /// here compares its name to anything, which is the invariant #42 refuses the other of.
    /// </summary>
    /// <returns>The user.</returns>
    internal static User Someone() => new User("someone", "provider", "reset");

    /// <summary>
    /// A leaf item carrying an identifier and a runtime.
    /// </summary>
    /// <param name="id">The item's identifier.</param>
    /// <param name="runtimeTicks">Its runtime, or null where the server has not analysed it.</param>
    /// <returns>The item.</returns>
    internal static BaseItem Work(Guid id, long? runtimeTicks) =>
        new Movie { Id = id, RunTimeTicks = runtimeTicks };

    /// <summary>
    /// The server's own record for one user and one item.
    /// </summary>
    /// <param name="played">Whether the person watched the work.</param>
    /// <param name="playCount">How often.</param>
    /// <param name="positionTicks">Where they stopped.</param>
    /// <param name="lastPlayed">When they last did, or null.</param>
    /// <returns>The record.</returns>
    internal static UserItemData Record(
        bool played,
        int playCount,
        long positionTicks,
        DateTime? lastPlayed) =>
        new UserItemData
        {
            Key = "a-key",
            Played = played,
            PlayCount = playCount,
            PlaybackPositionTicks = positionTicks,
            LastPlayedDate = lastPlayed,
        };

    /// <summary>
    /// Makes a page cost whatever this line charges for one, so that a fact about what a caller
    /// sees does not have to know which line is answering.
    ///
    /// On the line that offers a batch the batch answers for nothing, which sends the adapter to
    /// the per item read the other line has to use anyway. What a batch is worth is a fact of
    /// that line's own, in the file compiled only there, and asserting it here would make every
    /// fact about a page a fact about one line.
    /// </summary>
    /// <param name="server">The server's own user data manager.</param>
    /// <param name="user">The local user the mapping names.</param>
    internal static void PagesAreReadItemByItem(Mock<IUserDataManager> server, User user)
    {
#if NET10_0_OR_GREATER
        server
            .Setup(manager => manager.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, UserItemData>());
        server
            .Setup(manager => manager.GetResumeUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, VersionResumeData>());
#else
        _ = server;
        _ = user;
#endif
    }

    /// <summary>
    /// The adapter for whichever line this build is for, holding the manager it is given and
    /// nothing else the caller can see.
    ///
    /// On the newer line it is also handed a library that resolves nothing, because a fact about
    /// what a caller sees may not depend on which line is answering. What the newer line does
    /// with a version it can resolve is a fact of its own, in the file that is compiled only
    /// there.
    /// </summary>
    /// <param name="server">The server's own user data manager.</param>
    /// <returns>The adapter.</returns>
    internal static IUserDataGateway ForTheLineThisBuildIsFor(IUserDataManager server)
    {
#if NET10_0_OR_GREATER
        return new NewerLineUserData(server, Mock.Of<ILibraryManager>());
#else
        return new OlderLineUserData(server);
#endif
    }
}

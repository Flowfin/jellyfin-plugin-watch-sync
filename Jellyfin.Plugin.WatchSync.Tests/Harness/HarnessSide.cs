using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Storage;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;

namespace Jellyfin.Plugin.WatchSync.Tests.Harness;

/// <summary>
/// One server, as much of one as this plugin ever sees.
///
/// A side is a store under a directory of its own, a user data record of its own, a library of
/// its own, a clock of its own and an inbox the link fills. Nothing on it is shared with the
/// other side, and <c>TwoServersTests</c> asserts that for each of the four rather than leaving
/// it to the reader: two sides sharing a store or a clock make a case pass for the wrong reason,
/// and the reason is invisible in the case body.
///
/// It is not a Jellyfin server. There is no host, no database and no network, which the headless
/// rule refuses and names this harness as the replacement for. What stands in for the server is
/// the one interface this plugin reads and writes a record through, which is #20's adapter over a
/// manager holding a dictionary, so a write this plugin makes is a write the next read on the
/// same side finds and the far side does not.
/// </summary>
internal sealed class HarnessSide : IDisposable
{
    private readonly TemporaryDirectory _programData;

    private readonly Dictionary<(Guid User, Guid Item), UserItemData> _records =
        new Dictionary<(Guid User, Guid Item), UserItemData>();

    private readonly List<BaseItem> _library = new List<BaseItem>();

    private readonly List<string> _inbox = new List<string>();

    private HarnessSide(string name, TemporaryDirectory programData, string dataPath, DateTimeOffset startsAt)
    {
        Name = name;
        _programData = programData;
        DataPath = dataPath;
        Clock = new HarnessClock(startsAt);

        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(dataPath);

        Store = new DocumentStore(new StoreFolder(paths.Object));
        UserData = UserDataFixtures.ForTheLineThisBuildIsFor(AServerHoldingThisSidesRecords());
    }

    /// <summary>
    /// Gets what this side is called, which lands in its directory name so a leftover found after
    /// a run says which side of which case to look at.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// Gets the path this side's server would hand the plugin as its data path.
    /// </summary>
    internal string DataPath { get; }

    /// <summary>
    /// Gets this side's store, under this side's own directory.
    /// </summary>
    internal DocumentStore Store { get; }

    /// <summary>
    /// Gets the interface this side's records are read and written through.
    /// </summary>
    internal IUserDataGateway UserData { get; }

    /// <summary>
    /// Gets this side's clock.
    /// </summary>
    internal HarnessClock Clock { get; }

    /// <summary>
    /// Gets this side's library, which a case fills with whatever that side holds.
    ///
    /// It is a list a case may add to and take from, because the two sides holding different
    /// items is the ordinary case rather than a fault: an item the peer has and this server does
    /// not is what the whole of the matching milestone is about.
    /// </summary>
    internal IList<BaseItem> Library => _library;

    /// <summary>
    /// Gets the envelope bodies the link has carried to this side, in the order they arrived.
    /// </summary>
    internal IReadOnlyList<string> Inbox => _inbox;

    /// <summary>
    /// Stands a side up, with a directory of its own that goes away with it.
    /// </summary>
    /// <param name="name">What this side is called.</param>
    /// <param name="startsAt">The moment this side's clock begins at.</param>
    /// <returns>The side.</returns>
    internal static HarnessSide Create(string name, DateTimeOffset startsAt)
    {
        var programData = TemporaryDirectory.Create(name);
        var dataPath = Path.Combine(programData.FullPath, "data");
        Directory.CreateDirectory(dataPath);

        return new HarnessSide(name, programData, dataPath, startsAt);
    }

    /// <summary>
    /// Reads what this side holds for one user and one item, without going through the adapter.
    ///
    /// A case asserting that the far side was not touched needs to read the far side, and reading
    /// it through the thing under test would make the assertion depend on the adapter it is not
    /// about.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="item">The item.</param>
    /// <returns>What this side holds, or null where it holds nothing.</returns>
    internal UserItemData? HeldFor(User user, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);

        return _records.TryGetValue((user.Id, item.Id), out var held) ? held : null;
    }

    /// <summary>
    /// Takes a body the link carried to this side.
    /// </summary>
    /// <param name="body">The envelope body, as text.</param>
    internal void Receive(string body) => _inbox.Add(body);

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The manager standing in for this side's server, answering out of this side's dictionary.
    ///
    /// The dictionary is the whole of the independence between the two sides on this surface: a
    /// write reaches one dictionary, and nothing either side does can reach the other's.
    /// </summary>
    /// <returns>The manager.</returns>
    private IUserDataManager AServerHoldingThisSidesRecords()
    {
        var server = new Mock<IUserDataManager>();

        server
            .Setup(manager => manager.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((User user, BaseItem item) => HeldFor(user, item)!);

        server
            .Setup(manager => manager.SaveUserData(
                It.IsAny<User>(),
                It.IsAny<BaseItem>(),
                It.IsAny<UserItemData>(),
                It.IsAny<UserDataSaveReason>(),
                It.IsAny<CancellationToken>()))
            .Callback(
                (User user, BaseItem item, UserItemData record, UserDataSaveReason reason, CancellationToken cancellationToken) =>
                {
                    _ = reason;
                    _ = cancellationToken;
                    _records[(user.Id, item.Id)] = record;
                });

        PagesAreReadItemByItem(server);

        return server.Object;
    }

    /// <summary>
    /// Makes a page cost whatever this line charges for one, for the reason
    /// <see cref="UserDataFixtures.PagesAreReadItemByItem"/> gives: on the line that offers a
    /// batch the batch answers for nothing, which sends the adapter to the per item read the
    /// other line has to use anyway, so a fact written here does not become a fact about one
    /// line.
    /// </summary>
    /// <param name="server">The manager standing in for this side's server.</param>
    private static void PagesAreReadItemByItem(Mock<IUserDataManager> server)
    {
#if NET10_0_OR_GREATER
        server
            .Setup(manager => manager.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<User>()))
            .Returns(new Dictionary<Guid, UserItemData>());
        server
            .Setup(manager => manager.GetResumeUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<User>()))
            .Returns(new Dictionary<Guid, VersionResumeData>());
#else
        _ = server;
#endif
    }
}

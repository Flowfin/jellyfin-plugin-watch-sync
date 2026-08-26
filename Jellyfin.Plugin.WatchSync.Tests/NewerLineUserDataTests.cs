#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the newer supported line answers that the older one cannot, which is the half of #20 that
/// the adapter exists for.
///
/// This file is compiled only into the target that line is built for. The facts here are about
/// the two members that line adds: the batch read, which is what makes a pass over a large
/// library affordable, and the pair that names which version drives a resume point, which is the
/// number <c>VersionLanding</c> measures a peer's position against under #28.
///
/// A fact here is not a fact about what a caller sees. Those are in
/// <see cref="UserDataGatewayTests"/> and run on both legs, which is where the promise that the
/// two lines behave alike is kept.
/// </summary>
public class NewerLineUserDataTests
{
    private static readonly long NinetyMinutes = TimeSpan.FromMinutes(90).Ticks;

    private static readonly long HundredMinutes = TimeSpan.FromMinutes(100).Ticks;

    /// <summary>
    /// The version the server names is the one whose length answers, and it is not the item's.
    /// This is the whole difference between the two lines: an extended cut and a theatrical one
    /// are one item to the server, and a tick counts from the start of one particular file.
    /// </summary>
    [Fact]
    public void TheRuntimeIsTheOneOfTheVersionTheServerWouldResume()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);
        var version = UserDataFixtures.Work(Guid.NewGuid(), HundredMinutes);

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetResumeUserData(user, work))
            .Returns(new VersionResumeData(version.Id, UserDataFixtures.Record(false, 0, 10, null)));

        var library = new Mock<ILibraryManager>();
        library.Setup(manager => manager.GetItemById(version.Id)).Returns(version);

        var reading = new NewerLineUserData(server.Object, library.Object).Read(user, work);

        Assert.Equal(HundredMinutes, reading.ResumeRuntimeTicks);
    }

    /// <summary>
    /// Where the server names no version the item has none with a resume point, and the item's
    /// own length is the answer. It is the same answer the older line gives for the same item,
    /// which is what makes that line's absence of these members an absence rather than a
    /// difference in behaviour.
    /// </summary>
    [Fact]
    public void AnItemWithNoResumeVersionAnswersWithItsOwnRuntime()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);

        var reading = new NewerLineUserData(Mock.Of<IUserDataManager>(), Mock.Of<ILibraryManager>())
            .Read(user, work);

        Assert.Equal(NinetyMinutes, reading.ResumeRuntimeTicks);
    }

    /// <summary>
    /// A version identifier that resolves to nothing is an item that went out from under the
    /// read. The item's own length answers, rather than nothing at all, because answering with
    /// nothing would tell the rule in #28 that the two versions are too far apart when what
    /// happened is that one of them was not found.
    /// </summary>
    [Fact]
    public void AVersionThatResolvesToNothingFallsBackToTheItemsOwnRuntime()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);
        var vanished = Guid.NewGuid();

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetResumeUserData(user, work))
            .Returns(new VersionResumeData(vanished, UserDataFixtures.Record(false, 0, 10, null)));

        var reading = new NewerLineUserData(server.Object, Mock.Of<ILibraryManager>()).Read(user, work);

        Assert.Equal(NinetyMinutes, reading.ResumeRuntimeTicks);
    }

    /// <summary>
    /// A page costs one read rather than one per item, which is the reason this line has an
    /// implementation of its own. The count is asserted rather than the call merely being made,
    /// because a batch read used beside a loop is the shape that passes every other fact here
    /// and pays the older line's cost anyway.
    /// </summary>
    [Fact]
    public void APageIsOneReadRatherThanOnePerItem()
    {
        var user = UserDataFixtures.Someone();
        var page = new[]
        {
            UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes),
            UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes),
            UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes),
        };

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, UserItemData>
            {
                [page[0].Id] = UserDataFixtures.Record(true, 1, 0, null),
                [page[1].Id] = UserDataFixtures.Record(false, 0, 5, null),
                [page[2].Id] = UserDataFixtures.Record(false, 0, 0, null),
            });
        server
            .Setup(manager => manager.GetResumeUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, VersionResumeData>());

        var readings = new NewerLineUserData(server.Object, Mock.Of<ILibraryManager>())
            .ReadMany(user, page);

        Assert.Equal(3, readings.Count);

        server.Verify(
            manager => manager.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user),
            Times.Once);
        server.Verify(
            manager => manager.GetUserData(It.IsAny<Jellyfin.Database.Implementations.Entities.User>(), It.IsAny<BaseItem>()),
            Times.Never);
    }

    /// <summary>
    /// A page reads the resume versions in one call as well, and an item the server omits from
    /// that answer is one with no version to resume, so its own length answers. The server's own
    /// interface says items without one are omitted rather than present and empty.
    /// </summary>
    [Fact]
    public void APageTakesEachRuntimeFromTheVersionTheServerNamedForThatItem()
    {
        var user = UserDataFixtures.Someone();
        var versioned = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);
        var plain = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);
        var version = UserDataFixtures.Work(Guid.NewGuid(), HundredMinutes);

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, UserItemData>
            {
                [versioned.Id] = UserDataFixtures.Record(false, 0, 10, null),
                [plain.Id] = UserDataFixtures.Record(false, 0, 20, null),
            });
        server
            .Setup(manager => manager.GetResumeUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, VersionResumeData>
            {
                [versioned.Id] = new VersionResumeData(version.Id, UserDataFixtures.Record(false, 0, 10, null)),
            });

        var library = new Mock<ILibraryManager>();
        library.Setup(manager => manager.GetItemById(version.Id)).Returns(version);

        var readings = new NewerLineUserData(server.Object, library.Object)
            .ReadMany(user, new[] { versioned, plain });

        Assert.Equal(HundredMinutes, readings[versioned.Id].ResumeRuntimeTicks);
        Assert.Equal(NinetyMinutes, readings[plain.Id].ResumeRuntimeTicks);

        server.Verify(
            manager => manager.GetResumeUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user),
            Times.Once);
    }

    /// <summary>
    /// An item the batch omits is still answered, by asking for it on its own. The server's
    /// interface does not say a batch answers for every item it was handed, and an item silently
    /// dropped from a page is a difference between two servers that nothing would ever report.
    /// </summary>
    [Fact]
    public void AnItemTheBatchOmitsIsStillAsAnswered()
    {
        var user = UserDataFixtures.Someone();
        var present = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);
        var omitted = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, UserItemData>
            {
                [present.Id] = UserDataFixtures.Record(true, 2, 0, null),
            });
        server
            .Setup(manager => manager.GetResumeUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), user))
            .Returns(new Dictionary<Guid, VersionResumeData>());
        server
            .Setup(manager => manager.GetUserData(user, omitted))
            .Returns(UserDataFixtures.Record(false, 0, 700, null));

        var readings = new NewerLineUserData(server.Object, Mock.Of<ILibraryManager>())
            .ReadMany(user, new[] { present, omitted });

        Assert.Equal(2, readings[present.Id].State?.PlayCount);
        Assert.Equal(700, readings[omitted.Id].State?.PlaybackPositionTicks);
    }

    /// <summary>
    /// The adapter refuses to be built without the library, because the version the server names
    /// is an identifier and nothing else resolves one.
    /// </summary>
    [Fact]
    public void TheAdapterRefusesToBeBuiltWithoutTheLibrary()
    {
        Assert.Throws<ArgumentNullException>(() => new NewerLineUserData(Mock.Of<IUserDataManager>(), null!));
    }
}
#endif

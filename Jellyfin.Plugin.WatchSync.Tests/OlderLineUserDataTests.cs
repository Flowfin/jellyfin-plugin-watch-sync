#if !NET10_0_OR_GREATER
using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the older supported line answers, which is the same question with none of the members the
/// newer line answers it with.
///
/// This file is compiled only into the target that line is built for. The facts here are about
/// the absence rather than about a feature: that line's server names no version that drives a
/// resume point and offers no batch read, so the item's own length is the answer and a page costs
/// one read per item. Both are written down as facts so that the cost and the answer are recorded
/// rather than assumed from the other line's file.
///
/// A fact here is not a fact about what a caller sees. Those are in
/// <see cref="UserDataGatewayTests"/> and run on both legs.
/// </summary>
public class OlderLineUserDataTests
{
    private static readonly long NinetyMinutes = TimeSpan.FromMinutes(90).Ticks;

    /// <summary>
    /// The item's own length is the answer, because this line has no version to name. It is the
    /// same answer the newer line gives for an item with no resume version, which is what makes
    /// the two lines one behaviour from the caller's side.
    /// </summary>
    [Fact]
    public void TheRuntimeIsTheItemsOwn()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), NinetyMinutes);

        var reading = new OlderLineUserData(Mock.Of<IUserDataManager>()).Read(user, work);

        Assert.Equal(NinetyMinutes, reading.ResumeRuntimeTicks);
    }

    /// <summary>
    /// A page costs one read per item on this line. The count is asserted rather than left to be
    /// inferred, because it is the number the sweep in #55 has to be written against and the
    /// difference between the two lines that a measurement on one of them would hide.
    /// </summary>
    [Fact]
    public void APageCostsOneReadPerItem()
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
            .Setup(manager => manager.GetUserData(user, It.IsAny<BaseItem>()))
            .Returns(UserDataFixtures.Record(true, 1, 0, null));

        var readings = new OlderLineUserData(server.Object).ReadMany(user, page);

        Assert.Equal(3, readings.Count);

        server.Verify(
            manager => manager.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()),
            Times.Exactly(3));
    }
}
#endif

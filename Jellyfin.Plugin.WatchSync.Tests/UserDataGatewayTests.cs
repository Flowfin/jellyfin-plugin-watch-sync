using System;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What a caller of the user data adapter sees, which is #20's first condition and #28's second.
///
/// Every fact here is written against <c>IUserDataGateway</c> and runs on both legs of the suite,
/// so each one is an assertion that the two supported server lines answer the same question the
/// same way. The two lines reach two different sets of members underneath, and the promise that a
/// caller cannot tell is kept here or nowhere: a difference that only shows on one line is a
/// difference nobody meets until somebody upgrades a server.
/// </summary>
public class UserDataGatewayTests
{
    private static readonly DateTime WhenTheyWatched = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The four moved fields come back as the server holds them.
    /// </summary>
    [Fact]
    public void AReadCarriesTheMovedSetTheServerHolds()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetUserData(user, work))
            .Returns(UserDataFixtures.Record(true, 3, 500, WhenTheyWatched));

        var reading = UserDataFixtures.ForTheLineThisBuildIsFor(server.Object).Read(user, work);

        var state = Assert.IsType<SyncedState>(reading.State);

        Assert.True(state.Played);
        Assert.Equal(3, state.PlayCount);
        Assert.Equal(500, state.PlaybackPositionTicks);
        Assert.Equal(WhenTheyWatched, state.LastPlayedDate);
    }

    /// <summary>
    /// A record the server does not hold is carried through as nothing rather than as a set of
    /// zeroes. Both read the same on a dashboard; only one of them is a value somebody chose,
    /// and #34 turns on the difference.
    /// </summary>
    [Fact]
    public void AReadOfAnItemTheServerHoldsNoRecordForCarriesNothing()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);

        var reading = UserDataFixtures
            .ForTheLineThisBuildIsFor(Mock.Of<IUserDataManager>())
            .Read(user, work);

        Assert.Null(reading.State);
    }

    /// <summary>
    /// The runtime a read answers with is the one #28 measures a peer's position against, and
    /// where this server names no other version it is the item's own on both lines.
    /// </summary>
    [Fact]
    public void AReadCarriesTheRuntimeThePositionRuleIsMeasuredAgainst()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);

        var reading = UserDataFixtures
            .ForTheLineThisBuildIsFor(Mock.Of<IUserDataManager>())
            .Read(user, work);

        Assert.Equal(TimeSpan.FromMinutes(90).Ticks, reading.ResumeRuntimeTicks);
    }

    /// <summary>
    /// An item the server has not analysed carries no runtime, on either line. The rule that
    /// reads this number drops a position rather than applying one on the strength of a number
    /// that is not there.
    /// </summary>
    [Fact]
    public void AnItemWithNoRuntimeAnswersWithNone()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), null);

        var reading = UserDataFixtures
            .ForTheLineThisBuildIsFor(Mock.Of<IUserDataManager>())
            .Read(user, work);

        Assert.Null(reading.ResumeRuntimeTicks);
    }

    /// <summary>
    /// A page is answered per item and keyed on the item, including the items this server holds
    /// no record for. A caller comparing two sides needs to be told that this side holds nothing
    /// rather than finding the item absent from the answer and having to guess which it was.
    /// </summary>
    [Fact]
    public void APageIsAnsweredForEveryItemInIt()
    {
        var user = UserDataFixtures.Someone();
        var held = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);
        var unheld = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(45).Ticks);

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetUserData(user, held))
            .Returns(UserDataFixtures.Record(true, 1, 0, WhenTheyWatched));
        UserDataFixtures.PagesAreReadItemByItem(server, user);

        var readings = UserDataFixtures
            .ForTheLineThisBuildIsFor(server.Object)
            .ReadMany(user, new[] { held, unheld });

        Assert.Equal(2, readings.Count);
        Assert.True(readings[held.Id].State?.Played);
        Assert.Null(readings[unheld.Id].State);
        Assert.Equal(TimeSpan.FromMinutes(45).Ticks, readings[unheld.Id].ResumeRuntimeTicks);
    }

    /// <summary>
    /// A write assigns every moved field and adds to none of them, which is the invariant #50
    /// refuses the other spelling of. The record the server is handed back is the one it gave
    /// out, so the six fields it holds that never leave a server are untouched.
    /// </summary>
    [Fact]
    public void AWriteAssignsEveryMovedFieldAndTouchesNoOther()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);

        var held = UserDataFixtures.Record(false, 1, 900, null);
        held.IsFavorite = true;

        var server = new Mock<IUserDataManager>();
        server.Setup(manager => manager.GetUserData(user, work)).Returns(held);

        UserDataFixtures
            .ForTheLineThisBuildIsFor(server.Object)
            .Write(
                user,
                work,
                new SyncedState(true, 4, 250, WhenTheyWatched),
                UserDataSaveReason.Import,
                CancellationToken.None);

        server.Verify(
            manager => manager.SaveUserData(
                user,
                work,
                It.Is<UserItemData>(record =>
                    record.Played
                    && record.PlayCount == 4
                    && record.PlaybackPositionTicks == 250
                    && record.LastPlayedDate == WhenTheyWatched
                    && record.IsFavorite),
                UserDataSaveReason.Import,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The reason a write carries is the caller's. The server takes it from whoever calls, on
    /// both lines, so choosing one here would take a decision that belongs to the apply path in
    /// #54 and would take it in the one place nobody would look for it.
    /// </summary>
    [Fact]
    public void AWriteCarriesTheReasonItsCallerChose()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);

        var server = new Mock<IUserDataManager>();
        server
            .Setup(manager => manager.GetUserData(user, work))
            .Returns(UserDataFixtures.Record(false, 0, 0, null));

        UserDataFixtures
            .ForTheLineThisBuildIsFor(server.Object)
            .Write(
                user,
                work,
                new SyncedState(true, 1, 0, WhenTheyWatched),
                UserDataSaveReason.UpdateUserData,
                CancellationToken.None);

        server.Verify(
            manager => manager.SaveUserData(
                user,
                work,
                It.IsAny<UserItemData>(),
                UserDataSaveReason.UpdateUserData,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// An item the person has never touched on this server is exactly the item a first exchange
    /// is about, so a write starts a record rather than refusing. The four moved fields are what
    /// this plugin puts in it; the key is the server's own to assign and is not asserted here
    /// because assigning one here would be overwritten by the save.
    /// </summary>
    [Fact]
    public void AWriteToAnItemWithNoRecordStartsOne()
    {
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), TimeSpan.FromMinutes(90).Ticks);

        var server = new Mock<IUserDataManager>();

        UserDataFixtures
            .ForTheLineThisBuildIsFor(server.Object)
            .Write(
                user,
                work,
                new SyncedState(true, 1, 0, WhenTheyWatched),
                UserDataSaveReason.Import,
                CancellationToken.None);

        server.Verify(
            manager => manager.SaveUserData(
                user,
                work,
                It.Is<UserItemData>(record =>
                    record.Played
                    && record.PlayCount == 1
                    && record.PlaybackPositionTicks == 0
                    && record.LastPlayedDate == WhenTheyWatched),
                UserDataSaveReason.Import,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Nothing reaches the server on a null argument. The adapter is the boundary, and a null
    /// that got past it would be a call into the server made about nobody.
    /// </summary>
    [Fact]
    public void NothingIsAskedOfTheServerAboutNobody()
    {
        var gateway = UserDataFixtures.ForTheLineThisBuildIsFor(Mock.Of<IUserDataManager>());
        var user = UserDataFixtures.Someone();
        var work = UserDataFixtures.Work(Guid.NewGuid(), 1);
        var state = new SyncedState(false, 0, 0, null);

        Assert.Throws<ArgumentNullException>(() => gateway.Read(null!, work));
        Assert.Throws<ArgumentNullException>(() => gateway.Read(user, null!));
        Assert.Throws<ArgumentNullException>(() => gateway.ReadMany(null!, new[] { work }));
        Assert.Throws<ArgumentNullException>(() => gateway.ReadMany(user, null!));
        Assert.Throws<ArgumentNullException>(() => gateway.Write(
            null!, work, state, UserDataSaveReason.Import, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => gateway.Write(
            user, null!, state, UserDataSaveReason.Import, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => gateway.Write(
            user, work, null!, UserDataSaveReason.Import, CancellationToken.None));
    }

    /// <summary>
    /// The adapter refuses to be built around nothing. A manager that is not there is a plugin
    /// that would fail at the first read rather than at the moment it was assembled.
    /// </summary>
    [Fact]
    public void TheAdapterRefusesToBeBuiltWithoutAManager()
    {
        Assert.Throws<ArgumentNullException>(() => UserDataFixtures.ForTheLineThisBuildIsFor(null!));
    }
}

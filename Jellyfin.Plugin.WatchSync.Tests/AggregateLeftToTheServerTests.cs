using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Tests.Apply;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The receiving half of #13: aggregate state is left to the server to derive.
///
/// A series and a season carry a played state and it is derived from the episodes under them
/// rather than stored, which is the evidence #13 opens with. So a receiver that wrote a parent
/// would either be overwritten by the server's own derivation or, worse, would mark every
/// episode the peer holds and this server does not. That is the mass marking the prior art keeps
/// producing, and it is the failure this file is about.
///
/// The sending half is refused one layer earlier and is not re-asserted here.
/// <see cref="TransferSubject"/> has no public constructor, and the reading that makes one
/// refuses every kind <c>docs/matching.md</c> calls an aggregate or a container, which
/// <see cref="TransferSubjectTests"/> drives over the server's whole kind enumeration. What is
/// left for this file is the walk, because nothing in that refusal says what the walk does with a
/// set it has legitimately been handed.
///
/// The episodes here carry the identifiers of the season and the series they belong to, and that
/// is what gives the assertion teeth. The parents are reachable from the items the walk holds, so
/// a walk that rolled a set of episodes up to its parent has everything it would need, and a
/// fixture of unrelated films would have made the same assertion pass for the wrong reason.
///
/// Nothing here reads a clock. The moment the walk runs is a parameter, which is the injected
/// clock invariant and the headless rule together.
/// </summary>
public class AggregateLeftToTheServerTests
{
    private static readonly Guid _pairing = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid _peerUser = new("88888888-8888-8888-8888-888888888888");
    private static readonly Guid _series = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid _season = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid _firstEpisode = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid _secondEpisode = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid _thirdEpisode = new("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset _evening = new(2026, 8, 27, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _watchedAt = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The third condition of #13. Three episodes of one season are applied, and the only
    /// identifiers written are the three episodes.
    ///
    /// The assertion is over the whole list of writes rather than over the absence of two
    /// identifiers, because a walk that wrote some fourth thing would pass the narrower one.
    /// </summary>
    [Fact]
    public void ApplyingASetOfEpisodeChangesWritesTheEpisodesAndNothingElse()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        var answer = ItemByItemApply.Apply(
            user,
            Episodes(user, _firstEpisode, _secondEpisode, _thirdEpisode),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(
            new[] { _firstEpisode, _secondEpisode, _thirdEpisode },
            server.Writes.Select(write => write.ItemId).ToArray());

        Assert.Equal(
            new[] { _firstEpisode, _secondEpisode, _thirdEpisode },
            answer.Applied.Select(subject => subject.ItemId).ToArray());
    }

    /// <summary>
    /// The same walk, read at the parent rather than at the episodes. What the server holds for
    /// the season and for the series is exactly what it held before, so the derivation is left to
    /// it rather than pre-empted.
    ///
    /// Both parents are given a state to hold first, and the two are different from each other and
    /// from what the walk writes. A parent holding nothing before the walk would leave this fact
    /// unable to tell a walk that wrote the value it happened to have from one that wrote nothing.
    /// </summary>
    [Fact]
    public void WhatTheServerHoldsForTheSeasonAndTheSeriesIsUntouched()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();
        var seasonHeld = new SyncedState(false, 1, TimeSpan.FromMinutes(4).Ticks, null);
        var seriesHeld = new SyncedState(false, 0, TimeSpan.FromMinutes(9).Ticks, null);

        server.Hold(_season, seasonHeld);
        server.Hold(_series, seriesHeld);

        ItemByItemApply.Apply(
            user,
            Episodes(user, _firstEpisode, _secondEpisode, _thirdEpisode),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Same(seasonHeld, server.HeldFor(_season));
        Assert.Same(seriesHeld, server.HeldFor(_series));
    }

    /// <summary>
    /// The parent is still left alone when the walk is not going cleanly, which is the case a
    /// roll-up would most plausibly be reached for. The middle episode is refused, so the walk
    /// steps over it and finishes the set, and neither the season nor the series is written on the
    /// way through or at the end.
    /// </summary>
    [Fact]
    public void ARefusedEpisodeDoesNotSendTheWalkToTheParent()
    {
        var user = UserDataFixtures.Someone();
        var server = new RecordedWrites();

        server.Refuse(_secondEpisode, new InvalidOperationException("the library no longer holds it"));

        var answer = ItemByItemApply.Apply(
            user,
            Episodes(user, _firstEpisode, _secondEpisode, _thirdEpisode),
            server,
            AgreedRecords.NoneYet(_pairing, user.Id),
            ProvenanceRecords.NoneYet(_pairing, user.Id),
            _peerUser,
            1,
            FailureShare.DefaultMaximumShare,
            _evening,
            CancellationToken.None);

        Assert.Equal(
            new[] { _firstEpisode, _secondEpisode, _thirdEpisode },
            server.Writes.Select(write => write.ItemId).ToArray());

        Assert.Single(answer.Failed);
        Assert.Null(server.HeldFor(_season));
        Assert.Null(server.HeldFor(_series));
    }

    private static Episode Leaf(Guid id) =>
        new Episode
        {
            Id = id,
            RunTimeTicks = TimeSpan.FromMinutes(42).Ticks,
            SeasonId = _season,
            SeriesId = _series,
        };

    private static TransferSubject Subject(User user, Guid itemId)
    {
        var reading = TransferSubject.From(user.Id, itemId, BaseItemKind.Episode);

        Assert.True(reading.IsSubject);

        return reading.Value!;
    }

    private static IReadOnlyList<ItemToApply> Episodes(User user, params Guid[] itemIds) =>
        itemIds
            .Select(itemId => new ItemToApply(
                Subject(user, itemId),
                Leaf(itemId),
                new SyncedState(true, 1, 0, _watchedAt)))
            .ToList();
}

using System;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchSync.Configuration;

/// <summary>
/// The settings an operator of this server chooses, and nothing else.
///
/// What may sit here is decided in <c>docs/configuration.md</c> and is one category rather than
/// a judgement made per setting: a setting describing what this server does, independent of any
/// peer and of any person. The reason is that this file is one an operator copies between
/// servers, because it is the fastest way to stand a second server up behaving like the first.
/// A per-pairing setting arriving that way points at a peer that is not there; a per-user
/// setting arriving that way applies one person's decision about their own history to somebody
/// else. So the tolerated skew and the run caps are not here, and neither is the opt-out.
///
/// Every member is a whole number of a unit named in the member's own name, and none of them is
/// a <see cref="TimeSpan"/>. The reason is the control rather than the serializer: what an
/// operator meets on the page is a number box, so a span would be formatted into one and parsed
/// back out of it, and those two conversions are where a value stops being the one that was
/// typed. As counts, the number on the page, the number in this document and the number the rule
/// is handed are one number, and the only conversion left is the one <see cref="ServerWideSettings"/>
/// makes in one place. What the server's serializer does with a span was measured rather than
/// supposed - it round-trips one - and <c>PluginConfigurationTests</c> carries that run so the
/// question is not re-opened on a guess.
///
/// Nothing here is validated on the way in. What a value has to satisfy is declared on the rule
/// that consumes it, and <see cref="ServerWideSettings"/> is where the two meet and where a
/// value outside a bound is refused rather than repaired. That split is deliberate: a setter
/// that quietly clamps is the shape #61 exists against, and a setter that throws makes a server
/// unable to read its own configuration document.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets how far a position has to move, in seconds, before the move is a change
    /// worth carrying to a peer.
    ///
    /// It defaults to <see cref="PositionThresholds.DefaultMove"/> and is bounded by
    /// <see cref="PositionThresholds.MaximumMove"/>, and both of those are read from that type
    /// rather than written here, so the page and the rule cannot come to disagree about what
    /// this plugin does when nobody has chosen anything.
    /// </summary>
    public int PositionMoveSeconds { get; set; } =
        (int)PositionThresholds.DefaultMove.TotalSeconds;

    /// <summary>
    /// Gets or sets how close to the end of an item, in seconds, a position has to be before it
    /// is a finish rather than a place to resume from.
    ///
    /// Defaults to <see cref="PositionThresholds.DefaultFinish"/>, bounded by
    /// <see cref="PositionThresholds.MaximumFinish"/>.
    /// </summary>
    public int PositionFinishSeconds { get; set; } =
        (int)PositionThresholds.DefaultFinish.TotalSeconds;

    /// <summary>
    /// Gets or sets the length, in seconds, below which no position is carried for an item at
    /// all.
    ///
    /// Defaults to <see cref="PositionThresholds.DefaultShortestItem"/>, bounded by
    /// <see cref="PositionThresholds.MaximumShortestItem"/>.
    /// </summary>
    public int PositionShortestItemSeconds { get; set; } =
        (int)PositionThresholds.DefaultShortestItem.TotalSeconds;

    /// <summary>
    /// Gets or sets how long, in seconds, a write this plugin made is treated as the cause of an
    /// event the server raised about the same field.
    ///
    /// Defaults to <see cref="EchoWindow.DefaultWindow"/>, bounded by
    /// <see cref="EchoWindow.MaximumWindow"/>. The bound is the one worth knowing before this is
    /// raised: past it the window stops covering a server normalising this plugin's own write
    /// and starts covering a person acting, and what it swallows then is the deliberate unmark
    /// #34 exists to carry.
    /// </summary>
    public int EchoWindowSeconds { get; set; } =
        (int)EchoWindow.DefaultWindow.TotalSeconds;

    /// <summary>
    /// Gets or sets how long, in days, a recorded conflict is kept.
    ///
    /// Defaults to <see cref="ConflictRecords.DefaultRetention"/>, bounded by
    /// <see cref="ConflictRecords.MaximumRetention"/>.
    /// </summary>
    public int ConflictRetentionDays { get; set; } =
        (int)ConflictRecords.DefaultRetention.TotalDays;

    /// <summary>
    /// Gets or sets how long, in days, the provenance of a value this plugin wrote is kept.
    ///
    /// Defaults to <see cref="ProvenanceRecords.DefaultRetention"/>, bounded by
    /// <see cref="ProvenanceRecords.MaximumRetention"/>. It is the record a revoked pairing is
    /// undone from, so shortening it shortens how far back an undo can reach.
    /// </summary>
    public int ProvenanceRetentionDays { get; set; } =
        (int)ProvenanceRecords.DefaultRetention.TotalDays;

    /// <summary>
    /// Gets or sets the greatest share, in per cent, of the items one walk attempted that may
    /// fail before the walk stops.
    ///
    /// Defaults to <see cref="FailureShare.DefaultMaximumShare"/> and is bounded at both ends, by
    /// <see cref="FailureShare.SmallestConfigurableShare"/> and
    /// <see cref="FailureShare.LargestConfigurableShare"/>. It is the one setting here whose
    /// danger is at the low end: a share near zero stops a walk at the first refused item and
    /// every exchange after it, which is the all-or-nothing outcome #54 exists to refuse arrived
    /// at through the rule that bounds it.
    ///
    /// It is per cent rather than a fraction because what an operator meets is a number box, and
    /// a box in which 0.5 and 5 differ by a keystroke and by a factor of ten is one where the
    /// keystroke costs an exchange.
    /// </summary>
    public int MaximumFailureSharePercent { get; set; } =
        (int)Math.Round(FailureShare.DefaultMaximumShare * 100);
}

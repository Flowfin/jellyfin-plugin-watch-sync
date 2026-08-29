using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;

namespace Jellyfin.Plugin.WatchSync.Configuration;

/// <summary>
/// The one place a stored configuration document becomes the values the rules take.
///
/// It exists because the two ends are written in different things and neither may be bent to
/// the other. The document is whole numbers of a named unit, because that is what the server's
/// serializer can carry, and the rules take spans and a <see cref="PositionThresholds"/>,
/// because that is what makes a threshold comparable to a position. A caller converting for
/// itself is a caller that has decided, in passing, what an out-of-range value means, and the
/// six places that would do so would decide it six ways.
///
/// It refuses rather than repairs. A value above its bound is not clamped and not replaced by
/// the default, because both of those leave a server running a rule the operator did not choose
/// while the page goes on showing the number they typed. What a refusal costs is stated rather
/// than left to be met: a caller holding a refused reading has no thresholds at all and cannot
/// sync, which is the fail-closed direction and is the one this plan takes everywhere else.
/// Turning that into something an operator sees is the page in #62, and the refusal is data on
/// the reading so that it can be.
/// </summary>
public static class ServerWideSettings
{
    /// <summary>
    /// Reads the settings out of a stored configuration document.
    /// </summary>
    /// <param name="configuration">The document the server read for this plugin.</param>
    /// <returns>
    /// The values every rule accepted, or the refusals, never a mixture of the two.
    /// </returns>
    public static ServerWideSettingsReading Read(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var refusals = new List<SettingRefusal>();

        var move = Seconds(
            refusals,
            nameof(configuration.PositionMoveSeconds),
            configuration.PositionMoveSeconds,
            PositionThresholds.MaximumMove);
        var finish = Seconds(
            refusals,
            nameof(configuration.PositionFinishSeconds),
            configuration.PositionFinishSeconds,
            PositionThresholds.MaximumFinish);
        var shortestItem = Seconds(
            refusals,
            nameof(configuration.PositionShortestItemSeconds),
            configuration.PositionShortestItemSeconds,
            PositionThresholds.MaximumShortestItem);
        var echoWindow = Seconds(
            refusals,
            nameof(configuration.EchoWindowSeconds),
            configuration.EchoWindowSeconds,
            EchoWindow.MaximumWindow);
        var conflictRetention = Days(
            refusals,
            nameof(configuration.ConflictRetentionDays),
            configuration.ConflictRetentionDays,
            ConflictRecords.MaximumRetention);
        var provenanceRetention = Days(
            refusals,
            nameof(configuration.ProvenanceRetentionDays),
            configuration.ProvenanceRetentionDays,
            ProvenanceRecords.MaximumRetention);

        // The relation between two of the three thresholds, which neither of them can be judged
        // against on its own. A finish distance at or above the shortest item length makes every
        // position on the shortest item this plugin carries a finish, so the rule silently stops
        // being two rules. PositionThresholds refuses the same pair in its constructor, and this
        // is that refusal asked before the constructor is reached, because a throw out of here
        // would be a server unable to read its own configuration rather than an operator told
        // which two numbers disagree.
        if (finish is not null && shortestItem is not null && finish >= shortestItem)
        {
            refusals.Add(new SettingRefusal(
                nameof(configuration.PositionFinishSeconds),
                configuration.PositionFinishSeconds,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"below {nameof(configuration.PositionShortestItemSeconds)}, which is {configuration.PositionShortestItemSeconds}")));
        }

        if (refusals.Count > 0)
        {
            return ServerWideSettingsReading.Refused(refusals);
        }

        return ServerWideSettingsReading.Accepted(
            new PositionThresholds(move!.Value, finish!.Value, shortestItem!.Value),
            echoWindow!.Value,
            conflictRetention!.Value,
            provenanceRetention!.Value);
    }

    /// <summary>
    /// A setting stored as whole seconds, judged against the bound the rule declares.
    ///
    /// Zero is refused along with everything below it, and that is a decision rather than
    /// arithmetic. Every one of these settings is a distance or a window, and zero switches the
    /// rule off through the setting: a zero move threshold carries every progress report the
    /// player sends, a zero window suppresses no echo, and a zero retention keeps nothing. Each
    /// of those is a thing somebody may legitimately want and none of them is what a person
    /// typing a smaller number was asking for, so the way to switch a rule off is a decision of
    /// its own rather than a boundary value of a number that means something else.
    /// </summary>
    /// <param name="refusals">Where a refusal is collected.</param>
    /// <param name="setting">The member being read.</param>
    /// <param name="found">The value the document carried.</param>
    /// <param name="maximum">The widest value the rule accepts.</param>
    /// <returns>The span, or null where it was refused.</returns>
    private static TimeSpan? Seconds(
        List<SettingRefusal> refusals,
        string setting,
        int found,
        TimeSpan maximum) =>
        Bounded(refusals, setting, found, (int)maximum.TotalSeconds, "seconds")
            is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>
    /// A setting stored as whole days, judged against the bound the rule declares.
    /// </summary>
    /// <param name="refusals">Where a refusal is collected.</param>
    /// <param name="setting">The member being read.</param>
    /// <param name="found">The value the document carried.</param>
    /// <param name="maximum">The longest value the rule accepts.</param>
    /// <returns>The span, or null where it was refused.</returns>
    private static TimeSpan? Days(
        List<SettingRefusal> refusals,
        string setting,
        int found,
        TimeSpan maximum) =>
        Bounded(refusals, setting, found, (int)maximum.TotalDays, "days")
            is { } days
            ? TimeSpan.FromDays(days)
            : null;

    /// <summary>
    /// The bound both units share, in one place so that the two cannot come to disagree about
    /// which end of the range is refused.
    /// </summary>
    /// <param name="refusals">Where a refusal is collected.</param>
    /// <param name="setting">The member being read.</param>
    /// <param name="found">The value the document carried.</param>
    /// <param name="maximum">The largest accepted value, in the same unit.</param>
    /// <param name="unit">The unit, for the sentence an operator reads.</param>
    /// <returns>The value, or null where it was refused.</returns>
    private static int? Bounded(
        List<SettingRefusal> refusals,
        string setting,
        int found,
        int maximum,
        string unit)
    {
        if (found > 0 && found <= maximum)
        {
            return found;
        }

        refusals.Add(new SettingRefusal(
            setting,
            found,
            string.Create(CultureInfo.InvariantCulture, $"1 to {maximum} {unit}")));

        return null;
    }
}

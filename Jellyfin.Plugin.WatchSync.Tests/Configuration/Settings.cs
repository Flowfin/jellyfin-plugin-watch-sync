using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Transfer;

namespace Jellyfin.Plugin.WatchSync.Tests.Configuration;

/// <summary>
/// The settings the configuration document carries, as something a fact can walk.
///
/// A test cannot reach a property by reflection and still say anything about the unit it is in
/// or the rule that bounds it, so this file names both. That makes it a second list beside the
/// configuration type, which is the drift every closure in this repository exists against, and
/// it is held to that type rather than trusted: <c>ConfigurationSettingsTests</c> compares this
/// set against the members the type declares in both directions, so a setting added to one and
/// not the other is a red suite.
///
/// The bound is read off the rule that declares it rather than written here, for the same
/// reason the configuration type reads its defaults off the same declarations: a number typed
/// into a test agrees with the source on the day it is typed and never again.
/// </summary>
internal static class Settings
{
    /// <summary>
    /// Gets every setting the configuration document carries.
    /// </summary>
    internal static IReadOnlyList<Setting> All { get; } = new[]
    {
        new Setting(
            nameof(PluginConfiguration.PositionMoveSeconds),
            Unit.Seconds,
            PositionThresholds.DefaultMove,
            PositionThresholds.MaximumMove,
            (document, value) => document.PositionMoveSeconds = value,
            reading => (int)reading.Positions!.Move.TotalSeconds),
        new Setting(
            nameof(PluginConfiguration.PositionFinishSeconds),
            Unit.Seconds,
            PositionThresholds.DefaultFinish,
            PositionThresholds.MaximumFinish,
            (document, value) => document.PositionFinishSeconds = value,
            reading => (int)reading.Positions!.Finish.TotalSeconds),
        new Setting(
            nameof(PluginConfiguration.PositionShortestItemSeconds),
            Unit.Seconds,
            PositionThresholds.DefaultShortestItem,
            PositionThresholds.MaximumShortestItem,
            (document, value) => document.PositionShortestItemSeconds = value,
            reading => (int)reading.Positions!.ShortestItem.TotalSeconds),
        new Setting(
            nameof(PluginConfiguration.EchoWindowSeconds),
            Unit.Seconds,
            EchoWindow.DefaultWindow,
            EchoWindow.MaximumWindow,
            (document, value) => document.EchoWindowSeconds = value,
            reading => (int)reading.EchoWindow!.Value.TotalSeconds),
        new Setting(
            nameof(PluginConfiguration.ConflictRetentionDays),
            Unit.Days,
            ConflictRecords.DefaultRetention,
            ConflictRecords.MaximumRetention,
            (document, value) => document.ConflictRetentionDays = value,
            reading => (int)reading.ConflictRetention!.Value.TotalDays),
        new Setting(
            nameof(PluginConfiguration.ProvenanceRetentionDays),
            Unit.Days,
            ProvenanceRecords.DefaultRetention,
            ProvenanceRecords.MaximumRetention,
            (document, value) => document.ProvenanceRetentionDays = value,
            reading => (int)reading.ProvenanceRetention!.Value.TotalDays),
        new Setting(
            nameof(PluginConfiguration.MaximumFailureSharePercent),
            Unit.PerCent,
            FailureShare.DefaultMaximumShare,
            FailureShare.LargestConfigurableShare,
            (document, value) => document.MaximumFailureSharePercent = value,
            reading => (int)Math.Round(reading.MaximumFailureShare!.Value * 100),
            FailureShare.SmallestConfigurableShare),
        new Setting(
            nameof(PluginConfiguration.SweepIntervalMinutes),
            Unit.Minutes,
            SweepSchedule.DefaultInterval,
            SweepSchedule.LongestInterval,
            (document, value) => document.SweepIntervalMinutes = value,
            reading => (int)reading.SweepInterval!.Value.TotalMinutes),
    };

    /// <summary>
    /// The unit a setting is stored in.
    /// </summary>
    internal enum Unit
    {
        /// <summary>
        /// Whole seconds.
        /// </summary>
        Seconds,

        /// <summary>
        /// Whole minutes.
        /// </summary>
        Minutes,

        /// <summary>
        /// Whole days.
        /// </summary>
        Days,

        /// <summary>
        /// Whole per cent of a fraction the source declares between zero and one.
        /// </summary>
        PerCent,
    }

    /// <summary>
    /// A document every setting of which is inside its own bound, and which no relation between
    /// two settings refuses.
    ///
    /// The shortest item length is at its maximum rather than at its default, and that is the
    /// whole reason this is a method rather than a fresh instance at each site. A fact walking
    /// the settings one at a time raises the finish distance to its own bound of fifteen
    /// minutes, and a default shortest item of five minutes then refuses the pair for a reason
    /// the fact is not about.
    /// </summary>
    /// <returns>The document.</returns>
    internal static PluginConfiguration Document() =>
        new()
        {
            PositionShortestItemSeconds =
                (int)PositionThresholds.MaximumShortestItem.TotalSeconds,
        };

    /// <summary>
    /// One setting: its name on the configuration document, the unit it is stored in, the
    /// default it carries and the rule's bound, and the way to write a value into a document.
    /// </summary>
    internal sealed class Setting
    {
        private readonly Action<PluginConfiguration, int> _set;
        private readonly Func<ServerWideSettingsReading, int> _read;

        /// <summary>
        /// Initializes a new instance of the <see cref="Setting"/> class.
        /// </summary>
        /// <param name="name">The member's name on the configuration document.</param>
        /// <param name="unit">The unit the member is stored in.</param>
        /// <param name="declaredDefault">The default the consuming rule declares.</param>
        /// <param name="declaredBound">The widest value the consuming rule accepts.</param>
        /// <param name="set">Writes a value into a document.</param>
        /// <param name="read">
        /// Takes this setting's value back out of an accepted reading, in the unit the document
        /// stores it in. It is the other half of <paramref name="set"/> and it is what lets a
        /// fact compare the number that went in against the number that came out without knowing
        /// which member of the reading holds it.
        /// </param>
        /// <param name="declaredFloor">
        /// The smallest value the consuming rule accepts, where the rule declares one, and null
        /// where the floor is one of the setting's own unit. Only the failure share has one, and
        /// it has one because its dangerous end is the low one.
        /// </param>
        internal Setting(
            string name,
            Unit unit,
            object declaredDefault,
            object declaredBound,
            Action<PluginConfiguration, int> set,
            Func<ServerWideSettingsReading, int> read,
            object? declaredFloor = null)
        {
            Name = name;
            InUnit = unit;
            DeclaredDefault = declaredDefault;
            DeclaredBound = declaredBound;
            DeclaredFloor = declaredFloor;
            _set = set;
            _read = read;
        }

        /// <summary>
        /// Gets the member's name on the configuration document.
        /// </summary>
        internal string Name { get; }

        /// <summary>
        /// Gets the unit the member is stored in.
        /// </summary>
        internal Unit InUnit { get; }

        /// <summary>
        /// Gets the default the consuming rule declares, in whatever the rule declares it as.
        /// </summary>
        internal object DeclaredDefault { get; }

        /// <summary>
        /// Gets the widest value the consuming rule accepts, as the rule declares it.
        /// </summary>
        internal object DeclaredBound { get; }

        /// <summary>
        /// Gets the smallest value the consuming rule declares, or null where the floor is one of
        /// the setting's own unit rather than a number a rule declares.
        /// </summary>
        internal object? DeclaredFloor { get; }

        /// <summary>
        /// Gets the default in the unit the document stores it in.
        /// </summary>
        internal int Default => Count(DeclaredDefault);

        /// <summary>
        /// Gets the bound in the unit the document stores it in.
        /// </summary>
        internal int Maximum => Count(DeclaredBound);

        /// <summary>
        /// Gets the floor in the unit the document stores it in.
        /// </summary>
        internal int Minimum => DeclaredFloor is null ? 1 : Count(DeclaredFloor);

        /// <summary>
        /// Writes a value into a document.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="value">The value, in this setting's unit.</param>
        internal void Set(PluginConfiguration document, int value) => _set(document, value);

        /// <summary>
        /// Takes this setting's value out of an accepted reading.
        /// </summary>
        /// <param name="reading">A reading that was accepted.</param>
        /// <returns>The value, in this setting's unit.</returns>
        internal int Read(ServerWideSettingsReading reading) => _read(reading);

        private int Count(object declared) => (InUnit, declared) switch
        {
            (Unit.Seconds, TimeSpan span) => (int)span.TotalSeconds,
            (Unit.Minutes, TimeSpan span) => (int)span.TotalMinutes,
            (Unit.Days, TimeSpan span) => (int)span.TotalDays,
            (Unit.PerCent, double fraction) => (int)Math.Round(fraction * 100),
            _ => throw new InvalidOperationException(
                $"{declared.GetType().Name} is not how a rule declares a value this document stores in {InUnit}."),
        };
    }
}

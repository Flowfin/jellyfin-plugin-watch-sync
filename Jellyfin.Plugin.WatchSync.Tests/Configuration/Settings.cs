using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;

namespace Jellyfin.Plugin.WatchSync.Tests.Configuration;

/// <summary>
/// The six settings the configuration document carries, as something a fact can walk.
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
            (document, value) => document.PositionMoveSeconds = value),
        new Setting(
            nameof(PluginConfiguration.PositionFinishSeconds),
            Unit.Seconds,
            PositionThresholds.DefaultFinish,
            PositionThresholds.MaximumFinish,
            (document, value) => document.PositionFinishSeconds = value),
        new Setting(
            nameof(PluginConfiguration.PositionShortestItemSeconds),
            Unit.Seconds,
            PositionThresholds.DefaultShortestItem,
            PositionThresholds.MaximumShortestItem,
            (document, value) => document.PositionShortestItemSeconds = value),
        new Setting(
            nameof(PluginConfiguration.EchoWindowSeconds),
            Unit.Seconds,
            EchoWindow.DefaultWindow,
            EchoWindow.MaximumWindow,
            (document, value) => document.EchoWindowSeconds = value),
        new Setting(
            nameof(PluginConfiguration.ConflictRetentionDays),
            Unit.Days,
            ConflictRecords.DefaultRetention,
            ConflictRecords.MaximumRetention,
            (document, value) => document.ConflictRetentionDays = value),
        new Setting(
            nameof(PluginConfiguration.ProvenanceRetentionDays),
            Unit.Days,
            ProvenanceRecords.DefaultRetention,
            ProvenanceRecords.MaximumRetention,
            (document, value) => document.ProvenanceRetentionDays = value),
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
        /// Whole days.
        /// </summary>
        Days,
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

        /// <summary>
        /// Initializes a new instance of the <see cref="Setting"/> class.
        /// </summary>
        /// <param name="name">The member's name on the configuration document.</param>
        /// <param name="unit">The unit the member is stored in.</param>
        /// <param name="declaredDefault">The default the consuming rule declares.</param>
        /// <param name="declaredBound">The widest value the consuming rule accepts.</param>
        /// <param name="set">Writes a value into a document.</param>
        internal Setting(
            string name,
            Unit unit,
            TimeSpan declaredDefault,
            TimeSpan declaredBound,
            Action<PluginConfiguration, int> set)
        {
            Name = name;
            InUnit = unit;
            DeclaredDefault = declaredDefault;
            DeclaredBound = declaredBound;
            _set = set;
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
        /// Gets the default the consuming rule declares, as a span.
        /// </summary>
        internal TimeSpan DeclaredDefault { get; }

        /// <summary>
        /// Gets the widest value the consuming rule accepts, as a span.
        /// </summary>
        internal TimeSpan DeclaredBound { get; }

        /// <summary>
        /// Gets the default in the unit the document stores it in.
        /// </summary>
        internal int Default => Count(DeclaredDefault);

        /// <summary>
        /// Gets the bound in the unit the document stores it in.
        /// </summary>
        internal int Maximum => Count(DeclaredBound);

        /// <summary>
        /// Writes a value into a document.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="value">The value, in this setting's unit.</param>
        internal void Set(PluginConfiguration document, int value) => _set(document, value);

        private int Count(TimeSpan span) => InUnit switch
        {
            Unit.Seconds => (int)span.TotalSeconds,
            Unit.Days => (int)span.TotalDays,
            _ => throw new InvalidOperationException($"{InUnit} is not a unit this document stores."),
        };
    }
}

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Configuration;

/// <summary>
/// What one reading of the configuration document came back with.
///
/// A refused reading holds no values. There is no member on it carrying the thresholds that were
/// read before the bad setting was met, so a caller that meant to run with the operator's
/// choices has nothing to run with, and the refusal is a property of the type rather than a
/// discipline somebody keeps. It is the shape <c>EnvelopeReading</c> and <c>DocumentReading</c>
/// already take, for the same reason.
///
/// The other half of that shape is deliberately not copied. Those two name one bad member and
/// stop, because what is on the other end is a peer and the exchange is over either way. Here
/// the other end is a person with a form open, so every refused setting is named at once: a
/// reading that stops at the first sends them round the loop once per mistake, and the loop
/// includes a server restart on the line where this document is read at startup.
/// </summary>
public sealed class ServerWideSettingsReading
{
    private ServerWideSettingsReading(
        PositionThresholds? positions,
        TimeSpan? echoWindow,
        TimeSpan? conflictRetention,
        TimeSpan? provenanceRetention,
        double? maximumFailureShare,
        TimeSpan? sweepInterval,
        IReadOnlyList<SettingRefusal> refusals)
    {
        Positions = positions;
        EchoWindow = echoWindow;
        ConflictRetention = conflictRetention;
        ProvenanceRetention = provenanceRetention;
        MaximumFailureShare = maximumFailureShare;
        SweepInterval = sweepInterval;
        Refusals = refusals;
    }

    /// <summary>
    /// Gets a value indicating whether every setting was accepted.
    /// </summary>
    public bool IsRead => Refusals.Count == 0;

    /// <summary>
    /// Gets the three position thresholds the operator chose, or null where anything was
    /// refused.
    /// </summary>
    public PositionThresholds? Positions { get; }

    /// <summary>
    /// Gets how long a write of this plugin's own suppresses the event it caused, or null where
    /// anything was refused.
    /// </summary>
    public TimeSpan? EchoWindow { get; }

    /// <summary>
    /// Gets how long a recorded conflict is kept, or null where anything was refused.
    /// </summary>
    public TimeSpan? ConflictRetention { get; }

    /// <summary>
    /// Gets how long the provenance of a written value is kept, or null where anything was
    /// refused.
    /// </summary>
    public TimeSpan? ProvenanceRetention { get; }

    /// <summary>
    /// Gets the greatest share of one walk's attempted items that may fail before it stops, or
    /// null where anything was refused.
    /// </summary>
    public double? MaximumFailureShare { get; }

    /// <summary>
    /// Gets how often the scheduled sweep runs, or null where anything was refused.
    /// </summary>
    public TimeSpan? SweepInterval { get; }

    /// <summary>
    /// Gets every setting that was refused, in the order the members are declared, or an empty
    /// list where none was.
    /// </summary>
    public IReadOnlyList<SettingRefusal> Refusals { get; }

    /// <summary>
    /// The reading of a document every rule accepted.
    /// </summary>
    /// <param name="positions">The three position thresholds.</param>
    /// <param name="echoWindow">The echo suppression window.</param>
    /// <param name="conflictRetention">How long a conflict is kept.</param>
    /// <param name="provenanceRetention">How long a provenance entry is kept.</param>
    /// <param name="maximumFailureShare">The share of a walk that may fail before it stops.</param>
    /// <param name="sweepInterval">How often the scheduled sweep runs.</param>
    /// <returns>The reading.</returns>
    public static ServerWideSettingsReading Accepted(
        PositionThresholds positions,
        TimeSpan echoWindow,
        TimeSpan conflictRetention,
        TimeSpan provenanceRetention,
        double maximumFailureShare,
        TimeSpan sweepInterval)
    {
        ArgumentNullException.ThrowIfNull(positions);

        return new ServerWideSettingsReading(
            positions,
            echoWindow,
            conflictRetention,
            provenanceRetention,
            maximumFailureShare,
            sweepInterval,
            Array.Empty<SettingRefusal>());
    }

    /// <summary>
    /// The reading of a document at least one rule refused.
    /// </summary>
    /// <param name="refusals">Every setting that was refused, which may not be empty.</param>
    /// <returns>The reading.</returns>
    public static ServerWideSettingsReading Refused(IReadOnlyList<SettingRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        if (refusals.Count == 0)
        {
            throw new ArgumentException(
                "A refused reading names at least one refused setting, otherwise a caller reading Refusals learns that something was wrong and never what.",
                nameof(refusals));
        }

        return new ServerWideSettingsReading(null, null, null, null, null, null, refusals);
    }
}

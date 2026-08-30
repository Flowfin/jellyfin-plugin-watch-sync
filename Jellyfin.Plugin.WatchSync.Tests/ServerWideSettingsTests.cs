using System;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Apply;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Tests.Configuration;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the one place a stored configuration document becomes the values the rules take.
///
/// What is being held here is that a value an operator typed either reaches the rule unchanged
/// or does not reach it at all. The failure between those two is the one this set exists
/// against: a reader that clamps or falls back to the default leaves the server running a rule
/// nobody chose while the page goes on showing what was typed, and no run says so.
/// </summary>
public class ServerWideSettingsTests
{
    /// <summary>
    /// A document nobody has touched reads as the defaults the rules declare.
    ///
    /// The comparison is against the declaring types rather than against numbers written here,
    /// because a number written here is a third copy of a value the document and the source
    /// already both carry, and it would agree with neither after the next change.
    /// </summary>
    [Fact]
    public void AnUntouchedDocumentReadsAsTheDefaultsTheRulesDeclare()
    {
        var reading = ServerWideSettings.Read(new PluginConfiguration());

        Assert.True(reading.IsRead);
        Assert.Empty(reading.Refusals);
        Assert.Equal(PositionThresholds.DefaultMove, reading.Positions!.Move);
        Assert.Equal(PositionThresholds.DefaultFinish, reading.Positions!.Finish);
        Assert.Equal(PositionThresholds.DefaultShortestItem, reading.Positions!.ShortestItem);
        Assert.Equal(EchoWindow.DefaultWindow, reading.EchoWindow);
        Assert.Equal(ConflictRecords.DefaultRetention, reading.ConflictRetention);
        Assert.Equal(ProvenanceRecords.DefaultRetention, reading.ProvenanceRetention);
        Assert.Equal(FailureShare.DefaultMaximumShare, reading.MaximumFailureShare);
    }

    /// <summary>
    /// A value an operator chose reaches the rule as the value they chose.
    ///
    /// Without this the reader could return the defaults whatever the document said, and every
    /// other fact here would still pass: the refusals would refuse and the accepted reading
    /// would be accepted, and the settings would do nothing.
    /// </summary>
    [Fact]
    public void AChosenValueReachesTheRuleUnchanged()
    {
        var reading = ServerWideSettings.Read(new PluginConfiguration
        {
            PositionMoveSeconds = 90,
            PositionFinishSeconds = 45,
            PositionShortestItemSeconds = 600,
            EchoWindowSeconds = 12,
            ConflictRetentionDays = 3,
            ProvenanceRetentionDays = 200,
            MaximumFailureSharePercent = 37,
        });

        Assert.True(reading.IsRead);
        Assert.Equal(TimeSpan.FromSeconds(90), reading.Positions!.Move);
        Assert.Equal(TimeSpan.FromSeconds(45), reading.Positions!.Finish);
        Assert.Equal(TimeSpan.FromSeconds(600), reading.Positions!.ShortestItem);
        Assert.Equal(TimeSpan.FromSeconds(12), reading.EchoWindow);
        Assert.Equal(TimeSpan.FromDays(3), reading.ConflictRetention);
        Assert.Equal(TimeSpan.FromDays(200), reading.ProvenanceRetention);
        Assert.Equal(0.37, reading.MaximumFailureShare);
    }

    /// <summary>
    /// Across its whole range, a setting is either refused or reaches the rule as the number
    /// that was stored, and never as a third thing.
    ///
    /// This is the property the fact above states at one point and #61 asks for everywhere: no
    /// path modifies a setting value without telling the operator. The two states a reader may
    /// produce are a refusal, which the operator is told about, and the value they chose. What
    /// is refused here is the third state - a value quietly clamped to a bound, rounded to
    /// something the rule prefers, or replaced by the default - because that leaves a server
    /// running a rule nobody chose while the page goes on showing what was typed.
    ///
    /// The assertion is conditional on the reading being accepted rather than requiring every
    /// value to be, because a document is judged whole: five settings inside their own bounds
    /// and one relation between two of them refused is a refused reading, and which values those
    /// are is not this fact's business. The vacuous version of that is refused in the same pass
    /// by requiring at least one value of each setting to be accepted, so a reader that refused
    /// everything cannot satisfy this by never producing a number to compare.
    ///
    /// Five values per setting, at both ends and inside, because a clamp shows at the ends and a
    /// rounding shows away from them.
    /// </summary>
    [Fact]
    public void EverySettingIsEitherRefusedOrReachesTheRuleAsTheNumberThatWasStored()
    {
        foreach (var setting in Settings.All)
        {
            var middle = setting.Minimum + ((setting.Maximum - setting.Minimum) / 2);

            var values = new[]
            {
                setting.Minimum,
                setting.Minimum + 1,
                middle,
                setting.Maximum - 1,
                setting.Maximum,
            };

            var accepted = 0;

            foreach (var value in values)
            {
                var document = Settings.Document();
                setting.Set(document, value);

                var reading = ServerWideSettings.Read(document);

                if (!reading.IsRead)
                {
                    continue;
                }

                accepted++;

                Assert.Equal(value, setting.Read(reading));
            }

            Assert.True(
                accepted > 0,
                $"{setting.Name} was refused at every value between {setting.Minimum} and {setting.Maximum}, so nothing here compared a number that reached the rule");
        }
    }

    /// <summary>
    /// A value one above the bound the rule declares is refused, for every setting.
    ///
    /// One above rather than something absurd, because the mistake somebody makes is at the
    /// boundary and a fact written against a wild number passes a comparison that is off by one.
    /// The bound itself is asserted accepted in the same pass, so a reader that refused
    /// everything would fail here rather than look strict.
    /// </summary>
    [Fact]
    public void EverySettingIsRefusedOneAboveItsBoundAndAcceptedAtIt()
    {
        foreach (var setting in Settings.All)
        {
            var atTheBound = Settings.Document();
            setting.Set(atTheBound, setting.Maximum);

            Assert.True(
                ServerWideSettings.Read(atTheBound).IsRead,
                $"{setting.Name} was refused at {setting.Maximum}, which is the bound its own rule declares");

            var above = Settings.Document();
            setting.Set(above, setting.Maximum + 1);

            var refusal = Assert.Single(ServerWideSettings.Read(above).Refusals);

            Assert.Equal(setting.Name, refusal.Setting);
            Assert.Equal(setting.Maximum + 1, refusal.Found);
        }
    }

    /// <summary>
    /// A setting whose rule declares a floor of its own is refused one below it and accepted at
    /// it.
    ///
    /// The other six are floored at one of their own unit and the fact below covers them from the
    /// other side. What this one is about is the setting whose dangerous end is the low one: the
    /// failure share, whose floor is a number its own rule declares. A reader that floored
    /// everything at one would accept a share of one per cent, which stops every exchange at the
    /// first item a library no longer holds, and no fact about zero would notice.
    ///
    /// One below rather than something far under, because the mistake is at the boundary.
    /// </summary>
    [Fact]
    public void ASettingWithARuleDeclaredFloorIsRefusedOneBelowItAndAcceptedAtIt()
    {
        var floored = Settings.All.Where(setting => setting.DeclaredFloor is not null).ToList();

        Assert.NotEmpty(floored);

        foreach (var setting in floored)
        {
            var atTheFloor = Settings.Document();
            setting.Set(atTheFloor, setting.Minimum);

            Assert.True(
                ServerWideSettings.Read(atTheFloor).IsRead,
                $"{setting.Name} was refused at {setting.Minimum}, which is the floor its own rule declares");

            var below = Settings.Document();
            setting.Set(below, setting.Minimum - 1);

            var refusal = Assert.Single(ServerWideSettings.Read(below).Refusals);

            Assert.Equal(setting.Name, refusal.Setting);
            Assert.Equal(setting.Minimum - 1, refusal.Found);
        }
    }

    /// <summary>
    /// Zero and everything below it is refused, for every setting.
    ///
    /// Zero is the one worth having a fact about. It is inside every type's range, it reads as a
    /// number somebody meant, and it switches the rule off through the setting: no move
    /// threshold, no echo suppression, no retention. A reader accepting it would leave a server
    /// carrying every progress report a player sends and nobody would have chosen that.
    /// </summary>
    [Fact]
    public void EverySettingIsRefusedAtZeroAndBelow()
    {
        foreach (var setting in Settings.All)
        {
            foreach (var value in new[] { 0, -1 })
            {
                var document = Settings.Document();
                setting.Set(document, value);

                var reading = ServerWideSettings.Read(document);

                Assert.Contains(
                    reading.Refusals,
                    refusal => string.Equals(refusal.Setting, setting.Name, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// A refused reading holds no values at all.
    ///
    /// This is the property the type is shaped around rather than a detail of it. One bad
    /// setting out of six leaves five that were read perfectly well, and a reading offering
    /// those five is one a caller runs on: it would sync with the operator's thresholds and this
    /// plugin's default retention, which is a state nobody chose and nothing reports.
    /// </summary>
    [Fact]
    public void ARefusedReadingHoldsNoValues()
    {
        var reading = ServerWideSettings.Read(new PluginConfiguration { EchoWindowSeconds = 0 });

        Assert.False(reading.IsRead);
        Assert.Null(reading.Positions);
        Assert.Null(reading.EchoWindow);
        Assert.Null(reading.ConflictRetention);
        Assert.Null(reading.ProvenanceRetention);
        Assert.Null(reading.MaximumFailureShare);
    }

    /// <summary>
    /// Every refused setting is named, not the first one.
    ///
    /// The other end of this is a person with a form open, and a reader that stops at the first
    /// mistake sends them round the loop once per mistake. On the line where this document is
    /// read at startup that loop includes a restart.
    /// </summary>
    [Fact]
    public void EveryRefusedSettingIsNamedRatherThanTheFirst()
    {
        var reading = ServerWideSettings.Read(new PluginConfiguration
        {
            PositionMoveSeconds = 0,
            EchoWindowSeconds = -4,
            ProvenanceRetentionDays = 100000,
        });

        Assert.Equal(3, reading.Refusals.Count);
        Assert.Equal(
            new[] { "EchoWindowSeconds", "PositionMoveSeconds", "ProvenanceRetentionDays" },
            reading.Refusals.Select(refusal => refusal.Setting).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// A refusal says what was found and what it had to satisfy, in the setting's own unit.
    ///
    /// A refusal naming only the setting sends an operator to a document to find the range, and
    /// the range is declared on a type they cannot open from the dashboard.
    /// </summary>
    [Fact]
    public void ARefusalSaysWhatWasFoundAndWhatItHadToSatisfy()
    {
        var reading = ServerWideSettings.Read(
            new PluginConfiguration { ConflictRetentionDays = 91 });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal("ConflictRetentionDays", refusal.Setting);
        Assert.Equal(91, refusal.Found);
        Assert.Equal("1 to 90 days", refusal.Bound);
    }

    /// <summary>
    /// A finish distance at or above the shortest item length is refused, and neither number is
    /// outside its own bound.
    ///
    /// This is the relation no single setting can be judged against. Both values here are ones
    /// the two rules accept on their own, which is what makes the pair the subject: a reader
    /// checking each setting against its own bound and stopping passes this document, and what
    /// it then produces is a plugin where every position on the shortest item it carries is a
    /// finish.
    /// </summary>
    [Fact]
    public void AFinishDistanceAtOrAboveTheShortestItemIsRefused()
    {
        foreach (var finish in new[] { 300, 400 })
        {
            var reading = ServerWideSettings.Read(new PluginConfiguration
            {
                PositionFinishSeconds = finish,
                PositionShortestItemSeconds = 300,
            });

            var refusal = Assert.Single(reading.Refusals);

            Assert.Equal("PositionFinishSeconds", refusal.Setting);
            Assert.Contains("PositionShortestItemSeconds", refusal.Bound, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A finish distance one second below the shortest item length is accepted.
    ///
    /// The other side of the same boundary, so that the relation cannot be satisfied by refusing
    /// every pair.
    /// </summary>
    [Fact]
    public void AFinishDistanceBelowTheShortestItemIsAccepted()
    {
        var reading = ServerWideSettings.Read(new PluginConfiguration
        {
            PositionFinishSeconds = 299,
            PositionShortestItemSeconds = 300,
        });

        Assert.True(reading.IsRead);
    }

    /// <summary>
    /// A reading claiming to be refused and naming nothing is refused at the type.
    ///
    /// It is the state a caller cannot act on: they learn something was wrong and never what.
    /// </summary>
    [Fact]
    public void ARefusedReadingThatNamesNothingIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => ServerWideSettingsReading.Refused(Array.Empty<SettingRefusal>()));
    }
}

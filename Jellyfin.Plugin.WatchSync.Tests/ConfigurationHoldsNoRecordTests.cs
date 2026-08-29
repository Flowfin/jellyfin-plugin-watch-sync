using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Storage;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Nothing but a setting is in the plugin configuration, which is the second half of #68's first
/// condition.
///
/// The failure it refuses is not a tidiness one. The plugin configuration is small, is rewritten
/// whole on every save, and is a file an operator copies between servers to stand a second one up
/// behaving like the first. A record that grows with the library in it is rewritten in full every
/// time a setting is saved; and copied to another server, an agreed record says two machines
/// settled on values one of them has never seen, which is the state every conflict rule after it
/// is then decided against.
///
/// It has a subject because #58 landed the first settings. Before that the configuration type
/// carried nothing and a comparison over it was green whatever was in the store, which is what
/// the readings on #68 are about.
///
/// It is a separate fact from the ones #58 landed rather than a second reading of them.
/// <c>PluginConfigurationTests</c> refuses a member that is not an <c>int</c>, which happens to
/// exclude a record today, and it is about how a duration is written. The day a setting is a
/// boolean or a string that fact loosens correctly and this one has to hold unmoved, because what
/// it is about is the kind of thing in the file rather than the shape of a number.
/// </summary>
public class ConfigurationHoldsNoRecordTests
{
    /// <summary>
    /// The configuration type this plugin ships holds no record and no collection.
    /// </summary>
    [Fact]
    public void ThePluginConfigurationHoldsNothingButSettings()
    {
        Assert.Empty(RecordsIn(typeof(PluginConfiguration)));
    }

    /// <summary>
    /// A configuration carrying one of the documents the store holds is refused, by name.
    ///
    /// The near miss is the one somebody actually writes, and it is written for a good reason: the
    /// server already persists this file, so putting the agreed record on it is one less thing to
    /// build. What it costs is invisible until the file is copied.
    /// </summary>
    [Fact]
    public void AConfigurationCarryingAStoredDocumentIsRefused()
    {
        var finding = Assert.Single(RecordsIn(typeof(ConfigurationCarryingARecord)));

        Assert.Contains(nameof(AgreedRecords), finding, StringComparison.Ordinal);
        Assert.Contains("the store", finding, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration carrying a collection is refused even where its element type is one the
    /// store knows nothing about.
    ///
    /// This is the same mistake one step earlier, and it is the shape a record arrives in before
    /// it is a record: a list of item identifiers, of failed writes, of peers seen. What decides it
    /// is that the member grows with the library rather than what it is called, and the file is
    /// rewritten whole on every save.
    /// </summary>
    [Fact]
    public void AConfigurationCarryingACollectionIsRefused()
    {
        var finding = Assert.Single(RecordsIn(typeof(ConfigurationCarryingAList)));

        Assert.Contains("grows", finding, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration of settings alone passes, so the rule is not one that refuses everything.
    ///
    /// It carries a boolean and a string, which no setting is today, so the fact says what it
    /// claims to say rather than agreeing with the number rule beside it by accident.
    /// </summary>
    [Fact]
    public void AConfigurationOfSettingsAloneIsAccepted()
    {
        Assert.Empty(RecordsIn(typeof(ConfigurationOfSettingsAlone)));
    }

    /// <summary>
    /// The store's own declaration is what the rule reads, rather than a list written here.
    ///
    /// A document added to the store and not added here would otherwise be one this rule permits
    /// in the configuration file, which is exactly the direction the mistake goes.
    /// </summary>
    [Fact]
    public void TheRuleReadsTheStoresOwnDeclaration()
    {
        Assert.NotEmpty(StoredKinds.All);

        Assert.Empty(StoredKinds.All
            .Where(kind => !RecordsIn(ConfigurationCarrying(kind.DeclaredBy)).Any())
            .Select(kind =>
                $"a configuration carrying a {kind.DeclaredBy.Name} is not refused, and the store declares it as a document it holds"));
    }

    /// <summary>
    /// What is in a type that is not a setting, one line per member, as the sentence a reader of
    /// the failure gets.
    ///
    /// A function over a type rather than an assertion inside a fact, so the same rule judges the
    /// type this plugin ships and the near misses above, and so a rule that stopped refusing
    /// either shape is a red suite rather than a green one.
    /// </summary>
    /// <param name="configuration">The type to judge.</param>
    /// <returns>The findings.</returns>
    private static IReadOnlyList<string> RecordsIn(Type configuration)
    {
        var stored = StoredKinds.All.Select(kind => kind.DeclaredBy).ToList();
        var findings = new List<string>();

        foreach (var member in configuration.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (stored.Contains(member.PropertyType))
            {
                findings.Add(
                    $"{configuration.Name}.{member.Name} is a {member.PropertyType.Name}, which the store declares as a document it holds, and the store is where it belongs");

                continue;
            }

            if (member.PropertyType != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(member.PropertyType))
            {
                findings.Add(
                    $"{configuration.Name}.{member.Name} is a {member.PropertyType.Name}, which grows with what this plugin has seen, and this file is rewritten whole on every save and copied between servers");
            }
        }

        return findings;
    }

    /// <summary>
    /// A configuration type carrying one member of a given type, built rather than written, so
    /// every document the store declares is judged rather than the one somebody remembered.
    /// </summary>
    /// <param name="held">The type the member has.</param>
    /// <returns>A type whose single member is of that type.</returns>
    private static Type ConfigurationCarrying(Type held) =>
        typeof(Carrier<>).MakeGenericType(held);

    /// <summary>
    /// A configuration with an agreed record on it, which is the near miss written out.
    /// </summary>
    private sealed class ConfigurationCarryingARecord
    {
        /// <summary>
        /// Gets or sets what two servers last agreed.
        /// </summary>
        public AgreedRecords? Agreed { get; set; }
    }

    /// <summary>
    /// A configuration with a list on it, which is a record before anybody calls it one.
    /// </summary>
    private sealed class ConfigurationCarryingAList
    {
        /// <summary>
        /// Gets the items this plugin could not match.
        /// </summary>
        public IList<string> Unmatched { get; } = new List<string>();
    }

    /// <summary>
    /// A configuration of settings alone, including two shapes no setting takes today.
    /// </summary>
    private sealed class ConfigurationOfSettingsAlone
    {
        /// <summary>
        /// Gets or sets how long something is kept.
        /// </summary>
        public int RetentionDays { get; set; }

        /// <summary>
        /// Gets or sets whether something is on.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets a name somebody typed.
        /// </summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// One member of whatever type the store declares.
    /// </summary>
    /// <typeparam name="T">The type held.</typeparam>
    private sealed class Carrier<T>
    {
        /// <summary>
        /// Gets or sets the held value.
        /// </summary>
        public T? Held { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Records;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the record of a conflict to what it is allowed to carry, and its rule vocabulary to the
/// conflict table, which is the second condition of #36.
///
/// The shape rule is the one worth having as a machine. A record of a conflict is written to be
/// shown on a page and read out of a log, and the two things that must never reach either are a
/// title, which turns a diagnostic into a viewing history, and any string a peer chose, which is
/// text from a machine this server does not administer. Neither is refused by naming them: a
/// vocabulary of forbidden words is a list somebody adds to after the mistake. What is refused
/// is every type outside the set the record needs, so a member of any string type cannot be
/// added at all.
///
/// The rule column is closed against <c>docs/conflicts.md</c> in both directions, reading the
/// declaration through the same helper <c>ConflictTableTests</c> reads it with rather than a
/// second parser over the same file.
/// </summary>
public class ConflictRecordTests
{
    /// <summary>
    /// The types a conflict record may be made of. Identifiers, the field, the rule, the side,
    /// the two readings and the moment. A type outside this set is refused rather than judged,
    /// so the guard does not have to know what a title is called next time.
    /// </summary>
    private static readonly IReadOnlyList<Type> _permitted = new[]
    {
        typeof(Guid),
        typeof(SyncedField),
        typeof(ConflictRule),
        typeof(ConflictSide),
        typeof(long?),
        typeof(DateTimeOffset),
    };

    /// <summary>
    /// The whole point. Every member of the record is one of the permitted types, so a string
    /// cannot be added to it whatever it is named.
    /// </summary>
    [Fact]
    public void TheRecordCarriesNothingOutsideThePermittedTypes()
    {
        var members = Members();

        Assert.NotEmpty(members);

        Assert.Empty(members
            .Where(member => !_permitted.Contains(member.PropertyType))
            .Select(member =>
                $"{member.Name} is a {member.PropertyType} and a conflict record carries identifiers, the field, the rule, the side, the two readings and the moment. A title or a path makes the record a viewing history, and text a peer chose reaches a page and a log through it."));
    }

    /// <summary>
    /// The record has to be able to say what the table's rows say, so every rule the document
    /// declares has a member here. A declared rule with no member is a conflict that would be
    /// recorded as something else or not at all.
    /// </summary>
    [Fact]
    public void EveryRuleTheDocumentDeclaresHasAMember()
    {
        var declared = ConflictTableTests.ConflictDocument.DeclaredRules(
            ConflictTableTests.ConflictDocument.Text());

        Assert.NotEmpty(declared);

        Assert.Empty(declared
            .Where(rule => !Members(typeof(ConflictRule)).Contains(rule, StringComparer.OrdinalIgnoreCase))
            .Select(rule => $"{rule} is declared by the conflict table and no member of {nameof(ConflictRule)} names it, so a conflict it decided could not be recorded as its own."));
    }

    /// <summary>
    /// The other direction. A member naming a rule the document does not declare is a rule
    /// argued nowhere, and a record carrying it would be the invented vocabulary the table's own
    /// closure exists against, one register further out.
    /// </summary>
    [Fact]
    public void NoMemberNamesARuleTheDocumentDoesNotDeclare()
    {
        var declared = ConflictTableTests.ConflictDocument.DeclaredRules(
            ConflictTableTests.ConflictDocument.Text());

        Assert.Empty(Members(typeof(ConflictRule))
            .Where(member => !declared.Contains(member, StringComparer.OrdinalIgnoreCase))
            .Select(member => $"{nameof(ConflictRule)}.{member} names a rule the conflict table does not declare."));
    }

    /// <summary>
    /// A record that says this server lost a reading it never held is one an operator reads as a
    /// value this plugin threw away, and there was none.
    /// </summary>
    [Fact]
    public void ThisServerCannotLoseAReadingItNeverHeld()
    {
        var refused = Assert.Throws<ArgumentException>(() => new ConflictRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SyncedField.PlaybackPositionTicks,
            ConflictRule.Recency,
            here: null,
            atThePeer: 42,
            ConflictSide.Here,
            DateTimeOffset.UnixEpoch));

        Assert.Equal("discarded", refused.ParamName);
    }

    /// <summary>
    /// The same refusal in the other direction, which is the one-character neighbour of the case
    /// above and the arm a guard written for one side leaves open.
    /// </summary>
    [Fact]
    public void ThePeerCannotLoseAReadingItNeverOffered()
    {
        var refused = Assert.Throws<ArgumentException>(() => new ConflictRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SyncedField.PlaybackPositionTicks,
            ConflictRule.Recency,
            here: 42,
            atThePeer: null,
            ConflictSide.AtThePeer,
            DateTimeOffset.UnixEpoch));

        Assert.Equal("discarded", refused.ParamName);
    }

    /// <summary>
    /// A discarded reading that was there is carried, and the record holds every part of it
    /// unchanged. This is the passing neighbour of the two refusals above.
    /// </summary>
    [Fact]
    public void ADiscardedReadingThatWasThereIsCarried()
    {
        var pairing = Guid.NewGuid();
        var user = Guid.NewGuid();
        var item = Guid.NewGuid();
        var moment = new DateTimeOffset(2026, 8, 26, 21, 0, 0, TimeSpan.Zero);

        var record = new ConflictRecord(
            pairing,
            user,
            item,
            SyncedField.PlaybackPositionTicks,
            ConflictRule.Recency,
            here: 10,
            atThePeer: 20,
            ConflictSide.Here,
            moment);

        Assert.Equal(pairing, record.PairingId);
        Assert.Equal(user, record.MappedUserId);
        Assert.Equal(item, record.ItemId);
        Assert.Equal(SyncedField.PlaybackPositionTicks, record.Field);
        Assert.Equal(ConflictRule.Recency, record.Rule);
        Assert.Equal(10, record.Here);
        Assert.Equal(20, record.AtThePeer);
        Assert.Equal(ConflictSide.Here, record.Discarded);
        Assert.Equal(moment, record.RecordedAt);
    }

    /// <summary>
    /// The two rows that discard nothing are recordable, with both readings absent, because a
    /// rule that ran is worth recording whether or not it took anything away.
    /// </summary>
    [Fact]
    public void ARuleThatDiscardsNothingIsRecordable()
    {
        var record = new ConflictRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SyncedField.PlayCount,
            ConflictRule.Reckon,
            here: null,
            atThePeer: null,
            ConflictSide.Neither,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(ConflictSide.Neither, record.Discarded);
        Assert.Null(record.Here);
        Assert.Null(record.AtThePeer);
    }

    /// <summary>
    /// The public instance properties of the record.
    /// </summary>
    /// <returns>Its members.</returns>
    private static IReadOnlyList<PropertyInfo> Members() =>
        typeof(ConflictRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToList();

    /// <summary>
    /// The member names of an enumeration.
    /// </summary>
    /// <param name="enumeration">The enumeration to read.</param>
    /// <returns>Its member names.</returns>
    private static IReadOnlyList<string> Members(Type enumeration) =>
        Enum.GetNames(enumeration).ToList();
}

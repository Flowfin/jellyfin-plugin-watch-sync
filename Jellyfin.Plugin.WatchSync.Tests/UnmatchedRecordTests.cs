using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Matching;
using Jellyfin.Plugin.WatchSync.Records;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What one record of an unmatched item may say and what it refuses to say, which is #26's rule
/// that no match is a terminal answer with a reason recorded and never a guess.
///
/// Three refusals are what this set is about, and each of them is a shape somebody writes rather
/// than a shape nobody would. A reason carried in two members is carried in exactly one of them:
/// deriving a key and looking one up are two steps and an item falls out of either, so a record
/// carrying both says a lookup happened after the key was refused, and a record carrying neither
/// says an item did not match and does not say why. The third is an item that matched being
/// recorded as one that did not, which is a count an operator would go looking for a repair for.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public class UnmatchedRecordTests
{
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The types a record of an unmatched item may be made of. The item, its class, the two
    /// halves of the reason and the moment. A type outside this set is refused rather than
    /// judged, so the guard does not have to know what a title is called next time.
    /// </summary>
    private static readonly IReadOnlyList<Type> _permitted = new[]
    {
        typeof(Guid),
        typeof(BaseItemKind),
        typeof(MatchKeyRefusal),
        typeof(MatchAnswer?),
        typeof(DateTimeOffset),
    };

    /// <summary>
    /// Every member of the record is one of the permitted types.
    ///
    /// This record is about items a peer may have offered, so what it may carry is narrower than
    /// it looks: a title makes the list a viewing history, and a string a machine this server does
    /// not administer chose would arrive in a record meant to be counted and shown on a page.
    /// </summary>
    [Fact]
    public void TheRecordCarriesNothingOutsideThePermittedTypes()
    {
        var members = Members();

        Assert.NotEmpty(members);

        Assert.Empty(members
            .Where(member => !_permitted.Contains(member.PropertyType))
            .Select(member =>
                $"{member.Name} is a {member.PropertyType} and a record of an unmatched item carries the item, its class, the reason and the moment. A title or a path makes the record a viewing history, and text a peer chose reaches a page and a log through it."));
    }

    /// <summary>
    /// A record carrying a refusal and an answer together is refused.
    ///
    /// The key was refused, so nothing was looked up. A record saying both would let a reader
    /// conclude that the peer holds nothing matching an item whose key this server never derived,
    /// which sends an operator to the peer's library for a repair that belongs on this one.
    /// </summary>
    [Fact]
    public void ARefusalBesideAnAnswerIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => new UnmatchedRecord(
            _film,
            BaseItemKind.Movie,
            MatchKeyRefusal.NoIdentifierAtAll,
            MatchAnswer.NoMatch,
            _evening));

        Assert.Equal("answer", refused.ParamName);
    }

    /// <summary>
    /// A record carrying neither a refusal nor an answer is refused.
    ///
    /// It says the item did not match and says nothing about why, which is the row an operator
    /// cannot act on and the one that makes a fallback to a weaker comparison look reasonable.
    /// </summary>
    [Fact]
    public void ARecordThatSaysNothingAboutWhyIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => new UnmatchedRecord(
            _film,
            BaseItemKind.Movie,
            MatchKeyRefusal.None,
            null,
            _evening));

        Assert.Equal("answer", refused.ParamName);
    }

    /// <summary>
    /// A record naming a match is refused. An item that matched is not an unmatched item, and a
    /// count holding one is a repair somebody goes looking for and never finds.
    /// </summary>
    [Fact]
    public void AnItemThatMatchedIsNotRecordableAsUnmatched()
    {
        var refused = Assert.Throws<ArgumentException>(() => new UnmatchedRecord(
            _film,
            BaseItemKind.Movie,
            MatchKeyRefusal.None,
            MatchAnswer.Matched,
            _evening));

        Assert.Equal("answer", refused.ParamName);
    }

    /// <summary>
    /// Both halves of the reason are recordable on their own, which is what the two members are
    /// for. Every reason the guide is written against is one or the other.
    /// </summary>
    /// <param name="refusal">The refusal, where the key was never derived.</param>
    /// <param name="answer">The answer, where it was.</param>
    [Theory]
    [InlineData(MatchKeyRefusal.NoIdentifierAtAll, null)]
    [InlineData(MatchKeyRefusal.NoIdentifierFromAPreferredProvider, null)]
    [InlineData(MatchKeyRefusal.SpansSeveralEpisodes, null)]
    [InlineData(MatchKeyRefusal.None, MatchAnswer.NoMatch)]
    [InlineData(MatchKeyRefusal.None, MatchAnswer.Ambiguous)]
    public void EitherHalfOfTheReasonIsRecordableOnItsOwn(
        MatchKeyRefusal refusal,
        MatchAnswer? answer)
    {
        var record = new UnmatchedRecord(_film, BaseItemKind.Episode, refusal, answer, _evening);

        Assert.Equal(refusal, record.Refusal);
        Assert.Equal(answer, record.Answer);
    }

    /// <summary>
    /// Every reason the unmatched guide is written against is one this record can hold.
    ///
    /// The vocabulary is read off the two enumerations rather than listed here, which is the shape
    /// <c>UnmatchedGuideTests</c> already uses, so a reason added to either joins this fact without
    /// anybody editing it. A reason a record cannot carry is a section of that guide describing a
    /// row nothing can produce.
    /// </summary>
    [Fact]
    public void EveryReasonTheGuideIsWrittenAgainstIsRecordable()
    {
        var recordable = Enum.GetValues<MatchKeyRefusal>()
            .Where(refusal => refusal != MatchKeyRefusal.None)
            .Select(refusal => new UnmatchedRecord(
                _film, BaseItemKind.Movie, refusal, null, _evening))
            .Select(record => record.Refusal.ToString())
            .Concat(Enum.GetValues<MatchAnswer>()
                .Where(answer => answer != MatchAnswer.Matched)
                .Select(answer => new UnmatchedRecord(
                    _film, BaseItemKind.Movie, MatchKeyRefusal.None, answer, _evening))
                .Select(record => record.Answer!.Value.ToString()))
            .ToList();

        Assert.Equal(
            Enum.GetNames<MatchKeyRefusal>().Length + Enum.GetNames<MatchAnswer>().Length - 2,
            recordable.Count);
    }

    /// <summary>
    /// The public instance properties of the record.
    /// </summary>
    /// <returns>Its members.</returns>
    private static IReadOnlyList<PropertyInfo> Members() =>
        typeof(UnmatchedRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToList();
}

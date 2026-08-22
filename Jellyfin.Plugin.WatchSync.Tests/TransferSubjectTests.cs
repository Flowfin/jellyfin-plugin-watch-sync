using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The unit one transfer is about, and what cannot be one.
///
/// <c>docs/sync-model.md</c> fixes the unit as one mapped user and one leaf item, and says
/// that #13 refuses an aggregate by construction rather than by care. These facts drive the
/// only route to a subject there is, so what they assert is the type's whole surface rather
/// than one path through a larger one.
/// </summary>
public class TransferSubjectTests
{
    private static readonly Guid _user = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _item = new Guid("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// The refusal each disposition in <c>docs/matching.md</c> produces.
    ///
    /// This is the only place the two vocabularies are tied together, and the facts below
    /// hold it to the document in both directions, so a seventh disposition or a renamed one
    /// fails here rather than leaving a kind answered by the last arm of a switch.
    /// </summary>
    private static IReadOnlyDictionary<string, TransferSubjectRefusal> RefusalPerDisposition =>
        new Dictionary<string, TransferSubjectRefusal>(StringComparer.Ordinal)
        {
            ["synced"] = TransferSubjectRefusal.None,
            ["aggregate"] = TransferSubjectRefusal.KindIsAnAggregate,
            ["container"] = TransferSubjectRefusal.KindIsAContainer,
            ["facet"] = TransferSubjectRefusal.KindIsAFacet,
            ["ephemeral"] = TransferSubjectRefusal.KindIsEphemeral,
            ["deferred"] = TransferSubjectRefusal.KindIsDeferred,
        };

    /// <summary>
    /// The two kinds the matching document gives a key rule to are the two that produce a
    /// subject, and the subject carries the pair it was read from.
    /// </summary>
    /// <param name="kind">A kind the document calls synced.</param>
    [Theory]
    [InlineData(BaseItemKind.Movie)]
    [InlineData(BaseItemKind.Episode)]
    public void AMappedUserAndALeafItemAreATransferSubject(BaseItemKind kind)
    {
        var reading = TransferSubject.From(_user, _item, kind);

        Assert.True(reading.IsSubject);
        Assert.Equal(TransferSubjectRefusal.None, reading.Refusal);

        var subject = Assert.IsType<TransferSubject>(reading.Value);
        Assert.Equal(_user, subject.MappedUserId);
        Assert.Equal(_item, subject.ItemId);
        Assert.Equal(kind, subject.Kind);
    }

    /// <summary>
    /// The five this issue names, each with the reason the document gives for it.
    ///
    /// They are one case each rather than one case asserting that none of them is a subject,
    /// because a refusal that named the wrong reason would pass the second shape. A folder is
    /// refused for holding no watch state and a season for having its state derived from the
    /// episodes under it, and an operator reading the two needs to be told which.
    /// </summary>
    /// <param name="kind">The kind to read.</param>
    /// <param name="expected">The refusal the document's disposition for it produces.</param>
    [Theory]
    [InlineData(BaseItemKind.Folder, TransferSubjectRefusal.KindIsAContainer)]
    [InlineData(BaseItemKind.Season, TransferSubjectRefusal.KindIsAnAggregate)]
    [InlineData(BaseItemKind.Series, TransferSubjectRefusal.KindIsAnAggregate)]
    [InlineData(BaseItemKind.BoxSet, TransferSubjectRefusal.KindIsAnAggregate)]
    [InlineData(BaseItemKind.Playlist, TransferSubjectRefusal.KindIsAnAggregate)]
    public void AFolderASeasonASeriesACollectionAndAPlaylistAreNotTransferSubjects(
        BaseItemKind kind,
        TransferSubjectRefusal expected)
    {
        var reading = TransferSubject.From(_user, _item, kind);

        Assert.False(reading.IsSubject);
        Assert.Null(reading.Value);
        Assert.Equal(expected, reading.Refusal);
    }

    /// <summary>
    /// Every kind the server itself enumerates, answered the way the document dispositions
    /// it.
    ///
    /// The kinds come from the referenced assembly and the dispositions from the tracked
    /// document, so neither side of this is a list maintained here. A kind moved between two
    /// dispositions in the table, and a kind added upstream that the switch has no arm for,
    /// both fail here.
    /// </summary>
    [Fact]
    public void EveryKindTheServerHasIsAnsweredTheWayTheDocumentDispositionsIt()
    {
        var refusalPerDisposition = RefusalPerDisposition;
        var rows = MatchingDocumentTests.MatchingDocument.Rows(MatchingDocumentTests.MatchingDocument.Text());

        Assert.NotEmpty(rows);

        Assert.Empty(rows
            .Where(row => refusalPerDisposition.ContainsKey(row.Disposition))
            .Where(row => TransferSubject.From(_user, _item, Enum.Parse<BaseItemKind>(row.Kind)).Refusal
                          != refusalPerDisposition[row.Disposition])
            .Select(row =>
                $"{row.Kind} is {row.Disposition} in docs/matching.md and reads as "
                + $"{TransferSubject.From(_user, _item, Enum.Parse<BaseItemKind>(row.Kind)).Refusal}."));
    }

    /// <summary>
    /// The document's dispositions and the refusals above are the same set.
    ///
    /// Without this the fact above skips a row whose disposition it has no entry for, so a
    /// disposition added to the table would silently leave its kinds unjudged here.
    /// </summary>
    [Fact]
    public void EveryDispositionTheDocumentDeclaresHasARefusalAndNoRefusalOutlivesOne()
    {
        var declared = MatchingDocumentTests.MatchingDocument
            .DeclaredDispositions(MatchingDocumentTests.MatchingDocument.Text());
        var answered = RefusalPerDisposition.Keys;

        Assert.NotEmpty(declared);

        Assert.Empty(declared
            .Where(disposition => !answered.Contains(disposition, StringComparer.Ordinal))
            .Select(disposition => $"{disposition} is declared by the document and produces no refusal here."));

        Assert.Empty(answered
            .Where(disposition => !declared.Contains(disposition, StringComparer.Ordinal))
            .Select(disposition => $"{disposition} produces a refusal here and the document declares no such disposition."));
    }

    /// <summary>
    /// A kind this version has never heard of, which is what a kind added to a later server
    /// looks like from here.
    ///
    /// It is refused and named as unknown rather than being read as a leaf, because the safe
    /// answer for a kind nothing here has classified is the one that moves nothing, and
    /// rather than throwing, because a library holding one is a library to walk past.
    /// </summary>
    [Fact]
    public void AKindThisVersionHasNeverHeardOfIsRefusedRatherThanCarried()
    {
        var reading = TransferSubject.From(_user, _item, (BaseItemKind)10_000);

        Assert.False(reading.IsSubject);
        Assert.Null(reading.Value);
        Assert.Equal(TransferSubjectRefusal.KindIsUnknownToThisVersion, reading.Refusal);
    }

    /// <summary>
    /// A pair naming no user, on a kind that would otherwise be a subject and on one that
    /// would not.
    ///
    /// The second case is what fixes the order: an empty identifier is a mistake at the call
    /// rather than a fact about the library, and a reading that answered the kind for it
    /// would send whoever reads the record at a series that is not the problem.
    /// </summary>
    /// <param name="kind">The kind to read.</param>
    [Theory]
    [InlineData(BaseItemKind.Movie)]
    [InlineData(BaseItemKind.Series)]
    public void APairNamingNoMappedUserIsRefusedForThatBeforeAnythingElse(BaseItemKind kind)
    {
        var reading = TransferSubject.From(Guid.Empty, _item, kind);

        Assert.False(reading.IsSubject);
        Assert.Null(reading.Value);
        Assert.Equal(TransferSubjectRefusal.NoMappedUser, reading.Refusal);
    }

    /// <summary>
    /// A pair naming no item, with the same reasoning and the same ordering.
    /// </summary>
    /// <param name="kind">The kind to read.</param>
    [Theory]
    [InlineData(BaseItemKind.Movie)]
    [InlineData(BaseItemKind.Series)]
    public void APairNamingNoItemIsRefusedForThatBeforeTheKind(BaseItemKind kind)
    {
        var reading = TransferSubject.From(_user, Guid.Empty, kind);

        Assert.False(reading.IsSubject);
        Assert.Null(reading.Value);
        Assert.Equal(TransferSubjectRefusal.NoItem, reading.Refusal);
    }

    /// <summary>
    /// What makes the refusal structural rather than careful.
    ///
    /// If a caller can construct one of these directly, every fact above is a statement about
    /// one route among two, and the second route is the one somebody reaches for at the
    /// moment the first one refused them.
    /// </summary>
    [Fact]
    public void TheOnlyRouteToASubjectIsTheReading()
    {
        Assert.Empty(typeof(TransferSubject).GetConstructors());
    }
}

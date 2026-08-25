using System;
using Jellyfin.Plugin.WatchSync.Document;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The version every document in this plugin's store carries, and the document from the future
/// that is refused rather than read, which is #69.
///
/// One document shape exists, which is the agreed record in #14; the outbound queue is #48, the
/// conflict and unmatched records are #36 and #26, and the provenance is #44, and those four are
/// not in the tree. The facts here still drive the version and the members beside it rather than
/// any shape, and that is the point rather than a consequence of the four being absent: what #69
/// asks for is the rule that holds for every document, and a rule proven against the first
/// document to arrive would be a rule about that document.
/// </summary>
public class StoredDocumentTests
{
    private const int VersionThisCodeWrites = 3;

    /// <summary>
    /// A document written by a later version is refused, and nothing is read out of it.
    ///
    /// This is the first condition of #69. The reading carries no document, so a caller has
    /// nothing to read the known members out of and cannot half-read one by accident.
    /// </summary>
    [Fact]
    public void ADocumentFromTheFutureIsRefusedRatherThanRead()
    {
        var reading = StoredDocument.Read(
            """{"version":4,"agreedPlayCount":2}""",
            VersionThisCodeWrites);

        Assert.Equal(DocumentAnswer.FromTheFuture, reading.Answer);
        Assert.True(reading.IsRefused);
        Assert.Null(reading.Document);
    }

    /// <summary>
    /// A refused document is not rewritten, so nothing is lost by having tried to read it.
    ///
    /// This is the second condition of #69, and it is a property of the type rather than a rule
    /// a caller follows. A refusal carries no document, so there is nothing to write back: the
    /// quiet destruction the refusal exists against is a reader that keeps what it understood
    /// and writes that, and here there is nothing to keep.
    /// </summary>
    [Fact]
    public void ARefusedDocumentCarriesNothingToWriteBack()
    {
        var fromTheFuture = StoredDocument.Read(
            """{"version":9,"aMemberThisCodeHasNeverHeardOf":true}""",
            VersionThisCodeWrites);

        var notADocument = StoredDocument.Read("{}", VersionThisCodeWrites);

        Assert.Null(fromTheFuture.Document);
        Assert.Null(notADocument.Document);
    }

    /// <summary>
    /// The refusal names the version it found and the version it expected.
    ///
    /// Both numbers are what an operator needs, because the repair is running the version that
    /// wrote the document and they cannot run it without knowing which it was. The sentence
    /// that says so belongs on the status page, which is #62, and the numbers leave this type
    /// as numbers rather than as prose assembled here.
    /// </summary>
    [Fact]
    public void TheRefusalCarriesBothVersions()
    {
        var reading = StoredDocument.Read("""{"version":11}""", VersionThisCodeWrites);

        Assert.Equal(11, reading.FoundVersion);
        Assert.Equal(VersionThisCodeWrites, reading.ExpectedVersion);
    }

    /// <summary>
    /// A member this code has never heard of survives being read and written again.
    ///
    /// This is the fourth condition of #69 and the failure it names is the expensive one. An
    /// older version is allowed to read a document at its own version, and if reading it means
    /// deserializing into a shape and serializing that shape back, every member the shape does
    /// not declare is gone on the next write. The document is the object that was read, so the
    /// member is still in it.
    /// </summary>
    [Fact]
    public void AMemberThisCodeDoesNotKnowSurvivesAWrite()
    {
        var reading = StoredDocument.Read(
            """{"version":3,"agreedPlayCount":2,"somethingALaterVersionAdded":{"kept":[1,2]}}""",
            VersionThisCodeWrites);

        var document = Assert.IsType<StoredDocument>(reading.Document);
        var written = document.ToJson();

        Assert.Contains("somethingALaterVersionAdded", written, StringComparison.Ordinal);
        Assert.Contains("\"kept\":[1,2]", written, StringComparison.Ordinal);
        Assert.Contains("agreedPlayCount", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The version is the version and is not also one of the members beside it.
    ///
    /// A document holding the version in both places is one where the two can disagree, and
    /// the moment they do is the upgrade in #71: the version moves and a stale copy of the old
    /// one is still sitting among the members, ready to be written back over it. Keeping the
    /// members free of it is what makes that impossible rather than merely unlikely.
    /// </summary>
    [Fact]
    public void TheVersionIsNotAlsoOneOfTheMembersBesideIt()
    {
        var reading = StoredDocument.Read(
            """{"version":3,"agreedPlayCount":2}""",
            VersionThisCodeWrites);

        var document = Assert.IsType<StoredDocument>(reading.Document);
        var written = document.ToJson();

        Assert.Equal(3, document.Version);
        Assert.False(document.Fields.ContainsKey("version"));
        Assert.Equal("""{"version":3,"agreedPlayCount":2}""", written);
    }

    /// <summary>
    /// A document at the version this code writes is current, and an older one is not.
    ///
    /// The two are separate answers because a reader that took an older document for a current
    /// one would read members that moved and miss members that arrived, without anything
    /// saying so. Carrying an older document forward one version at a time is #71, and this
    /// rule hands it over rather than doing it.
    /// </summary>
    [Fact]
    public void AnOlderDocumentIsReadableAndIsNotCurrent()
    {
        var current = StoredDocument.Read("""{"version":3}""", VersionThisCodeWrites);
        var older = StoredDocument.Read("""{"version":1}""", VersionThisCodeWrites);

        Assert.Equal(DocumentAnswer.Current, current.Answer);
        Assert.Equal(DocumentAnswer.OlderThanThisCode, older.Answer);
        Assert.False(older.IsRefused);
        Assert.NotNull(older.Document);
    }

    /// <summary>
    /// Bytes that are not one of these documents are told apart from a document from the
    /// future.
    ///
    /// The four cases here are what a store folder actually collects: a file cut off by a kill
    /// in the middle of a write, which is #70's subject; something else's file; a document
    /// whose version is not a number; and one whose version is not a whole number above zero.
    /// Reading any of them as version zero or as the oldest version known would turn it into
    /// an upgrade, and the upgrade would write something back.
    /// </summary>
    [Fact]
    public void BytesThatAreNotOneOfTheseDocumentsAreNotReadAsAnOldOne()
    {
        var cutOff = StoredDocument.Read("""{"version":3,"agreed""", VersionThisCodeWrites);
        var somethingElse = StoredDocument.Read("""["not","an","object"]""", VersionThisCodeWrites);
        var notANumber = StoredDocument.Read("""{"version":"3"}""", VersionThisCodeWrites);
        var notAVersion = StoredDocument.Read("""{"version":0}""", VersionThisCodeWrites);

        Assert.Equal(DocumentAnswer.NotADocument, cutOff.Answer);
        Assert.Equal(DocumentAnswer.NotADocument, somethingElse.Answer);
        Assert.Equal(DocumentAnswer.NotADocument, notANumber.Answer);
        Assert.Equal(DocumentAnswer.NotADocument, notAVersion.Answer);
        Assert.Null(cutOff.FoundVersion);
    }

    /// <summary>
    /// A version this code writes that is not a whole number above zero is a defect in the
    /// caller and is refused as one.
    ///
    /// It is not a state a document can be in, so answering it with one of the four document
    /// answers would report a caller's mistake as a fact about the store, and every document
    /// in the store would come back as being from the future.
    /// </summary>
    [Fact]
    public void AVersionThisCodeCouldNotHaveWrittenIsRefusedAsACallersMistake()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StoredDocument.Read("""{"version":1}""", 0));
    }
}

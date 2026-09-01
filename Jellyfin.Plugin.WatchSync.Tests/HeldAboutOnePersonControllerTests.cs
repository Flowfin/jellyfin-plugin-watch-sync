using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Api;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The surface a person's question about their own record is answered through, which is #74's
/// first condition.
///
/// What the two operations do is <see cref="HeldAboutOnePerson"/>'s and is covered beside that
/// type. What is covered here is only what the surface adds: that an identifier naming nobody is
/// refused before the store is walked, that what is answered carries every field somebody was
/// told is everything, and that a person this plugin holds nothing about and a person this server
/// has never had are answered the same way.
///
/// Nothing here starts a server or a request. The controller is constructed with the store and
/// called, because these facts are about the answer rather than about the routing, and the
/// routing is held by the two comparisons in <c>EndpointPolicyTests</c> and
/// <c>EndpointDocumentTests</c> against the attributes themselves.
/// </summary>
public sealed class HeldAboutOnePersonControllerTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _person = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _somebodyElse = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _neverHere = new("55555555-5555-5555-5555-555555555555");

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeldAboutOnePersonControllerTests"/> class,
    /// with a directory of its own standing in for what a server would hand over.
    /// </summary>
    public HeldAboutOnePersonControllerTests()
    {
        _programData = TemporaryDirectory.Create("heldapi");
        Directory.CreateDirectory(DataPath);
    }

    private string DataPath => Path.Combine(_programData.FullPath, "data");

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The report carries every document the store holds about the person, and none about anybody
    /// else.
    ///
    /// Every field of every entry is asserted rather than the count alone. A report is what
    /// somebody is handed when they ask what is held about them, so an entry naming a document
    /// and carrying nothing of it would be telling them something exists and not showing it.
    /// </summary>
    [Fact]
    public void TheReportCarriesEveryDocumentAboutThePersonAndNoneAboutAnybodyElse()
    {
        var store = Store();
        var kind = StoredKinds.All[0];

        store.Write(NameFor(kind, _pairing, _person), _ => Document("mine"));
        store.Write(NameFor(kind, _otherPairing, _person), _ => Document("mine too"));
        store.Write(NameFor(kind, _pairing, _somebodyElse), _ => Document("not mine"));

        var report = Answer(new HeldAboutOnePersonController(store).Report(_person));

        Assert.Equal(_person, report.MappedUserId);
        Assert.Equal(2, report.Count);
        Assert.Equal(report.Count, report.Records.Count);

        Assert.Equal(
            new[] { _otherPairing, _pairing }.OrderBy(each => each).ToList(),
            report.Records.Select(record => record.PairingId).OrderBy(each => each).ToList());

        foreach (var record in report.Records)
        {
            Assert.Equal(kind.NamePrefix, record.Kind);
            Assert.StartsWith(kind.NamePrefix, record.Name, StringComparison.Ordinal);
            Assert.Equal(DocumentVersions.Current, record.Version);
            Assert.Contains("mine", record.Document, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            report.Records,
            record => record.Document.Contains("not mine", StringComparison.Ordinal));
    }

    /// <summary>
    /// A person this plugin holds nothing about and a person this server has never had are
    /// answered identically, which is the deliberate sameness <c>docs/endpoints.md</c> declares.
    ///
    /// This plugin holds no list of users, so what it answers about somebody it has never written
    /// a document for cannot depend on whether that person exists. A fact asserting only the empty
    /// report would pass a controller that had started asking the server, so what is asserted is
    /// that the two answers agree.
    /// </summary>
    [Fact]
    public void APersonWithNoRecordsAndAPersonThisServerNeverHadAreAnsweredTheSame()
    {
        var store = Store();

        store.Write(NameFor(StoredKinds.All[0], _pairing, _person), _ => Document("mine"));

        var controller = new HeldAboutOnePersonController(store);

        var withNoRecords = Answer(controller.Report(_somebodyElse));
        var neverHad = Answer(controller.Report(_neverHere));

        Assert.Equal(0, withNoRecords.Count);
        Assert.Equal(withNoRecords.Count, neverHad.Count);
        Assert.Empty(withNoRecords.Records);
        Assert.Empty(neverHad.Records);
    }

    /// <summary>
    /// An identifier naming nobody is refused by both endpoints, and it is refused before the
    /// store is walked.
    ///
    /// The all-zero identifier is what an empty field on a page sends. A walk driven by it would
    /// be a walk over every document whose name carries an empty identifier, and a removal driven
    /// by it would delete them, which is why the rule underneath refuses it as well. What the
    /// endpoint owes on top of that is a status a caller can act on rather than a fault, and the
    /// assertion that the store is untouched is what says the refusal came first.
    /// </summary>
    [Fact]
    public void AnIdentifierNamingNobodyIsRefusedByBothAndTheStoreIsUntouched()
    {
        var store = Store();
        var name = NameFor(StoredKinds.All[0], _pairing, _person);

        store.Write(name, _ => Document("mine"));

        var controller = new HeldAboutOnePersonController(store);

        Assert.IsType<BadRequestResult>(controller.Report(Guid.Empty).Result);
        Assert.IsType<BadRequestResult>(controller.Remove(Guid.Empty).Result);

        Assert.Contains(name, store.Names());
    }

    /// <summary>
    /// A removal answers how many documents went, leaves nothing about that person, and leaves
    /// what is held about anybody else where it was.
    ///
    /// The count and the scan are both here on purpose. A count is what the caller is told and a
    /// scan is what is true, and they come apart in the direction that matters: a removal that
    /// reported what it found rather than what it removed would satisfy a fact about the number
    /// and leave the documents on the disk.
    /// </summary>
    [Fact]
    public void ARemovalAnswersWhatWentAndLeavesNothingAboutThePerson()
    {
        var store = Store();
        var kind = StoredKinds.All[0];
        var theirs = NameFor(kind, _pairing, _somebodyElse);

        store.Write(NameFor(kind, _pairing, _person), _ => Document("mine"));
        store.Write(NameFor(kind, _otherPairing, _person), _ => Document("mine too"));
        store.Write(theirs, _ => Document("not mine"));

        var controller = new HeldAboutOnePersonController(store);
        var removed = Answer(controller.Remove(_person));

        Assert.Equal(_person, removed.MappedUserId);
        Assert.Equal(2, removed.Removed);

        Assert.Empty(Answer(controller.Report(_person)).Records);
        Assert.Equal(new[] { theirs }, store.Names().ToArray());
    }

    /// <summary>
    /// A removal about somebody this plugin holds nothing for answers zero rather than failing.
    ///
    /// It is the case an operator meets after the first removal, because the person who asked
    /// once asks again, and an endpoint that refused the second call would be reporting an error
    /// about a store that is in exactly the state the caller wanted.
    /// </summary>
    [Fact]
    public void ARemovalAboutSomebodyWithNothingHeldAnswersZero()
    {
        var store = Store();

        store.Write(NameFor(StoredKinds.All[0], _pairing, _person), _ => Document("mine"));

        var removed = Answer(new HeldAboutOnePersonController(store).Remove(_somebodyElse));

        Assert.Equal(0, removed.Removed);
        Assert.Single(store.Names());
    }

    /// <summary>
    /// The value of an answer that carried one, so a fact reads the answer rather than the
    /// wrapper.
    /// </summary>
    /// <typeparam name="T">What the endpoint answers.</typeparam>
    /// <param name="answer">The answer.</param>
    /// <returns>The value.</returns>
    private static T Answer<T>(ActionResult<T> answer)
    {
        Assert.Null(answer.Result);
        Assert.NotNull(answer.Value);

        return answer.Value!;
    }

    private static string NameFor(StoredKind kind, Guid pairingId, Guid mappedUserId) =>
        kind.NamePrefix + Spelled(pairingId) + "-" + Spelled(mappedUserId);

    private static string Spelled(Guid identifier) =>
        identifier.ToString("N", CultureInfo.InvariantCulture);

    private static StoredDocument Document(string who)
    {
        var fields = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
            ["who"] = JsonValue.Create(who),
        };

        return StoredDocument.Read(fields.ToJsonString(), DocumentVersions.Current).Document!;
    }

    private DocumentStore Store()
    {
        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new DocumentStore(new StoreFolder(paths.Object));
    }
}

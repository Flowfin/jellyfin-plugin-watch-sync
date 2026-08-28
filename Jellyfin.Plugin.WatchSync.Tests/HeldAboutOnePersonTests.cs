using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What this plugin can say and can remove about one person, which is #74's second, third and
/// fourth conditions.
///
/// The failure the set is written against is a person being answered by hand. Somebody who asks
/// what is held about them, or asks for it to go, is answered today by an administrator opening
/// files, and what that answer misses is whatever the administrator did not know was there. The
/// two conditions with teeth are therefore about the list being derived rather than typed: the
/// report walks the kinds the store declares, and the removal is checked by scanning what is left
/// rather than by counting what the removal said it did.
///
/// Nothing here reaches a server. The store is a folder this fixture owns, and what a person
/// watched belongs to the server and is not touched by either operation.
/// </summary>
public sealed class HeldAboutOnePersonTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _otherPairing = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _person = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _somebodyElse = new("44444444-4444-4444-4444-444444444444");

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeldAboutOnePersonTests"/> class, with a
    /// directory of its own standing in for what a server would hand over.
    /// </summary>
    public HeldAboutOnePersonTests()
    {
        _programData = TemporaryDirectory.Create("held");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// #74's second condition. The report holds a document of every kind the store declares, and
    /// what it is compared against is that declaration rather than a list written here.
    ///
    /// A list in this file would be the drift the condition exists to refuse, one level in:
    /// somebody adding the fifth kind would add it to the declaration and not here, and this case
    /// would go on passing over four fifths of what is held about a person.
    /// </summary>
    [Fact]
    public void TheReportHoldsADocumentOfEveryKindTheStoreDeclares()
    {
        var store = Store();

        foreach (var kind in StoredKinds.All)
        {
            store.Write(NameFor(kind, _pairing, _person), _ => Document("first"));
        }

        var report = HeldAboutOnePerson.Report(store, _person);

        Assert.Equal(
            StoredKinds.All.Select(kind => kind.NamePrefix).OrderBy(prefix => prefix, StringComparer.Ordinal).ToArray(),
            report.Select(entry => entry.Key.Kind.NamePrefix).OrderBy(prefix => prefix, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The report reaches every pairing, because a person asks about themselves rather than about
    /// a pairing they have never heard of.
    /// </summary>
    [Fact]
    public void TheReportReachesEveryPairingThePersonAppearsUnder()
    {
        var store = Store();
        var kind = StoredKinds.All[0];

        store.Write(NameFor(kind, _pairing, _person), _ => Document("first"));
        store.Write(NameFor(kind, _otherPairing, _person), _ => Document("second"));

        Assert.Equal(
            new[] { _otherPairing, _pairing }.OrderBy(pairing => pairing).ToArray(),
            HeldAboutOnePerson.Report(store, _person)
                .Select(entry => entry.Key.PairingId)
                .OrderBy(pairing => pairing)
                .ToArray());
    }

    /// <summary>
    /// The report carries what each document holds, rather than a row saying that something about
    /// the person exists.
    /// </summary>
    [Fact]
    public void TheReportCarriesWhatEachDocumentHolds()
    {
        var store = Store();
        var name = NameFor(StoredKinds.All[0], _pairing, _person);

        store.Write(name, _ => Document("what was written"));

        var entry = Assert.Single(HeldAboutOnePerson.Report(store, _person));

        Assert.Equal(name, entry.Key.Name);
        Assert.Equal("what was written", (string?)entry.Value.Fields["who"]);
    }

    /// <summary>
    /// A document about somebody else is not in the report, which is the direction that would be
    /// noticed only by the person who received it.
    /// </summary>
    [Fact]
    public void ADocumentAboutSomebodyElseIsNotReported()
    {
        var store = Store();
        var kind = StoredKinds.All[0];

        store.Write(NameFor(kind, _pairing, _person), _ => Document("theirs"));
        store.Write(NameFor(kind, _pairing, _somebodyElse), _ => Document("not theirs"));

        var entry = Assert.Single(HeldAboutOnePerson.Report(store, _person));

        Assert.Equal(_person, entry.Key.MappedUserId);
    }

    /// <summary>
    /// #74's third condition. After a removal, no document left in the store names that person,
    /// and what says so is a scan of the folder rather than the count the removal answered.
    ///
    /// The scan reads the bytes as well as the names. A count is what a removal claims about
    /// itself, and a person told their record is gone has been told something specific.
    /// </summary>
    [Fact]
    public void ARemovalLeavesNoDocumentNamingThatPerson()
    {
        var store = Store();

        foreach (var kind in StoredKinds.All)
        {
            store.Write(NameFor(kind, _pairing, _person), _ => Document(Spelled(_person)));
            store.Write(NameFor(kind, _otherPairing, _person), _ => Document(Spelled(_person)));
            store.Write(NameFor(kind, _pairing, _somebodyElse), _ => Document(Spelled(_somebodyElse)));
        }

        HeldAboutOnePerson.Remove(store, _person);

        foreach (var path in Directory.EnumerateFiles(StorePath))
        {
            Assert.DoesNotContain(Spelled(_person), Path.GetFileName(path), StringComparison.Ordinal);
            Assert.DoesNotContain(Spelled(_person), File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A removal leaves somebody else's documents where they are, which is the half a scan for the
    /// removed person cannot see.
    /// </summary>
    [Fact]
    public void ARemovalLeavesSomebodyElsesDocumentsAlone()
    {
        var store = Store();
        var kind = StoredKinds.All[0];
        var theirs = NameFor(kind, _pairing, _somebodyElse);

        store.Write(NameFor(kind, _pairing, _person), _ => Document("theirs"));
        store.Write(theirs, _ => Document("not theirs"));

        HeldAboutOnePerson.Remove(store, _person);

        Assert.Equal(new[] { theirs }, store.Names().ToArray());
    }

    /// <summary>
    /// The count is of documents that went, which is what #74 asks be recorded.
    /// </summary>
    [Fact]
    public void TheRemovalCountsTheDocumentsThatWent()
    {
        var store = Store();

        foreach (var kind in StoredKinds.All)
        {
            store.Write(NameFor(kind, _pairing, _person), _ => Document("first"));
        }

        store.Write(NameFor(StoredKinds.All[0], _pairing, _somebodyElse), _ => Document("theirs"));

        Assert.Equal(StoredKinds.All.Count, HeldAboutOnePerson.Remove(store, _person));
        Assert.Equal(0, HeldAboutOnePerson.Remove(store, _person));
    }

    /// <summary>
    /// A file a write left in flight is not a document, so it is neither reported nor removed.
    ///
    /// It carries the bytes of a write that replaced nothing. Reporting it would hand somebody a
    /// document nobody ever wrote, and removing it as if it were theirs would be a removal
    /// deciding what a file is by its position in a folder.
    ///
    /// <para>
    /// What is asserted first is that the store does not name it, because that is where the
    /// property lives. The walk over the store would leave it alone anyway, for a second reason:
    /// the file's name carries the attempt number, so it reads as neither of the two identifiers
    /// a document name ends in. Asserting only the walk therefore passes with the store's own
    /// filter taken out, which is how this case was written first and what the run that removed
    /// the filter said about it.
    /// </para>
    /// </summary>
    [Fact]
    public void AFileAWriteLeftInFlightIsNotADocumentAndIsNeitherReportedNorRemoved()
    {
        var store = Store();
        var kind = StoredKinds.All[0];
        var name = NameFor(kind, _pairing, _person);

        store.Write(name, _ => Document("first"));

        var leftBehind = Path.Combine(StorePath, name + ".1.writing");

        File.WriteAllText(leftBehind, "half a document");

        Assert.Equal(new[] { name }, store.Names().ToArray());
        Assert.Single(HeldAboutOnePerson.Report(store, _person));
        Assert.Equal(1, HeldAboutOnePerson.Remove(store, _person));
        Assert.True(File.Exists(leftBehind));
    }

    /// <summary>
    /// A name no kind claims is left alone by both operations.
    ///
    /// The store folder is a folder on somebody's server. A removal that deleted what it could not
    /// name would be deleting on the strength of not recognising it, and the file it does not
    /// recognise is as likely to be an operator's as anybody's.
    /// </summary>
    [Fact]
    public void ANameNoKindClaimsIsLeftAlone()
    {
        var store = Store();
        var strange = "something-else-" + Spelled(_person);

        store.Write(NameFor(StoredKinds.All[0], _pairing, _person), _ => Document("first"));
        store.Write(strange, _ => Document("not this plugin's shape"));

        Assert.Single(HeldAboutOnePerson.Report(store, _person));
        Assert.Equal(1, HeldAboutOnePerson.Remove(store, _person));
        Assert.Equal(new[] { strange }, store.Names().ToArray());
    }

    /// <summary>
    /// A name carrying a known prefix and something other than two identifiers after it is not
    /// read, and is therefore not removed.
    ///
    /// This is the near miss of the reading: the prefix is right and the rest is not, which is
    /// what a name from a scheme this plugin does not use looks like from here.
    /// </summary>
    [Fact]
    public void ANameUnderAKnownPrefixThatIsNotTwoIdentifiersIsNotRead()
    {
        var store = Store();
        var malformed = StoredKinds.All[0].NamePrefix + Spelled(_person);

        store.Write(malformed, _ => Document("not two identifiers"));

        Assert.Empty(HeldAboutOnePerson.Report(store, _person));
        Assert.Equal(0, HeldAboutOnePerson.Remove(store, _person));
        Assert.Equal(new[] { malformed }, store.Names().ToArray());
    }

    /// <summary>
    /// A name carrying a valid pair of identifiers and something after them is not read.
    ///
    /// This is the other side of the length check, and it is the side a one-character mistake
    /// leaves open: a comparison written as at least this long rather than exactly this long
    /// passes every name a longer scheme could produce, and each of those would be read as a
    /// document about whoever the first two identifiers happened to name. The case above, whose
    /// name is too short, is caught either way, so it says nothing about which comparison was
    /// written.
    /// </summary>
    [Fact]
    public void ANameCarryingMoreThanTwoIdentifiersIsNotRead()
    {
        var store = Store();
        var longer = NameFor(StoredKinds.All[0], _pairing, _person) + "-and-then-some";

        store.Write(longer, _ => Document("a scheme this plugin does not use"));

        Assert.Empty(HeldAboutOnePerson.Report(store, _person));
        Assert.Equal(0, HeldAboutOnePerson.Remove(store, _person));
        Assert.Equal(new[] { longer }, store.Names().ToArray());
    }

    /// <summary>
    /// A walk about nobody is refused rather than answered.
    ///
    /// An empty identifier matches no document this plugin writes, so the walk would answer
    /// nothing and the removal would report that nothing is held about the person who asked. That
    /// is a wrong answer to the one question this rule exists to answer, and it looks like a
    /// correct one.
    /// </summary>
    [Fact]
    public void AWalkAboutNobodyIsRefused()
    {
        var store = Store();

        Assert.Throws<ArgumentException>(() => HeldAboutOnePerson.Held(store, Guid.Empty));
        Assert.Throws<ArgumentException>(() => HeldAboutOnePerson.Report(store, Guid.Empty));
        Assert.Throws<ArgumentException>(() => HeldAboutOnePerson.Remove(store, Guid.Empty));
    }

    /// <summary>
    /// #74's fourth condition, as far as a reading of this tree reaches it. Neither operation
    /// takes or answers anything from the adapter this plugin reads and writes a person's record
    /// through, so neither has a path to the server's own user data.
    ///
    /// What this is and what it is not. It is a reading of the surface of this rule rather than an
    /// observation of a running server, so it says that a call cannot reach the server's user data
    /// through this type and not that a server saw no write. Nothing here can make the second
    /// statement: no server of either line is reachable from this suite, which is the headless
    /// rule rather than a gap in this case. What holds the wider property is the
    /// <c>user-data-behind-the-adapter</c> invariant, which refuses the server's manager anywhere
    /// but the adapter.
    /// </summary>
    [Fact]
    public void NeitherOperationReachesTheServersUserData()
    {
        var adapter = typeof(StoredKinds).Assembly
            .GetTypes()
            .Where(type => string.Equals(
                type.Namespace,
                "Jellyfin.Plugin.WatchSync.UserData",
                StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(adapter);

        var reached = typeof(HeldAboutOnePerson)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .SelectMany(Named)
            .Where(adapter.Contains)
            .ToList();

        Assert.Empty(reached);
    }

    private static IEnumerable<Type> Named(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().Prepend(type) : new[] { type };

    private static string Spelled(Guid identifier) =>
        identifier.ToString("n", CultureInfo.InvariantCulture);

    private static string NameFor(StoredKind kind, Guid pairingId, Guid mappedUserId) =>
        kind.NamePrefix + Spelled(pairingId) + "-" + Spelled(mappedUserId);

    private static StoredDocument Document(string who)
    {
        var fields = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
            ["who"] = JsonValue.Create(who),
        };

        return StoredDocument.Read(fields.ToJsonString(), DocumentVersions.Current).Document!;
    }

    private string DataPath => Path.Combine(_programData.FullPath, "data");

    private string StorePath => Path.Combine(DataPath, "watch-sync");

    private DocumentStore Store()
    {
        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new DocumentStore(new StoreFolder(paths.Object));
    }
}
